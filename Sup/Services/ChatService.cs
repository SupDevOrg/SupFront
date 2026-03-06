using Sup.Models;
using Sup.ForTokens;
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

        // Состояние и кэши, которые ранее были в MainChatWindow
        private uint _currentUserId = 0;
        private List<ChatDto> _userChats = new List<ChatDto>();
        private Dictionary<uint, string> _userNameCache = new Dictionary<uint, string>();
        private Dictionary<uint, ChatDto> _pendingChats = new Dictionary<uint, ChatDto>();
        private Dictionary<uint, uint> _chatToOtherUserIdCache = new Dictionary<uint, uint>();
        private List<uint> _lastChatIds = new List<uint>();

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
            {
                _userNameCache[_currentUserId] = username;
            }

            // Предварительно загружаем чаты
            _userChats = await GetUserChatsAsync();
            return _currentUserId;
        }

        public async Task<List<ChatDto>> GetUserChatsAsync()
        {
            if (_currentUserId == 0)
                return new List<ChatDto>();

            var request = new { user_id = _currentUserId };
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_messageServiceBaseUrl}/chat/user", content);
            if (!response.IsSuccessStatusCode)
            {
                // Необработанные ошибки будут обработаны вызывающим кодом
                return new List<ChatDto>();
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var chatsResponse = JsonSerializer.Deserialize<GetUserChatsResponse>(responseContent);
            if (chatsResponse?.Chats == null)
                return new List<ChatDto>();

            _userChats = await LoadChatDetailsAsync(chatsResponse.Chats);
            return _userChats;
        }

        public async Task<List<ChatDto>> LoadChatDetailsAsync(List<ChatInfoDto> chatInfos)
        {
            var validChats = new List<ChatDto>();

            foreach (var chatInfo in chatInfos)
            {
                try
                {
                    uint otherUserId = 0;
                    string userName = chatInfo.Name ?? $"Чат {chatInfo.Id}";
                    string lastMessage = "Нет сообщений";
                    DateTime lastMessageTime = DateTime.Now;

                    // Загружаем сообщения для определения otherUserId и получения последнего сообщения
                    var url = $"{_messageServiceBaseUrl}/messages/{chatInfo.Id}?user_id={_currentUserId}&page=1&page_size=20";
                    var response = await _httpClient.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var messagesResponse = JsonSerializer.Deserialize<MessagesResponse>(content);

                        if (messagesResponse?.Messages != null && messagesResponse.Messages.Count > 0)
                        {
                            Console.WriteLine($"[LoadChatDetailsAsync] Чат {chatInfo.Id} - найдено {messagesResponse.Messages.Count} сообщений");
                            
                            // Находим последнее сообщение (по CreatedAt)
                            var lastMsg = messagesResponse.Messages.OrderByDescending(m => m.CreatedAt).First();
                            lastMessage = lastMsg.Content.Length > 50 ? lastMsg.Content.Substring(0, 50) + "..." : lastMsg.Content;
                            lastMessageTime = lastMsg.CreatedAt;
                            
                            // Определяем otherUserId если его нет в кэше
                            if (!_chatToOtherUserIdCache.TryGetValue(chatInfo.Id, out otherUserId))
                            {
                                var allSenderIds = messagesResponse.Messages.Select(m => m.SenderId).Distinct().ToList();
                                otherUserId = allSenderIds.FirstOrDefault(id => id != _currentUserId);
                                
                                if (otherUserId > 0)
                                {
                                    _chatToOtherUserIdCache[chatInfo.Id] = otherUserId;
                                    Console.WriteLine($"[LoadChatDetailsAsync] Определили otherUserId из сообщений для чата {chatInfo.Id}: {otherUserId}");
                                }
                            }
                        }
                    }

                    // Используем имя из ответа service сообщений если доступно
                    if (!string.IsNullOrEmpty(chatInfo.Name))
                    {
                        userName = chatInfo.Name;
                        // Стараемся кэшировать имя пользователя
                        if (otherUserId > 0)
                        {
                            _userNameCache[otherUserId] = userName;
                        }
                    }

                    // Если у нас всё ещё нет правильного имени, пытаемся его загрузить
                    if (string.IsNullOrEmpty(userName) || userName.StartsWith("Чат "))
                    {
                        if (otherUserId > 0)
                        {
                            var loaded = await GetUserNameByIdAsync(otherUserId);
                            if (!string.IsNullOrEmpty(loaded))
                            {
                                userName = loaded;
                            }
                        }
                    }

                    validChats.Add(new ChatDto
                    {
                        Id = chatInfo.Id,
                        Name = userName,
                        LastMessage = lastMessage,
                        LastMessageTime = lastMessageTime,
                        OtherUserId = otherUserId
                    });
                    
                    Console.WriteLine($"[LoadChatDetailsAsync] Добавлен чат {chatInfo.Id}: Name='{userName}', LastMessage='{lastMessage}', OtherUserId={otherUserId}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LoadChatDetailsAsync] Ошибка обработки чата {chatInfo.Id}: {ex.Message}");
                }
            }

            Console.WriteLine($"[LoadChatDetailsAsync] Загружено всего чатов: {validChats.Count}");
            return validChats;
        }

        private async Task<uint?> GetChatInfoAsync(uint chatId)
        {
            try
            {
                // Пытаемся получить участников чата
                var urls = new[]
                {
                    $"{_messageServiceBaseUrl}/message/chat/{chatId}/members",
                    $"{_messageServiceBaseUrl}/message/chat/{chatId}/participants",
                    $"{_messageServiceBaseUrl}/chat/{chatId}/members",
                    $"{_messageServiceBaseUrl}/members/chat/{chatId}"
                };

                foreach (var url in urls)
                {
                    try
                    {
                        Console.WriteLine($"[GetChatInfoAsync] Запрашиваем участников чата {chatId}: {url}");
                        var response = await _httpClient.GetAsync(url);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            var content = await response.Content.ReadAsStringAsync();
                            Console.WriteLine($"[GetChatInfoAsync] Успешно получены участники чата {chatId} по URL: {url}");
                            Console.WriteLine($"[GetChatInfoAsync] Содержимое ответа (первые 200 символов): {content.Substring(0, Math.Min(200, content.Length))}");
                            
                            using var doc = JsonDocument.Parse(content);
                            var root = doc.RootElement;

                            // Пытаемся найти список участников
                            if (root.TryGetProperty("members", out var membersElem) && membersElem.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                Console.WriteLine($"[GetChatInfoAsync] Найдено поле 'members' с {membersElem.GetArrayLength()} элементами");
                                var otherUserId = FindOtherUserIdFromMembers(membersElem);
                                if (otherUserId > 0)
                                {
                                    Console.WriteLine($"[GetChatInfoAsync] Успешно найден другой пользователь {otherUserId} в чате {chatId}");
                                    return otherUserId;
                                }
                            }
                            else if (root.TryGetProperty("participants", out var participantsElem) && participantsElem.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                Console.WriteLine($"[GetChatInfoAsync] Найдено поле 'participants' с {participantsElem.GetArrayLength()} элементами");
                                var otherUserId = FindOtherUserIdFromMembers(participantsElem);
                                if (otherUserId > 0)
                                {
                                    Console.WriteLine($"[GetChatInfoAsync] Успешно найден другой пользователь {otherUserId} в чате {chatId}");
                                    return otherUserId;
                                }
                            }
                            else if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                // Если сам корень - массив
                                Console.WriteLine($"[GetChatInfoAsync] Корень - массив с {root.GetArrayLength()} элементами");
                                var otherUserId = FindOtherUserIdFromMembers(root);
                                if (otherUserId > 0)
                                {
                                    Console.WriteLine($"[GetChatInfoAsync] Успешно найден другой пользователь {otherUserId} в чате {chatId}");
                                    return otherUserId;
                                }
                            }
                            else
                            {
                                Console.WriteLine($"[GetChatInfoAsync] Не найдена структура members/participants/array в ответе");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"[GetChatInfoAsync] Ошибка запроса {url}: {response.StatusCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GetChatInfoAsync] Исключение при запросе {url}: {ex.Message}");
                    }
                }
                
                Console.WriteLine($"[GetChatInfoAsync] Не удалось получить участников чата {chatId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetChatInfoAsync] Ошибка при получении участников чата {chatId}: {ex.Message}");
            }

            return null;
        }

        private uint FindOtherUserIdFromMembers(JsonElement membersArray)
        {
            try
            {
                int memberCount = 0;
                foreach (var member in membersArray.EnumerateArray())
                {
                    memberCount++;
                    uint memberId = 0;
                    
                    // Пытаемся найти ID участника
                    if (member.TryGetProperty("id", out var idElem))
                        memberId = idElem.GetUInt32();
                    else if (member.TryGetProperty("user_id", out var userIdElem))
                        memberId = userIdElem.GetUInt32();
                    else if (member.TryGetProperty("userId", out var userIdElem2))
                        memberId = userIdElem2.GetUInt32();
                    else if (member.ValueKind == System.Text.Json.JsonValueKind.Number)
                        memberId = member.GetUInt32();

                    Console.WriteLine($"[FindOtherUserIdFromMembers] Участник #{memberCount}: memberId={memberId}, currentUserId={_currentUserId}");

                    // Если это не текущий пользователь, вернули его ID
                    if (memberId > 0 && memberId != _currentUserId)
                    {
                        Console.WriteLine($"[FindOtherUserIdFromMembers] Выбран другой пользователь: {memberId}");
                        return memberId;
                    }
                }
                Console.WriteLine($"[FindOtherUserIdFromMembers] Всего участников: {memberCount}, других пользователей не найдено");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FindOtherUserIdFromMembers] Ошибка парсинга: {ex.Message}");
            }

            return 0;
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
                    var resp = await JsonSerializer.DeserializeAsync<SearchUsersResponse>(stream);
                    var user = resp?.Users?.FirstOrDefault(u => u.Id == userId);
                    if (user != null)
                    {
                        _userNameCache[userId] = user.Username;
                        return user.Username;
                    }
                }

                // Агрессивный поиск
                var searchTerms = "abcdefghijklmnopqrstuvwxyz0123456789".Select(c => c.ToString());
                var tasks = searchTerms.Select(async term =>
                {
                    try
                    {
                        url = $"{App.ApiBaseUrl}user/{Uri.EscapeDataString(term)}?page=0&size=1000";
                        var resp2 = await _httpClient.GetAsync(url);
                        if (resp2.IsSuccessStatusCode)
                        {
                            using var stream2 = await resp2.Content.ReadAsStreamAsync();
                            var sr = await JsonSerializer.DeserializeAsync<SearchUsersResponse>(stream2);
                            return sr?.Users?.FirstOrDefault(u => u.Id == userId);
                        }
                    }
                    catch { }
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
            catch { }

            return null;
        }

        public async Task<uint?> GetUserIdByNameAsync(string name)
        {
            try
            {
                var url = $"{App.ApiBaseUrl}user/{Uri.EscapeDataString(name)}?page=0&size=8";
                var resp = await _httpClient.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return null;
                using var stream = await resp.Content.ReadAsStreamAsync();
                var result = await JsonSerializer.DeserializeAsync<SearchUsersResponse>(stream);
                var user = result?.Users?.FirstOrDefault(u => u.Username == name);
                return user?.Id != null ? (uint?)user.Id : null;
            }
            catch { return null; }
        }

        public async Task<uint?> CreateChatAsync(uint otherUserId)
        {
            try
            {
                var req = new { user_id_1 = _currentUserId, user_id_2 = otherUserId };
                var json = JsonSerializer.Serialize(req);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var resp = await _httpClient.PostAsync($"{_messageServiceBaseUrl}/chat/bytwouser", content);
                if (!resp.IsSuccessStatusCode)
                    return null;
                var respJson = await resp.Content.ReadAsStringAsync();
                using var d = JsonDocument.Parse(respJson);
                return d.RootElement.GetProperty("chat_id").GetUInt32();
            }
            catch { return null; }
        }

        public void PreCacheChatInfo(uint chatId, uint otherUserId, string username)
        {
            _chatToOtherUserIdCache[chatId] = otherUserId;
            _userNameCache[otherUserId] = username;
            Console.WriteLine($"[ChatService.PreCacheChatInfo] Кэшировано: chatId={chatId}, otherUserId={otherUserId}, username={username}");
        }

        public Task<uint> GetOtherUserIdForChat(uint chatId)
        {
            // Сначала ищем в загруженных чатах
            var chat = _userChats.FirstOrDefault(c => c.Id == chatId);
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
                var url = $"{_messageServiceBaseUrl}/messages/{chatId}?user_id={_currentUserId}&page={page}&page_size={pageSize}";
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[ChatService.LoadChatHistoryAsync] Ошибка загрузки истории чата {chatId}: {response.StatusCode}");
                    return result;
                }
                var responseContent = await response.Content.ReadAsStringAsync();
                var messagesResponse = JsonSerializer.Deserialize<MessagesResponse>(responseContent);
                if (messagesResponse?.Messages != null)
                {
                    result = messagesResponse.Messages.OrderBy(m => m.CreatedAt).ToList();
                    // Предварительная загрузка имён
                    var ids = result.Where(m => m.SenderId != _currentUserId).Select(m => m.SenderId).Distinct();
                    foreach (var id in ids)
                    {
                        if (!_userNameCache.ContainsKey(id))
                            await GetUserNameByIdAsync(id);
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
            if (_currentUserId == 0)
            {
                Console.WriteLine("[ChatService.SendMessageAsync] currentUserId=0, сообщение не отправлено");
                return null;
            }

            if (chatId == 0 || string.IsNullOrWhiteSpace(content))
                return null;

            var payload = new
            {
                chat_id = chatId,
                sender_id = _currentUserId,
                content = content
            };

            var json = JsonSerializer.Serialize(payload);

            // Пробуем несколько вариантов эндпоинтов — на разных сборках backend они различаются.
            var urls = new[]
            {
                $"{_messageServiceBaseUrl}/messages/{chatId}?user_id={_currentUserId}",
                $"{_messageServiceBaseUrl}/messages/{chatId}",
                $"{_messageServiceBaseUrl}/messages",
                $"{_messageServiceBaseUrl}/message",
                $"{_messageServiceBaseUrl}/send",
                $"{_messageServiceBaseUrl}/chat/{chatId}/messages",
                $"{_messageServiceBaseUrl}/chat/{chatId}/message",
            };

            foreach (var url in urls)
            {
                try
                {
                    var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                    var resp = await _httpClient.PostAsync(url, httpContent);
                    if (!resp.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[ChatService.SendMessageAsync] POST {url} -> {resp.StatusCode}");
                        continue;
                    }

                    var respJson = await resp.Content.ReadAsStringAsync();
                    if (string.IsNullOrWhiteSpace(respJson))
                    {
                        Console.WriteLine($"[ChatService.SendMessageAsync] POST {url} -> OK, но пустой ответ");
                        return new MessageDto
                        {
                            Id = 0,
                            ChatId = chatId,
                            SenderId = _currentUserId,
                            Content = content,
                            CreatedAt = DateTime.Now
                        };
                    }

                    // Вариант 1: сервер вернул MessageDto напрямую
                    try
                    {
                        var msg = JsonSerializer.Deserialize<MessageDto>(respJson);
                        if (msg != null && !string.IsNullOrWhiteSpace(msg.Content))
                            return msg;
                    }
                    catch { }

                    // Вариант 2: сервер вернул объект-обёртку
                    try
                    {
                        using var doc = JsonDocument.Parse(respJson);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("message", out var messageElem))
                        {
                            var msg = JsonSerializer.Deserialize<MessageDto>(messageElem.GetRawText());
                            if (msg != null && !string.IsNullOrWhiteSpace(msg.Content))
                                return msg;
                        }

                        if (root.TryGetProperty("id", out _))
                        {
                            var msg = JsonSerializer.Deserialize<MessageDto>(root.GetRawText());
                            if (msg != null && !string.IsNullOrWhiteSpace(msg.Content))
                                return msg;
                        }
                    }
                    catch { }

                    // Если формат ответа неизвестен, хотя бы считаем, что сообщение сохранено.
                    Console.WriteLine($"[ChatService.SendMessageAsync] POST {url} -> OK (неизвестный формат ответа)");
                    return new MessageDto
                    {
                        Id = 0,
                        ChatId = chatId,
                        SenderId = _currentUserId,
                        Content = content,
                        CreatedAt = DateTime.Now
                    };
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ChatService.SendMessageAsync] Исключение при POST {url}: {ex.Message}");
                }
            }

            return null;
        }

        public async Task<bool> PollForChatListChangesAsync()
        {
            if (_currentUserId == 0) return false;
            var request = new { user_id = _currentUserId };
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_messageServiceBaseUrl}/chat/user", content);
            if (!response.IsSuccessStatusCode) return false;
            var responseContent = await response.Content.ReadAsStringAsync();
            var chatsResponse = JsonSerializer.Deserialize<GetUserChatsResponse>(responseContent);
            if (chatsResponse?.Chats == null) return false;
            
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
    }
}