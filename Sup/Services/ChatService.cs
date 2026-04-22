using Sup.ForTokens;
using Sup.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sup.Services
{
    public class ChatService : IChatService
    {
        private readonly HttpClient _httpClient;
        private readonly string _messageServiceBaseUrl = $"{App.ApiBaseUrl}message";
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private uint _currentUserId = 0;
        private List<ChatDto> _userChats = new();
        private readonly Dictionary<uint, string> _userNameCache = new();
        private readonly Dictionary<uint, uint> _chatToOtherUserIdCache = new();
        private readonly Dictionary<uint, List<ChatParticipantDto>> _chatParticipantsCache = new();
        private List<uint> _lastChatIds = new();

        public ChatService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? HttpClientFactory.CreateAuthenticatedClient();
        }

        public uint CurrentUserId => _currentUserId;

        public async Task<uint> InitializeUserAsync(string username)
        {
            if (string.IsNullOrEmpty(username))
                throw new ArgumentException("Username cannot be empty", nameof(username));

            _currentUserId = await GetUserIdByNameAsync(username) ?? 0;
            if (_currentUserId > 0)
                _userNameCache[_currentUserId] = username;

            _userChats = await GetUserChatsAsync();
            _lastChatIds = _userChats.Select(c => c.Id).ToList();
            return _currentUserId;
        }

        public async Task<List<ChatDto>> GetUserChatsAsync()
        {
            if (_currentUserId == 0)
                return new List<ChatDto>();

            var response = await SendWithUserHeaderAsync(HttpMethod.Get, $"{_messageServiceBaseUrl}/chats");
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[ChatService.GetUserChatsAsync] GET {_messageServiceBaseUrl}/chats -> {response.StatusCode}");
                return new List<ChatDto>();
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var chatsResponse = JsonSerializer.Deserialize<GetUserChatsResponse>(responseContent, _jsonOptions);
            if (chatsResponse?.Chats == null)
                return new List<ChatDto>();

            _userChats = await LoadChatDetailsAsync(chatsResponse.Chats);
            _lastChatIds = _userChats.Select(c => c.Id).ToList();
            return _userChats;
        }

        public async Task<List<ChatDto>> LoadChatDetailsAsync(List<ChatInfoDto> chatInfos)
        {
            var validChats = new List<ChatDto>();

            foreach (var chatInfo in chatInfos)
            {
                try
                {
                    var lastMessage = !string.IsNullOrWhiteSpace(chatInfo.LastMessage)
                        ? TrimMessage(chatInfo.LastMessage)
                        : "Нет сообщений";
                    var lastMessageTime = chatInfo.LastMessageTime != default ? chatInfo.LastMessageTime : DateTime.Now;
                    var participants = new List<ChatParticipantDto>();
                    uint otherUserId = 0;

                    var messages = await LoadChatHistoryAsync(chatInfo.Id, 1, 20);
                    if (messages.Count > 0)
                    {
                        var lastMsg = messages.OrderByDescending(m => m.CreatedAt).First();
                        lastMessage = TrimMessage(lastMsg.Content);
                        lastMessageTime = lastMsg.CreatedAt;

                        if (!chatInfo.IsGroup && !_chatToOtherUserIdCache.TryGetValue(chatInfo.Id, out otherUserId))
                        {
                            otherUserId = messages
                                .Select(m => m.SenderId)
                                .Distinct()
                                .FirstOrDefault(id => id != _currentUserId);

                            if (otherUserId > 0)
                                _chatToOtherUserIdCache[chatInfo.Id] = otherUserId;
                        }
                    }

                    if (chatInfo.IsGroup)
                    {
                        participants = await GetChatParticipantsAsync(chatInfo.Id);
                    }
                    else
                    {
                        if (!_chatToOtherUserIdCache.TryGetValue(chatInfo.Id, out otherUserId))
                        {
                            otherUserId = await GetChatInfoAsync(chatInfo.Id) ?? 0;
                            if (otherUserId > 0)
                                _chatToOtherUserIdCache[chatInfo.Id] = otherUserId;
                        }
                    }

                    var chatName = chatInfo.Name;
                    if (chatInfo.IsGroup)
                    {
                        chatName = !string.IsNullOrWhiteSpace(chatName)
                            ? chatName
                            : BuildGroupDisplayName(participants);
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(chatName) && otherUserId > 0)
                        {
                            _userNameCache[otherUserId] = chatName;
                        }
                        else if (otherUserId > 0)
                        {
                            chatName = await GetUserNameByIdAsync(otherUserId) ?? $"Чат {chatInfo.Id}";
                        }
                        else
                        {
                            chatName = $"Чат {chatInfo.Id}";
                        }
                    }

                    validChats.Add(new ChatDto
                    {
                        Id = chatInfo.Id,
                        Name = chatName,
                        LastMessage = lastMessage,
                        LastMessageTime = lastMessageTime,
                        OtherUserId = chatInfo.IsGroup ? 0 : otherUserId,
                        IsGroup = chatInfo.IsGroup,
                        Participants = CloneParticipants(participants)
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LoadChatDetailsAsync] Ошибка обработки чата {chatInfo.Id}: {ex.Message}");
                }
            }

            return validChats;
        }

        public async Task<uint?> CreateChatAsync(uint otherUserId)
        {
            try
            {
                if (_currentUserId == 0 || otherUserId == 0)
                    return null;

                var json = JsonSerializer.Serialize(new { user_id = otherUserId }, _jsonOptions);
                var url = $"{_messageServiceBaseUrl}/chats";
                var resp = await SendWithUserHeaderAsync(
                    HttpMethod.Post,
                    url,
                    new StringContent(json, Encoding.UTF8, "application/json"));

                if (!resp.IsSuccessStatusCode)
                {
                    var errorBody = await resp.Content.ReadAsStringAsync();
                    Console.WriteLine($"[ChatService.CreateChatAsync] POST {url} -> {resp.StatusCode}. Body: {errorBody}");
                    return null;
                }

                var respJson = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(respJson);
                if (TryExtractChatId(doc.RootElement, out var chatId))
                    return chatId;

                Console.WriteLine("[ChatService.CreateChatAsync] Не удалось извлечь chat_id из ответа");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ChatService.CreateChatAsync] Исключение: {ex.Message}");
                return null;
            }
        }

        public async Task<uint?> CreateGroupChatAsync(IEnumerable<uint> memberIds)
        {
            try
            {
                var ids = memberIds.Where(id => id > 0).Distinct().ToList();
                if (_currentUserId == 0 || ids.Count < 2)
                    return null;

                var request = CreateGroupChatRequest.FromIds(ids);
                var json = JsonSerializer.Serialize(request, _jsonOptions);
                var url = $"{_messageServiceBaseUrl}/chats/group";
                var resp = await SendWithUserHeaderAsync(
                    HttpMethod.Post,
                    url,
                    new StringContent(json, Encoding.UTF8, "application/json"));

                if (!resp.IsSuccessStatusCode)
                {
                    var errorBody = await resp.Content.ReadAsStringAsync();
                    Console.WriteLine($"[ChatService.CreateGroupChatAsync] POST {url} -> {resp.StatusCode}. Body: {errorBody}");
                    return null;
                }

                var respJson = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(respJson);
                if (!TryExtractChatId(doc.RootElement, out var chatId))
                {
                    Console.WriteLine("[ChatService.CreateGroupChatAsync] Не удалось извлечь chat_id из ответа");
                    return null;
                }

                var seededParticipants = ids
                    .Select(id => new ChatParticipantDto
                    {
                        Id = id,
                        Username = _userNameCache.TryGetValue(id, out var name) ? name : string.Empty
                    })
                    .ToList();
                seededParticipants.Insert(0, new ChatParticipantDto
                {
                    Id = _currentUserId,
                    Username = _userNameCache.TryGetValue(_currentUserId, out var currentName) ? currentName : string.Empty
                });

                _chatParticipantsCache[chatId] = await HydrateParticipantsAsync(seededParticipants);
                return chatId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ChatService.CreateGroupChatAsync] Исключение: {ex.Message}");
                return null;
            }
        }

        public async Task<List<ChatParticipantDto>> GetChatMembersAsync(uint chatId)
        {
            if (chatId == 0 || _currentUserId == 0)
                return new List<ChatParticipantDto>();

            if (_chatParticipantsCache.TryGetValue(chatId, out var cachedMembers) && cachedMembers.Count > 0)
                return CloneParticipants(cachedMembers);

            try
            {
                var url = $"{_messageServiceBaseUrl}/chats/{chatId}/members";
                var response = await SendWithUserHeaderAsync(HttpMethod.Get, url);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[ChatService.GetChatMembersAsync] GET {url} -> {response.StatusCode}");
                    return new List<ChatParticipantDto>();
                }

                var content = await response.Content.ReadAsStringAsync();
                List<ChatParticipantDto> members;
                try
                {
                    var membersResponse = JsonSerializer.Deserialize<GetChatMembersResponse>(content, _jsonOptions);
                    members = membersResponse?.Members ?? new List<ChatParticipantDto>();
                }
                catch
                {
                    members = new List<ChatParticipantDto>();
                }

                if (members.Count == 0)
                {
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("members", out var membersElem) && membersElem.ValueKind == JsonValueKind.Array)
                        members = ParseMembers(membersElem);
                }

                var hydratedMembers = await HydrateParticipantsAsync(members);
                _chatParticipantsCache[chatId] = hydratedMembers;
                return CloneParticipants(hydratedMembers);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ChatService.GetChatMembersAsync] Исключение: {ex.Message}");
                return new List<ChatParticipantDto>();
            }
        }

        public async Task<List<ChatParticipantDto>> GetChatParticipantsAsync(uint chatId)
        {
            return await GetChatMembersAsync(chatId);
        }

        public async Task<bool> AddUsersToChatAsync(uint chatId, IEnumerable<uint> userIds)
        {
            try
            {
                var ids = userIds.Where(id => id > 0).Distinct().ToList();
                if (_currentUserId == 0 || chatId == 0 || ids.Count == 0)
                    return false;

                var request = AddUsersToChatRequest.FromIds(ids);
                var json = JsonSerializer.Serialize(request, _jsonOptions);
                var url = $"{_messageServiceBaseUrl}/chats/{chatId}/members";
                var resp = await SendWithUserHeaderAsync(
                    HttpMethod.Post,
                    url,
                    new StringContent(json, Encoding.UTF8, "application/json"));

                if (!resp.IsSuccessStatusCode)
                {
                    var errorBody = await resp.Content.ReadAsStringAsync();
                    Console.WriteLine($"[ChatService.AddUsersToChatAsync] POST {url} -> {resp.StatusCode}. Body: {errorBody}");
                    return false;
                }

                _chatParticipantsCache.Remove(chatId);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ChatService.AddUsersToChatAsync] Исключение: {ex.Message}");
                return false;
            }
        }

        public void PreCacheChatInfo(uint chatId, uint otherUserId, string username)
        {
            _chatToOtherUserIdCache[chatId] = otherUserId;
            _userNameCache[otherUserId] = username;
        }

        public Task<uint> GetOtherUserIdForChat(uint chatId)
        {
            var chat = _userChats.FirstOrDefault(c => c.Id == chatId && !c.IsGroup);
            if (chat != null && chat.OtherUserId > 0)
                return Task.FromResult(chat.OtherUserId);

            if (_chatToOtherUserIdCache.TryGetValue(chatId, out var cached))
                return Task.FromResult(cached);

            return Task.FromResult<uint>(0);
        }

        public async Task<List<MessageDto>> LoadChatHistoryAsync(uint chatId, int page = 1, int pageSize = 20)
        {
            var result = new List<MessageDto>();
            try
            {
                var url = $"{_messageServiceBaseUrl}/messages/{chatId}?page={page}&page_size={pageSize}";
                var resp = await SendWithUserHeaderAsync(HttpMethod.Get, url);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    Console.WriteLine($"[ChatService.LoadChatHistoryAsync] GET {url} -> {resp.StatusCode}. Body: {body}");
                    return result;
                }

                var responseContent = await resp.Content.ReadAsStringAsync();
                var messagesResponse = JsonSerializer.Deserialize<MessagesResponse>(responseContent, _jsonOptions);
                if (messagesResponse?.Messages != null)
                {
                    result = messagesResponse.Messages.OrderBy(m => m.CreatedAt).ToList();
                    var senderIds = result.Where(m => m.SenderId != _currentUserId).Select(m => m.SenderId).Distinct();
                    foreach (var senderId in senderIds)
                    {
                        if (!_userNameCache.ContainsKey(senderId))
                            await GetUserNameByIdAsync(senderId);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ChatService.LoadChatHistoryAsync] Исключение: {ex.Message}");
            }
            return result;
        }

        public async Task<MessageDto?> SendMessageAsync(uint chatId, string content)
        {
            if (_currentUserId == 0 || chatId == 0 || string.IsNullOrWhiteSpace(content))
                return null;

            Console.WriteLine("[ChatService.SendMessageAsync] REST-эндпоинт отправки сообщения отсутствует, используем WebSocket fallback");
            return null;
        }

        public async Task<string?> GetUserNameByIdAsync(uint userId)
        {
            if (_userNameCache.TryGetValue(userId, out var cached))
                return cached;

            try
            {
                var url = $"{App.ApiBaseUrl}user/{Uri.EscapeDataString(userId.ToString())}?page=0&size=100";
                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync();
                    var resp = await JsonSerializer.DeserializeAsync<SearchUsersResponse>(stream, _jsonOptions);
                    var user = resp?.Users?.FirstOrDefault(u => u.Id == userId);
                    if (user != null)
                    {
                        _userNameCache[userId] = user.Username;
                        return user.Username;
                    }
                }

                var searchTerms = "abcdefghijklmnopqrstuvwxyz0123456789".Select(c => c.ToString());
                var tasks = searchTerms.Select(async term =>
                {
                    try
                    {
                        var searchUrl = $"{App.ApiBaseUrl}user/{Uri.EscapeDataString(term)}?page=0&size=1000";
                        var resp2 = await _httpClient.GetAsync(searchUrl);
                        if (resp2.IsSuccessStatusCode)
                        {
                            using var stream2 = await resp2.Content.ReadAsStreamAsync();
                            var sr = await JsonSerializer.DeserializeAsync<SearchUsersResponse>(stream2, _jsonOptions);
                            return sr?.Users?.FirstOrDefault(u => u.Id == userId);
                        }
                    }
                    catch
                    {
                    }

                    return null;
                });

                var results = await Task.WhenAll(tasks);
                var found = results.FirstOrDefault(u => u != null);
                if (found != null)
                {
                    _userNameCache[userId] = found.Username;
                    return found.Username;
                }
            }
            catch
            {
            }

            return null;
        }

        public async Task<uint?> GetUserIdByNameAsync(string name)
        {
            try
            {
                var url = $"{App.ApiBaseUrl}user/{Uri.EscapeDataString(name)}?page=0&size=8";
                var resp = await _httpClient.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                    return null;

                using var stream = await resp.Content.ReadAsStreamAsync();
                var result = await JsonSerializer.DeserializeAsync<SearchUsersResponse>(stream, _jsonOptions);
                var user = result?.Users?.FirstOrDefault(u => u.Username == name);
                return user?.Id != null ? (uint?)user.Id : null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> PollForChatListChangesAsync()
        {
            if (_currentUserId == 0)
                return false;

            var response = await SendWithUserHeaderAsync(HttpMethod.Get, $"{_messageServiceBaseUrl}/chats");
            if (!response.IsSuccessStatusCode)
                return false;

            var responseContent = await response.Content.ReadAsStringAsync();
            var chatsResponse = JsonSerializer.Deserialize<GetUserChatsResponse>(responseContent, _jsonOptions);
            if (chatsResponse?.Chats == null)
                return false;

            var set = new HashSet<uint>(chatsResponse.Chats.Select(c => c.Id));
            var lastSet = new HashSet<uint>(_lastChatIds);

            if (!set.SetEquals(lastSet))
            {
                _lastChatIds = chatsResponse.Chats.Select(c => c.Id).ToList();
                _userChats = await LoadChatDetailsAsync(chatsResponse.Chats);
                return true;
            }

            return false;
        }

        private async Task<uint?> GetChatInfoAsync(uint chatId)
        {
            try
            {
                var members = await GetChatMembersAsync(chatId);
                var otherUserId = members
                    .Select(member => member.Id)
                    .FirstOrDefault(id => id > 0 && id != _currentUserId);
                return otherUserId > 0 ? otherUserId : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ChatService.GetChatInfoAsync] Ошибка: {ex.Message}");
                return null;
            }
        }

        private async Task<List<ChatParticipantDto>> HydrateParticipantsAsync(IEnumerable<ChatParticipantDto> participants)
        {
            var hydrated = participants
                .Where(participant => participant.Id > 0)
                .GroupBy(participant => participant.Id)
                .Select(group => new ChatParticipantDto
                {
                    Id = group.Key,
                    Username = group.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Username))?.Username ?? string.Empty
                })
                .ToList();

            foreach (var participant in hydrated)
            {
                if (!string.IsNullOrWhiteSpace(participant.Username))
                {
                    _userNameCache[participant.Id] = participant.Username;
                    continue;
                }

                if (participant.Id == _currentUserId && _userNameCache.TryGetValue(_currentUserId, out var currentUserName))
                {
                    participant.Username = currentUserName;
                    continue;
                }

                participant.Username = await GetUserNameByIdAsync(participant.Id) ?? $"Пользователь {participant.Id}";
            }

            return hydrated;
        }

        private List<ChatParticipantDto> ParseMembers(JsonElement membersArray)
        {
            var members = new List<ChatParticipantDto>();

            foreach (var member in membersArray.EnumerateArray())
            {
                if (member.ValueKind == JsonValueKind.Number)
                {
                    if (member.TryGetUInt32(out var numericId))
                        members.Add(new ChatParticipantDto { Id = numericId });
                    continue;
                }

                if (member.ValueKind != JsonValueKind.Object)
                    continue;

                try
                {
                    var parsedMember = JsonSerializer.Deserialize<ChatParticipantDto>(member.GetRawText(), _jsonOptions) ?? new ChatParticipantDto();
                    if (parsedMember.Id > 0)
                        members.Add(parsedMember);
                }
                catch
                {
                }
            }

            return members;
        }

        private static bool TryExtractChatId(JsonElement root, out uint chatId)
        {
            chatId = 0;

            if (root.TryGetProperty("chat_id", out var chatIdElem))
            {
                if (chatIdElem.ValueKind == JsonValueKind.Number && chatIdElem.TryGetUInt32(out chatId))
                    return true;

                if (chatIdElem.ValueKind == JsonValueKind.Object &&
                    chatIdElem.TryGetProperty("id", out var nestedIdElem) &&
                    nestedIdElem.TryGetUInt32(out chatId))
                {
                    return true;
                }
            }

            if (root.TryGetProperty("id", out var idElem) && idElem.TryGetUInt32(out chatId))
                return true;

            if (root.TryGetProperty("chat", out var chatElem) &&
                chatElem.ValueKind == JsonValueKind.Object &&
                chatElem.TryGetProperty("id", out var chatObjIdElem) &&
                chatObjIdElem.TryGetUInt32(out chatId))
            {
                return true;
            }

            return false;
        }

        private string BuildGroupDisplayName(List<ChatParticipantDto> participants)
        {
            var participantNames = participants
                .Where(participant => participant.Id != _currentUserId)
                .Select(participant => participant.DisplayName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct()
                .ToList();

            if (participantNames.Count == 0)
                return "Групповой чат";

            if (participantNames.Count == 1)
                return participantNames[0];

            if (participantNames.Count == 2)
                return string.Join(", ", participantNames);

            return $"{participantNames[0]}, {participantNames[1]} +{participantNames.Count - 2}";
        }

        private static string TrimMessage(string content)
        {
            return content.Length > 50 ? content.Substring(0, 50) + "..." : content;
        }

        private static List<ChatParticipantDto> CloneParticipants(IEnumerable<ChatParticipantDto> participants)
        {
            return participants.Select(participant => new ChatParticipantDto
            {
                Id = participant.Id,
                Username = participant.Username
            }).ToList();
        }

        private async Task<HttpResponseMessage> SendWithUserHeaderAsync(HttpMethod method, string url, HttpContent? content = null)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.TryAddWithoutValidation("X-Auth-User-ID", _currentUserId.ToString());
            request.Content = content;
            return await _httpClient.SendAsync(request);
        }
    }
}
