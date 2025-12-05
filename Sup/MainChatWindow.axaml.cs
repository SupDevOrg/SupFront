using Avalonia.Controls;
using Avalonia.Interactivity;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Sup.ForTokens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Net.WebSockets;
using System.Threading;
using System.Text;

namespace Sup
{
    public partial class MainChatWindow : Window
    {
        private readonly HttpClient _httpClient = HttpClientFactory.CreateAuthenticatedClient();
        private ClientWebSocket? _currentWs;
        private CancellationTokenSource? _currentWsToken;
        private Task? _currentWsListenTask;
        private bool _isVoiceTestActive = false;
        private WasapiCapture? _audioCapture;
        private WasapiOut? _audioPlayback;
        private int _currentPage = 0;
        private int _totalPages = 1;
        private string _currentSearchQuery = string.Empty;
        private float _smoothVolume = 0;

        // Поля для работы с чатами
        private List<ChatDto> _userChats = new List<ChatDto>();
        private uint _currentUserId = 1; // Будет загружен через поиск по username
        private uint? _currentChatId = null;
        private string _currentChatUserName = string.Empty; // Отображаемое имя
        private string _currentUsername = string.Empty; // Username текущего пользователя
        private Dictionary<uint, string> _userNameCache = new Dictionary<uint, string>(); // Кэш имен пользователей
        private System.Timers.Timer? _chatListTimer;
        private List<uint> _lastChatIds = new List<uint>(); // для отслеживания изменений

        // Базовый URL для message service
        private readonly string _messageServiceBaseUrl = "http://109.73.194.181:80/api/v1/message";

        // Хранилище временных чатов (еще не созданных на сервере)
        private Dictionary<uint, ChatDto> _pendingChats = new Dictionary<uint, ChatDto>();

        // Кэш для связи chatId -> otherUserId (чтобы не терять информацию после создания чата)
        private Dictionary<uint, uint> _chatToOtherUserIdCache = new Dictionary<uint, uint>();

        public MainChatWindow() : this("user")
        {
        }

        public MainChatWindow(string username)
        {
            InitializeComponent();
            ResetPagination();

            _currentUsername = username;
            UserNameLabel.Text = username;

            // События для кнопок
            SearchUsersButton.Click += OnSearchUsersClicked;
            BackToChatButton.Click += OnBackToChatClicked;
            SearchGlobalTextBox.KeyUp += async (s, e) => await OnSearchUsersAsync();
            GlobalUsersListBox.DoubleTapped += OnGlobalUserSelected;
            SettingsButton.Click += OnSettingsClicked;
            VoiceSettingsButton.Click += OnVoiceSettingsClicked;
            BackFromSettingsButton.Click += OnBackFromSettingsClicked;
            TestVoiceButton.Click += OnTestVoiceClicked;

            // Обработчики для кнопок пагинации
            PrevPageButton.Click += OnPrevPageClick;
            NextPageButton.Click += OnNextPageClick;

            // Новые обработчики для чата
            SendMessageButton.Click += OnSendMessageClicked;
            UsersListBox.SelectionChanged += OnChatSelected;

            // Загружаем user_id через поиск по username и чаты при инициализации
            _ = InitializeUserDataAsync();

            _chatListTimer = new System.Timers.Timer(1000);
            _chatListTimer.Elapsed += async (s, e) => await ChatListPollingAsync();
            _chatListTimer.AutoReset = true;
            _chatListTimer.Start();
        }

        /// <summary>
        /// Инициализация данных пользователя (user_id через поиск по username)
        /// </summary>
        private async Task InitializeUserDataAsync()
        {
            try
            {
                var url = $"{App.ApiBaseUrl}user/{Uri.EscapeDataString(_currentUsername)}?page=0&size=10";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync();
                    var searchResponse = await JsonSerializer.DeserializeAsync<SearchUsersResponse>(stream);

                    if (searchResponse?.Users != null)
                    {
                        var currentUser = searchResponse.Users.FirstOrDefault(u =>
                            string.Equals(u.Username, _currentUsername, StringComparison.OrdinalIgnoreCase));

                        if (currentUser != null)
                        {
                            _currentUserId = (uint)currentUser.Id;
                            _userNameCache[_currentUserId] = currentUser.Username;
                            Console.WriteLine($"Загружен user_id: {_currentUserId} для пользователя {_currentUsername}");
                        }
                        else
                        {
                            Console.WriteLine($"Пользователь {_currentUsername} не найден в результатах поиска");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"Ошибка при поиске пользователя {_currentUsername}: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка загрузки user_id для {_currentUsername}: {ex.Message}");
            }

            await LoadUserChatsAsync();
        }

        // МЕТОДЫ ДЛЯ РАБОТЫ С ЧАТАМИ ////////////////////////////////////

        /// <summary>
        /// Загрузка списка чатов пользователя
        /// </summary>
        private async Task LoadUserChatsAsync()
        {
            try
            {
                Console.WriteLine($"Начинаем загрузку чатов для пользователя {_currentUserId}");

                var request = new { user_id = _currentUserId };
                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_messageServiceBaseUrl}/chat/user", content);

                Console.WriteLine($"Ответ от сервера: {response.StatusCode}");

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    Console.WriteLine("Получен 401 даже после попытки обновления токена");
                    return;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Ошибка загрузки чатов: {response.StatusCode}, {errorContent}");
                    return;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Получены данные чатов: {responseContent}");

                var chatsResponse = JsonSerializer.Deserialize<GetUserChatsResponse>(responseContent);

                if (chatsResponse != null && chatsResponse.Chats != null)
                {
                    _userChats = await LoadChatDetailsAsync(chatsResponse.Chats);
                    UpdateChatsList();
                    Console.WriteLine($"Успешно загружено {_userChats.Count} чатов");
                }
                else
                {
                    Console.WriteLine("Получен пустой список чатов или ошибка десериализации");
                }
            }
            catch (HttpRequestException httpEx)
            {
                Console.WriteLine($"Сетевая ошибка при загрузке чатов: {httpEx.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Исключение при загрузке чатов: {ex.Message}");
            }
        }

        /// <summary>
        /// Загрузка деталей для каждого чата с именами пользователей
        /// </summary>
        private async Task<List<ChatDto>> LoadChatDetailsAsync(List<uint> chatIds)
        {
            var validChats = new List<ChatDto>();

            foreach (var chatId in chatIds)
            {
                try
                {
                    Console.WriteLine($"Загружаем детали чата {chatId}");

                    // Сначала проверяем кэш связей чат -> собеседник
                    uint otherUserId = 0;
                    string userName = $"Чат {chatId}";
                    string lastMessage = "Нет сообщений";
                    DateTime lastMessageTime = DateTime.Now;

                    if (_chatToOtherUserIdCache.TryGetValue(chatId, out var cachedOtherUserId))
                    {
                        otherUserId = cachedOtherUserId;
                        Console.WriteLine($"Найден кэшированный otherUserId для чата {chatId}: {otherUserId}");
                    }

                    // Загружаем несколько сообщений, чтобы найти собеседника
                    var url = $"{_messageServiceBaseUrl}/messages/{chatId}?user_id={_currentUserId}&page=1&page_size=20";
                    var response = await _httpClient.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var messagesResponse = JsonSerializer.Deserialize<MessagesResponse>(content);

                        if (messagesResponse?.Messages != null && messagesResponse.Messages.Count > 0)
                        {
                            // Находим последнее сообщение
                            var lastMsg = messagesResponse.Messages.OrderByDescending(m => m.CreatedAt).First();
                            lastMessage = lastMsg.Content.Length > 50 ? lastMsg.Content.Substring(0, 50) + "..." : lastMsg.Content;
                            lastMessageTime = lastMsg.CreatedAt;

                            // Ищем всех уникальных отправителей
                            var allSenderIds = messagesResponse.Messages
                                .Select(m => m.SenderId)
                                .Distinct()
                                .ToList();

                            // Если otherUserId еще не найден через кэш, ищем в сообщениях
                            if (otherUserId == 0)
                            {
                                // Находим собеседника (кто не является текущим пользователем)
                                otherUserId = allSenderIds.FirstOrDefault(id => id != _currentUserId);

                                if (otherUserId > 0)
                                {
                                    // Сохраняем в кэш
                                    _chatToOtherUserIdCache[chatId] = otherUserId;
                                    Console.WriteLine($"Найден otherUserId для чата {chatId} через сообщения: {otherUserId}");
                                }
                            }

                            // Теперь получаем имя собеседника
                            if (otherUserId > 0)
                            {
                                // Получаем имя собеседника
                                if (!_userNameCache.TryGetValue(otherUserId, out userName))
                                {
                                    var loadedName = await GetUserNameByIdAsync(otherUserId);
                                    if (!string.IsNullOrEmpty(loadedName))
                                    {
                                        userName = loadedName;
                                        Console.WriteLine($"Загружено имя для чата {chatId}: {userName} (ID: {otherUserId})");
                                    }
                                    else
                                    {
                                        userName = $"Пользователь {otherUserId}";
                                    }
                                }
                            }
                            else
                            {
                                // Если так и не нашли собеседника
                                Console.WriteLine($"Не удалось определить собеседника для чата {chatId}");
                                userName = $"Чат {chatId}";
                            }
                        }
                        else
                        {
                            // Нет сообщений в чате
                            Console.WriteLine($"В чате {chatId} нет сообщений");

                            // Если otherUserId найден через кэш, получаем имя
                            if (otherUserId > 0)
                            {
                                if (!_userNameCache.TryGetValue(otherUserId, out userName))
                                {
                                    var loadedName = await GetUserNameByIdAsync(otherUserId);
                                    if (!string.IsNullOrEmpty(loadedName))
                                    {
                                        userName = loadedName;
                                    }
                                    else
                                    {
                                        userName = $"Пользователь {otherUserId}";
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Ошибка загрузки сообщений для чата {chatId}: {response.StatusCode}");

                        // Даже при ошибке, если у нас есть кэшированный otherUserId, используем его
                        if (otherUserId > 0)
                        {
                            if (!_userNameCache.TryGetValue(otherUserId, out userName))
                            {
                                userName = $"Пользователь {otherUserId}";
                            }
                        }
                        else
                        {
                            continue; // Пропускаем чаты с ошибками и без кэша
                        }
                    }

                    validChats.Add(new ChatDto
                    {
                        Id = chatId,
                        Name = userName,
                        LastMessage = lastMessage,
                        LastMessageTime = lastMessageTime,
                        OtherUserId = otherUserId
                    });

                    Console.WriteLine($"Загружен чат {chatId}: {userName}, последнее сообщение: {lastMessage}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка загрузки деталей чата {chatId}: {ex.Message}");
                    // Пропускаем чаты с ошибками
                    continue;
                }
            }

            Console.WriteLine($"Загружено {validChats.Count} валидных чатов с именами");
            return validChats;
        }

        /// <summary>
        /// Получить имя пользователя по ID через API
        /// </summary>
        private async Task<string?> GetUserNameByIdAsync(uint userId)
        {
            // Проверяем кэш
            if (_userNameCache.TryGetValue(userId, out var cachedName))
            {
                return cachedName;
            }

            try
            {
                // Пробуем несколько стратегий

                // Стратегия 1: Поиск по ID как строке
                var url = $"{App.ApiBaseUrl}user/{Uri.EscapeDataString(userId.ToString())}?page=0&size=100";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync();
                    var searchResponse = await JsonSerializer.DeserializeAsync<SearchUsersResponse>(stream);

                    if (searchResponse?.Users != null)
                    {
                        var user = searchResponse.Users.FirstOrDefault(u => u.Id == userId);
                        if (user != null)
                        {
                            _userNameCache[userId] = user.Username;
                            Console.WriteLine($"Найден пользователь {userId}: {user.Username} (поиск по ID)");
                            return user.Username;
                        }
                    }
                }

                // Стратегия 2: Пробуем все буквы и цифры для поиска
                var searchTerms = new[] {
                    "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m",
                    "n", "o", "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z",
                    "0", "1", "2", "3", "4", "5", "6", "7", "8", "9"
                };

                var searchTasks = searchTerms.Select(async term =>
                {
                    try
                    {
                        url = $"{App.ApiBaseUrl}user/{Uri.EscapeDataString(term)}?page=0&size=1000";
                        response = await _httpClient.GetAsync(url);

                        if (response.IsSuccessStatusCode)
                        {
                            using var stream = await response.Content.ReadAsStreamAsync();
                            var searchResponse = await JsonSerializer.DeserializeAsync<SearchUsersResponse>(stream);

                            if (searchResponse?.Users != null)
                            {
                                var user = searchResponse.Users.FirstOrDefault(u => u.Id == userId);
                                if (user != null)
                                {
                                    return user;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка при поиске пользователя {userId} (термин '{term}'): {ex.Message}");
                    }
                    return null;
                });

                var results = await Task.WhenAll(searchTasks);
                var foundUser = results.FirstOrDefault(u => u != null);

                if (foundUser != null)
                {
                    _userNameCache[userId] = foundUser.Username;
                    Console.WriteLine($"Найден пользователь {userId}: {foundUser.Username} (агрессивный поиск)");
                    return foundUser.Username;
                }

                Console.WriteLine($"Пользователь {userId} не найден через все методы поиска");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка получения имени пользователя {userId}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Обновление списка чатов в UI
        /// </summary>
        private void UpdateChatsList()
        {
            try
            {
                // Фильтруем только валидные чаты (без ошибок)
                var validChats = _userChats.Where(chat => chat != null && chat.Id > 0).ToList();

                // Также добавляем временные чаты
                var pendingChatItems = _pendingChats.Values
                    .Where(chat => chat.IsPending)
                    .Select(chat => new ChatListItem
                    {
                        ChatId = chat.Id,
                        DisplayName = chat.Name,
                        LastMessage = chat.LastMessage,
                        LastMessageTime = chat.LastMessageTime.ToString("HH:mm")
                    }).ToList();

                var chatItems = validChats.Select(chat => new ChatListItem
                {
                    ChatId = chat.Id,
                    DisplayName = !string.IsNullOrEmpty(chat.Name) && !chat.Name.StartsWith("Чат ") ?
                        chat.Name :
                        (chat.OtherUserId > 0 ? $"Пользователь {chat.OtherUserId}" : $"Чат {chat.Id}"),
                    LastMessage = chat.LastMessage,
                    LastMessageTime = chat.LastMessageTime.ToString("HH:mm")
                }).ToList();

                // Объединяем реальные и временные чаты
                var allChats = chatItems.Concat(pendingChatItems).ToList();

                UsersListBox.ItemsSource = allChats;
                Console.WriteLine($"Обновлен список чатов в UI: {allChats.Count} элементов");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обновлении списка чатов: {ex.Message}");
            }
        }

        /////////////////==== WEBSOCKET LOGIC ====/////////////////

        private async Task OpenWebSocketForChat(uint chatId, string otherUsername, uint otherUserId)
        {
            CloseCurrentWebSocket();

            _currentWs = new ClientWebSocket();
            _currentWsToken = new CancellationTokenSource();

            // Загружаем токен для авторизации
            var tokenData = await TokenManager.LoadTokensAsync();
            if (tokenData == null || string.IsNullOrEmpty(tokenData.AccessToken))
            {
                Console.WriteLine("Ошибка: токен не найден для WebSocket подключения");
                return;
            }

            // Добавляем токен в заголовки WebSocket
            _currentWs.Options.SetRequestHeader("Authorization", $"Bearer {tokenData.AccessToken}");

            var wsUrl = $"ws://109.73.194.181:80/api/v1/message/ws?user_id_1={_currentUserId}&user_id_2={otherUserId}";
            try
            {
                await _currentWs.ConnectAsync(new Uri(wsUrl), _currentWsToken.Token);
                Console.WriteLine($"WS открыт для чата {chatId} с {otherUserId}");
                _currentWsListenTask = ListenWebSocketLoop(chatId, otherUserId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка подключения к WebSocket: {ex.Message}");
                CloseCurrentWebSocket();
            }
        }

        private void CloseCurrentWebSocket()
        {
            try
            {
                _currentWsToken?.Cancel();
            }
            catch { }
            if (_currentWs != null)
            {
                try { _currentWs.Dispose(); } catch { }
                _currentWs = null;
            }
            _currentWsToken = null;
        }

        private async Task ListenWebSocketLoop(uint currentChatId, uint otherUserId)
        {
            var ws = _currentWs;
            if (ws == null) return;
            var buffer = new byte[4096];
            while (ws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result = null;
                var ms = new System.IO.MemoryStream();
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var messageJson = Encoding.UTF8.GetString(ms.ToArray());
                try
                {
                    var msg = JsonSerializer.Deserialize<MessageDto>(messageJson);
                    if (msg != null)
                    {
                        // Получаем имя отправителя
                        string senderName;
                        if (msg.SenderId == _currentUserId)
                        {
                            senderName = "Вы";
                        }
                        else
                        {
                            // Пробуем получить имя из кэша или через API
                            if (_userNameCache.TryGetValue(msg.SenderId, out var cachedName))
                            {
                                senderName = cachedName;
                            }
                            else
                            {
                                // Если нет в кэше, загружаем через API
                                var userName = await GetUserNameByIdAsync(msg.SenderId);
                                senderName = userName ?? (!string.IsNullOrEmpty(_currentChatUserName) ? _currentChatUserName : $"Пользователь {msg.SenderId}");
                            }
                        }

                        var finalSenderName = senderName; // Захватываем для замыкания
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (_currentChatId == msg.ChatId) // только если активный!
                            {
                                var messages = (MessagesListBox.ItemsSource as List<MessageListItem> ?? new List<MessageListItem>()).ToList();
                                messages.Add(new MessageListItem
                                {
                                    Content = msg.Content,
                                    IsOwnMessage = msg.SenderId == _currentUserId,
                                    Time = msg.CreatedAt.ToString("HH:mm"),
                                    SenderName = finalSenderName
                                });
                                MessagesListBox.ItemsSource = messages;
                                ScrollToLatestMessage();
                            }
                        });
                        await LoadUserChatsAsync(); // обновить список чатов на любые новые сообщения
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка обработки ws-сообщения: {ex.Message}");
                }
            }
        }

        ///////////==== ЧАТ/ИСТОРИЯ ====///////////

        /// <summary>
        /// Обработчик выбора чата из списка
        /// </summary>
        private async void OnChatSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (UsersListBox.SelectedItem is ChatListItem selectedChat && selectedChat.ChatId != 0)
            {
                Console.WriteLine($"Выбран чат: {selectedChat.DisplayName} (ID: {selectedChat.ChatId})");

                _currentChatId = selectedChat.ChatId;
                _currentChatUserName = selectedChat.DisplayName;
                ChatUserName.Text = selectedChat.DisplayName;

                // Проверяем, является ли этот чат временным
                if (_pendingChats.TryGetValue(selectedChat.ChatId, out var pendingChat))
                {
                    // Это временный чат - просто очищаем историю
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        MessagesListBox.ItemsSource = new List<MessageListItem>();
                    });
                    Console.WriteLine($"Выбран временный чат с пользователем {pendingChat.Name}");
                }
                else
                {
                    // Это реальный чат - загружаем историю
                    await LoadChatHistoryAsync(selectedChat.ChatId);

                    // Находим собеседника
                    var otherUserId = await GetOtherUserIdForChat(selectedChat.ChatId);
                    if (otherUserId > 0)
                    {
                        await OpenWebSocketForChat(selectedChat.ChatId, selectedChat.DisplayName, otherUserId);
                    }
                }
            }
        }

        /// <summary>
        /// Загрузка истории сообщений для чата
        /// </summary>
        private async Task LoadChatHistoryAsync(uint chatId, int page = 1, int pageSize = 20)
        {
            try
            {
                Console.WriteLine($"Загружаем историю чата {chatId}, страница {page}");

                var url = $"{_messageServiceBaseUrl}/messages/{chatId}?user_id={_currentUserId}&page={page}&page_size={pageSize}";
                var response = await _httpClient.GetAsync(url);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    Console.WriteLine("Получен 401 даже после попытки обновления токена при загрузке истории");
                    return;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Ошибка загрузки истории чата: {response.StatusCode}, {errorContent}");
                    return;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Получены сообщения: {responseContent}");

                var messagesResponse = JsonSerializer.Deserialize<MessagesResponse>(responseContent);

                if (messagesResponse != null && messagesResponse.Messages != null)
                {
                    // Загружаем имена всех отправителей из сообщений
                    var uniqueSenderIds = messagesResponse.Messages
                        .Where(m => m.SenderId != _currentUserId)
                        .Select(m => m.SenderId)
                        .Distinct()
                        .ToList();

                    // Загружаем имена всех отправителей параллельно
                    var loadNameTasks = uniqueSenderIds.Select(async senderId =>
                    {
                        if (!_userNameCache.ContainsKey(senderId))
                        {
                            var name = await GetUserNameByIdAsync(senderId);
                            if (name != null)
                            {
                                Console.WriteLine($"Загружено имя для sender_id {senderId}: {name}");
                            }
                        }
                    });

                    await Task.WhenAll(loadNameTasks);

                    await UpdateMessagesListAsync(messagesResponse.Messages);
                    Console.WriteLine($"Загружено {messagesResponse.Messages.Count} сообщений");
                }
                else
                {
                    Console.WriteLine("Получен пустой список сообщений");
                    await UpdateMessagesListAsync(new List<MessageDto>());
                }
            }
            catch (HttpRequestException httpEx)
            {
                Console.WriteLine($"Сетевая ошибка при загрузке истории: {httpEx.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Исключение при загрузке истории чата: {ex.Message}");
            }
        }

        /// <summary>
        /// Обновление списка сообщений в UI
        /// </summary>
        private async Task UpdateMessagesListAsync(List<MessageDto> messages)
        {
            try
            {
                var sortedMessages = messages.OrderBy(m => m.CreatedAt).ToList();

                var messageItems = new List<MessageListItem>();
                foreach (var msg in sortedMessages)
                {
                    string senderName;
                    if (msg.SenderId == _currentUserId)
                    {
                        senderName = "Вы";
                    }
                    else
                    {
                        // Пробуем получить имя из кэша или через API
                        if (_userNameCache.TryGetValue(msg.SenderId, out var cachedName))
                        {
                            senderName = cachedName;
                        }
                        else
                        {
                            // Если нет в кэше, загружаем через API
                            var userName = await GetUserNameByIdAsync(msg.SenderId);
                            if (!string.IsNullOrEmpty(userName))
                            {
                                senderName = userName;
                            }
                            else
                            {
                                // Если не удалось получить, используем текущее имя чата или заглушку
                                senderName = !string.IsNullOrEmpty(_currentChatUserName) ? _currentChatUserName : $"Пользователь {msg.SenderId}";
                            }
                        }
                    }

                    messageItems.Add(new MessageListItem
                    {
                        Content = msg.Content,
                        IsOwnMessage = msg.SenderId == _currentUserId,
                        Time = msg.CreatedAt.ToString("HH:mm"),
                        SenderName = senderName
                    });
                }

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    MessagesListBox.ItemsSource = messageItems;
                    ScrollToLatestMessage();
                });

                Console.WriteLine($"Обновлен список сообщений: {messageItems.Count} сообщений");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обновлении списка сообщений: {ex.Message}");
            }
        }

        /// <summary>
        /// Прокрутка к последнему сообщению
        /// </summary>
        private void ScrollToLatestMessage()
        {
            try
            {
                if (MessagesListBox.ItemCount > 0)
                {
                    MessagesListBox.ScrollIntoView(MessagesListBox.ItemCount - 1);
                    Console.WriteLine("Прокручено к последнему сообщению");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при прокрутке сообщений: {ex.Message}");
            }
        }

        /// <summary>
        /// Обработчик отправки сообщения (создание чата при первом сообщении)
        /// </summary>
        private async void OnSendMessageClicked(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(MessageTextBox.Text))
            {
                Console.WriteLine("Нельзя отправить пустое сообщение");
                return;
            }

            try
            {
                var message = MessageTextBox.Text.Trim();

                if (!_currentChatId.HasValue)
                {
                    Console.WriteLine("Попытка отправить сообщение без активного чата");
                    return;
                }

                // Проверяем, является ли текущий чат временным (pending)
                ChatDto? pendingChat = null;
                uint otherUserId = 0;
                string otherUsername = "";

                if (_currentChatId.HasValue && _pendingChats.TryGetValue(_currentChatId.Value, out var tempChat))
                {
                    pendingChat = tempChat;
                    otherUserId = tempChat.OtherUserId;
                    otherUsername = tempChat.Name;
                }

                // Если это временный чат, создаем его на сервере
                if (pendingChat != null && otherUserId > 0)
                {
                    Console.WriteLine($"Обнаружен временный чат, создаем чат на сервере с пользователем {otherUserId}");

                    // Создаем чат на сервере
                    var realChatId = await CreateChatOnFirstMessage(otherUserId);
                    if (realChatId == null)
                    {
                        Console.WriteLine("Не удалось создать чат на сервере");
                        return;
                    }

                    Console.WriteLine($"Чат создан на сервере с ID: {realChatId}");

                    // Сохраняем имя собеседника для нового чата
                    if (!_userNameCache.ContainsKey(otherUserId))
                    {
                        _userNameCache[otherUserId] = otherUsername;
                    }

                    // Сохраняем связь chatId -> otherUserId в кэше
                    _chatToOtherUserIdCache[realChatId.Value] = otherUserId;

                    // Удаляем временный чат из хранилищ
                    _pendingChats.Remove(_currentChatId.Value);

                    // Обновляем текущий ID чата на реальный
                    _currentChatId = realChatId;

                    // Обновляем имя чата в UI
                    _currentChatUserName = otherUsername;
                    ChatUserName.Text = otherUsername;

                    // Открываем WebSocket для нового чата
                    await OpenWebSocketForChat(realChatId.Value, otherUsername, otherUserId);

                    // Немедленно добавляем созданный чат в список, чтобы он не пропадал
                    var newChat = new ChatDto
                    {
                        Id = realChatId.Value,
                        Name = otherUsername,
                        LastMessage = message,
                        LastMessageTime = DateTime.Now,
                        OtherUserId = otherUserId
                    };

                    // Удаляем старый временный чат из _userChats если он там есть
                    _userChats.RemoveAll(c => c.Id == pendingChat.Id && c.IsPending);
                    _userChats.Add(newChat);
                    UpdateChatsList();

                    Console.WriteLine($"Добавлен чат {realChatId} в список, переключились на реальный чат");

                    // Отправляем сообщение через WebSocket
                    await SendMessageViaWebSocket(message, realChatId.Value);
                }
                else
                {
                    // Это уже существующий чат - просто отправляем сообщение
                    await SendMessageViaWebSocket(message, _currentChatId.Value);
                }

                MessageTextBox.Text = string.Empty;
                ScrollToLatestMessage();

                Console.WriteLine("Сообщение отправлено и добавлено в UI");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при отправке сообщения: {ex.Message}");
            }
        }

        /// <summary>
        /// Отправка сообщения через WebSocket
        /// </summary>
        private async Task SendMessageViaWebSocket(string message, uint chatId)
        {
            var ws = _currentWs;
            if (ws == null || ws.State != WebSocketState.Open)
            {
                Console.WriteLine($"Нет ws соединения для чата {chatId}");

                // Пытаемся открыть соединение
                var otherUserId = await GetOtherUserIdForChat(chatId);
                if (otherUserId > 0)
                {
                    await OpenWebSocketForChat(chatId, _currentChatUserName, otherUserId);
                    ws = _currentWs;

                    if (ws == null || ws.State != WebSocketState.Open)
                    {
                        Console.WriteLine("Не удалось открыть WebSocket соединение");
                        return;
                    }
                }
                else
                {
                    Console.WriteLine("Не удалось определить собеседника для открытия WebSocket");
                    return;
                }
            }

            var payload = Encoding.UTF8.GetBytes(message);
            await ws.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, CancellationToken.None);
            Console.WriteLine($"Сообщение отправлено через WebSocket в чат {chatId}");
        }

        // МЕТОДЫ ПОИСКА ////////////////////////////////////

        private async Task PerformSearchAsync(int page = 0)
        {
            var query = SearchGlobalTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                GlobalUsersListBox.ItemsSource = new List<string>();
                ResetPagination();
                return;
            }

            try
            {
                _currentSearchQuery = query;
                _currentPage = page;

                var url = $"{App.ApiBaseUrl}user/{Uri.EscapeDataString(query)}?page={page}&size=8";
                var response = await _httpClient.GetAsync(url);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    Console.WriteLine("Получен 401 при поиске пользователей");
                    GlobalUsersListBox.ItemsSource = new List<string> { "Сессия истекла. Войдите снова." };
                    return;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Ошибка поиска: {response.StatusCode}, {errorContent}");
                    GlobalUsersListBox.ItemsSource = new List<string> { $"Ошибка поиска: {response.StatusCode}" };
                    return;
                }

                using var stream = await response.Content.ReadAsStreamAsync();
                var searchResponse = await JsonSerializer.DeserializeAsync<SearchUsersResponse>(stream);

                if (searchResponse != null)
                {
                    _totalPages = searchResponse.TotalPages;
                    UpdatePaginationInfo();

                    // Заполняем кэш найденными пользователями
                    if (searchResponse.Users != null)
                    {
                        foreach (var user in searchResponse.Users)
                        {
                            if (!_userNameCache.ContainsKey((uint)user.Id))
                            {
                                _userNameCache[(uint)user.Id] = user.Username;
                            }
                        }
                    }

                    var usernames = searchResponse.Users?.Select(u => u.Username).ToList() ?? new List<string>();
                    GlobalUsersListBox.ItemsSource = usernames;
                }
                else
                {
                    ResetPagination();
                    GlobalUsersListBox.ItemsSource = new List<string>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при поиске: {ex.Message}");
                GlobalUsersListBox.ItemsSource = new List<string> { "Ошибка сети" };
                ResetPagination();
            }
        }

        private void UpdatePaginationInfo()
        {
            PageInfoText.Text = $"Страница {_currentPage + 1} из {_totalPages}";
            PrevPageButton.IsEnabled = _currentPage > 0;
            NextPageButton.IsEnabled = _currentPage < _totalPages - 1;
        }

        private void ResetPagination()
        {
            _currentPage = 0;
            _totalPages = 1;
            PageInfoText.Text = "Страница 1 из 1";
            PrevPageButton.IsEnabled = false;
            NextPageButton.IsEnabled = false;
        }

        private void OnSearchUsersClicked(object? sender, RoutedEventArgs e)
        {
            GlobalSearchPanel.IsVisible = true;
            ChatPanel.IsVisible = false;
        }

        private async Task OnSearchUsersAsync()
        {
            await Task.Delay(300);
            var currentQuery = SearchGlobalTextBox.Text ?? string.Empty;
            if (currentQuery != _currentSearchQuery)
            {
                _ = PerformSearchAsync(0);
            }
        }

        private void OnBackToChatClicked(object? sender, RoutedEventArgs e)
        {
            GlobalSearchPanel.IsVisible = false;
            ChatPanel.IsVisible = true;
        }

        // ---[ СОЗДАНИЕ ЧАТА ТОЛЬКО ПРИ ПЕРВОМ СООБЩЕНИИ ]---
        private async void OnGlobalUserSelected(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (GlobalUsersListBox.SelectedItem is string userName)
            {
                // Получаем ID пользователя по имени
                var foundUserId = await GetUserIdByName(userName);
                if (foundUserId == null)
                {
                    Console.WriteLine($"Не удалось найти ID для пользователя {userName}");
                    return;
                }

                // Сохраняем имя в кэш
                _userNameCache[foundUserId.Value] = userName;

                // Проверяем, существует ли уже чат с этим пользователем
                var existingChat = _userChats.FirstOrDefault(c => c.OtherUserId == foundUserId.Value);

                if (existingChat != null)
                {
                    // Если чат существует, переключаемся в него
                    _currentChatId = existingChat.Id;
                    _currentChatUserName = userName;
                    ChatUserName.Text = userName;

                    // Переключаем панели
                    GlobalSearchPanel.IsVisible = false;
                    ChatPanel.IsVisible = true;

                    // Загружаем историю и открываем WebSocket
                    await LoadChatHistoryAsync(existingChat.Id);
                    await OpenWebSocketForChat(existingChat.Id, userName, foundUserId.Value);
                }
                else
                {
                    // Создаем временный чат (еще не на сервере)
                    var tempChatId = (uint)(Guid.NewGuid().GetHashCode() & 0x7FFFFFFF); // Генерируем временный положительный ID
                    var pendingChat = new ChatDto
                    {
                        Id = tempChatId,
                        Name = userName,
                        LastMessage = "Начните общение",
                        LastMessageTime = DateTime.Now,
                        OtherUserId = foundUserId.Value,
                        IsPending = true // Флаг, что чат еще не создан на сервере
                    };

                    // Сохраняем во временное хранилище
                    _pendingChats[tempChatId] = pendingChat;

                    // Переключаемся в этот чат
                    _currentChatId = pendingChat.Id;
                    _currentChatUserName = userName;
                    ChatUserName.Text = userName;

                    // Переключаем панели
                    GlobalSearchPanel.IsVisible = false;
                    ChatPanel.IsVisible = true;

                    // Очищаем историю сообщений
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        MessagesListBox.ItemsSource = new List<MessageListItem>();
                    });

                    Console.WriteLine($"Создан временный чат с пользователем {userName} (ID: {foundUserId.Value}, временный ID чата: {tempChatId})");

                    // Обновляем список чатов, чтобы временный чат отобразился
                    UpdateChatsList();
                }
            }
        }

        private void OnPrevPageClick(object? sender, RoutedEventArgs e)
        {
            if (_currentPage > 0)
            {
                _ = PerformSearchAsync(_currentPage - 1);
            }
        }

        private void OnNextPageClick(object? sender, RoutedEventArgs e)
        {
            if (_currentPage < _totalPages - 1)
            {
                _ = PerformSearchAsync(_currentPage + 1);
            }
        }

        // МЕТОДЫ НАСТРОЕК ///////////////////////////

        private void OnBackFromSettingsClicked(object? sender, RoutedEventArgs e)
        {
            SettingsPanel.IsVisible = false;
            VoicePanel.IsVisible = false;
            MainPanels.IsVisible = true;
            LeftSearchPanel.IsVisible = true;
        }

        private void OnSettingsClicked(object? sender, RoutedEventArgs e)
        {
            MainPanels.IsVisible = false;
            LeftSearchPanel.IsVisible = false;
            SettingsPanel.IsVisible = true;
        }

        private void OnVoiceSettingsClicked(object? sender, RoutedEventArgs e)
        {
            VoicePanel.IsVisible = true;
            LoadAudioDevices();
        }

        private void LoadAudioDevices()
        {
            try
            {
                var microphones = new List<string>();
                var enumerator = new MMDeviceEnumerator();
                var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                foreach (var device in captureDevices)
                {
                    microphones.Add(device.FriendlyName);
                }
                MicrophoneComboBox.ItemsSource = microphones;

                var audioOutputs = new List<string>();
                var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (var device in renderDevices)
                {
                    audioOutputs.Add(device.FriendlyName);
                }
                AudioOutputComboBox.ItemsSource = audioOutputs;
            }
            catch (Exception ex)
            {
                MicrophoneComboBox.ItemsSource = new List<string> { "Ошибка загрузки микрофонов" };
                AudioOutputComboBox.ItemsSource = new List<string> { "Ошибка загрузки устройств звука" };
            }
        }

        private void OnTestVoiceClicked(object? sender, RoutedEventArgs e)
        {
            if (!_isVoiceTestActive)
            {
                StartVoiceTest();
                TestVoiceButton.Content = "Остановить";
                _isVoiceTestActive = true;
            }
            else
            {
                StopVoiceTest();
                TestVoiceButton.Content = "Проверить голос";
                _isVoiceTestActive = false;
            }
        }

        private void StartVoiceTest()
        {
            try
            {
                var selectedMicrophone = MicrophoneComboBox.SelectedIndex;
                var selectedAudioOutput = AudioOutputComboBox.SelectedIndex;

                if (selectedMicrophone < 0 || selectedAudioOutput < 0)
                {
                    return;
                }

                var enumerator = new MMDeviceEnumerator();
                var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToArray();
                var captureDevice = captureDevices[selectedMicrophone];

                var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToArray();
                var renderDevice = renderDevices[selectedAudioOutput];

                _audioCapture = new WasapiCapture(captureDevice);
                var waveProvider = new BufferedWaveProvider(_audioCapture.WaveFormat);
                waveProvider.BufferLength = 65536;
                waveProvider.DiscardOnBufferOverflow = true;

                _audioPlayback = new WasapiOut(renderDevice, AudioClientShareMode.Shared, false, 10);

                double currentVolume = 0;
                double peakVolume = 0;
                DateTime lastPeakTime = DateTime.Now;

                _audioCapture.DataAvailable += (s, e) =>
                {
                    waveProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);

                    int bytesPerSample = _audioCapture.WaveFormat.BitsPerSample / 8;
                    int sampleCount = e.BytesRecorded / bytesPerSample;

                    if (sampleCount == 0) return;

                    double sumSquares = 0;
                    double maxAmplitude = 0;

                    if (_audioCapture.WaveFormat.Encoding == WaveFormatEncoding.Pcm)
                    {
                        if (_audioCapture.WaveFormat.BitsPerSample == 16)
                        {
                            for (int i = 0; i < e.BytesRecorded; i += 2)
                            {
                                short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                                double normalizedSample = sample / 32768.0;

                                sumSquares += normalizedSample * normalizedSample;
                                maxAmplitude = Math.Max(maxAmplitude, Math.Abs(normalizedSample));
                            }
                        }
                        else if (_audioCapture.WaveFormat.BitsPerSample == 32)
                        {
                            for (int i = 0; i < e.BytesRecorded; i += 4)
                            {
                                int sample = BitConverter.ToInt32(e.Buffer, i);
                                double normalizedSample = sample / 2147483648.0;

                                sumSquares += normalizedSample * normalizedSample;
                                maxAmplitude = Math.Max(maxAmplitude, Math.Abs(normalizedSample));
                            }
                        }
                        else if (_audioCapture.WaveFormat.BitsPerSample == 8)
                        {
                            for (int i = 0; i < e.BytesRecorded; i++)
                            {
                                double normalizedSample = (e.Buffer[i] - 128) / 128.0;
                                sumSquares += normalizedSample * normalizedSample;
                                maxAmplitude = Math.Max(maxAmplitude, Math.Abs(normalizedSample));
                            }
                        }
                    }
                    else if (_audioCapture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                    {
                        for (int i = 0; i < e.BytesRecorded; i += 4)
                        {
                            float sample = BitConverter.ToSingle(e.Buffer, i);

                            sumSquares += sample * sample;
                            maxAmplitude = Math.Max(maxAmplitude, Math.Abs(sample));
                        }
                    }

                    double rms = Math.Sqrt(sumSquares / sampleCount);
                    double db = rms > 0 ? 20.0 * Math.Log10(rms) : -60;
                    double normalizedVolume = Math.Max(0, Math.Min(100, (db + 60) * 100 / 60));

                    double attackCoeff = 0.3;
                    double releaseCoeff = 0.1;

                    if (normalizedVolume > currentVolume)
                        currentVolume = currentVolume * (1 - attackCoeff) + normalizedVolume * attackCoeff;
                    else
                        currentVolume = currentVolume * (1 - releaseCoeff) + normalizedVolume * releaseCoeff;

                    if (normalizedVolume > peakVolume)
                    {
                        peakVolume = normalizedVolume;
                        lastPeakTime = DateTime.Now;
                    }
                    else if ((DateTime.Now - lastPeakTime).TotalMilliseconds > 1500)
                    {
                        peakVolume *= 0.95;
                        if (peakVolume < currentVolume)
                            peakVolume = currentVolume;
                    }

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        VoiceVolumeSlider.Value = currentVolume;
                    });
                };

                _audioCapture.RecordingStopped += (s, e) =>
                {
                    if (e.Exception != null)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            VoiceVolumeSlider.Value = 0;
                        });
                    }
                };

                _audioPlayback.Init(waveProvider);
                _audioPlayback.Play();
                _audioCapture.StartRecording();
            }
            catch (Exception ex)
            {
                StopVoiceTest();
                Console.WriteLine($"Ошибка запуска теста голоса: {ex.Message}");
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    TestVoiceButton.Content = "Проверить голос";
                    _isVoiceTestActive = false;
                });
            }
        }

        private void StopVoiceTest()
        {
            try
            {
                _audioCapture?.StopRecording();
                _audioCapture?.Dispose();
                _audioCapture = null;

                _audioPlayback?.Stop();
                _audioPlayback?.Dispose();
                _audioPlayback = null;

                _smoothVolume = 0;

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    VoiceVolumeSlider.Value = 0;
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при остановке теста голоса: {ex.Message}");
            }
        }

        // ---[ ПОМОЩНИКИ ---]
        private async Task<uint?> GetUserIdByName(string name)
        {
            try
            {
                var url = $"{App.ApiBaseUrl}user/" + Uri.EscapeDataString(name) + "?page=0&size=8";
                var resp = await _httpClient.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return null;
                using var stream = await resp.Content.ReadAsStreamAsync();
                var result = await JsonSerializer.DeserializeAsync<SearchUsersResponse>(stream);
                var user = result?.Users?.FirstOrDefault(u => u.Username == name);
                return user?.Id != null ? (uint?)user.Id : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Получить ID собеседника для чата
        /// </summary>
        private async Task<uint> GetOtherUserIdForChat(uint chatId)
        {
            try
            {
                // Сначала проверяем в загруженных чатах
                var chat = _userChats.FirstOrDefault(c => c.Id == chatId);
                if (chat != null && chat.OtherUserId > 0)
                {
                    return chat.OtherUserId;
                }

                // Проверяем кэш связей
                if (_chatToOtherUserIdCache.TryGetValue(chatId, out var cachedId))
                {
                    Console.WriteLine($"Найден otherUserId для чата {chatId} из кэша связей: {cachedId}");
                    return cachedId;
                }

                Console.WriteLine($"Не удалось определить собеседника для чата {chatId}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка определения собеседника для чата {chatId}: {ex.Message}");
                return 0;
            }
        }

        // ---[ МЕТОД ДЛЯ СОЗДАНИЯ ЧАТА ПРИ ПЕРВОМ СООБЩЕНИИ ]---
        private async Task<uint?> CreateChatOnFirstMessage(uint otherUserId)
        {
            try
            {
                // Создаем чат через API
                var req = new
                {
                    user_id_1 = _currentUserId,
                    user_id_2 = otherUserId
                };
                var json = JsonSerializer.Serialize(req);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var resp = await _httpClient.PostAsync($"{_messageServiceBaseUrl}/chat/bytwouser", content);

                if (!resp.IsSuccessStatusCode)
                {
                    var errorContent = await resp.Content.ReadAsStringAsync();
                    Console.WriteLine($"Ошибка создания чата: {resp.StatusCode}, {errorContent}");
                    return null;
                }

                var respJson = await resp.Content.ReadAsStringAsync();
                using var d = JsonDocument.Parse(respJson);
                var chatId = d.RootElement.GetProperty("chat_id").GetUInt32();

                Console.WriteLine($"Создан новый чат {chatId} с пользователем {otherUserId}");

                return chatId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при создании чата: {ex.Message}");
                return null;
            }
        }

        // КЛАССЫ ДЛЯ ДАННЫХ /////////////////////////////////////////

        public class SearchUsersResponse
        {
            [JsonPropertyName("users")]
            public List<UserDto> Users { get; set; } = new();

            [JsonPropertyName("currentPage")]
            public int CurrentPage { get; set; }

            [JsonPropertyName("totalItems")]
            public int TotalItems { get; set; }

            [JsonPropertyName("totalPages")]
            public int TotalPages { get; set; }
        }

        public class UserDto
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }

            [JsonPropertyName("username")]
            public string Username { get; set; } = string.Empty;
        }

        public class GetUserChatsResponse
        {
            [JsonPropertyName("user_id")]
            public uint UserId { get; set; }

            [JsonPropertyName("chats")]
            public List<uint> Chats { get; set; } = new();
        }

        public class ChatDto
        {
            public uint Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string LastMessage { get; set; } = string.Empty;
            public DateTime LastMessageTime { get; set; }
            public uint OtherUserId { get; set; }
            public bool IsPending { get; set; } = false; // Флаг для временных чатов
        }

        public class MessageDto
        {
            [JsonPropertyName("id")]
            public uint Id { get; set; }

            [JsonPropertyName("content")]
            public string Content { get; set; } = string.Empty;

            [JsonPropertyName("sender_id")]
            public uint SenderId { get; set; }

            [JsonPropertyName("chat_id")]
            public uint ChatId { get; set; }

            [JsonPropertyName("created_at")]
            public DateTime CreatedAt { get; set; }
        }

        public class MessagesResponse
        {
            [JsonPropertyName("messages")]
            public List<MessageDto> Messages { get; set; } = new();
        }

        private async Task ChatListPollingAsync()
        {
            try
            {
                if (_currentUserId == 0) return;

                var request = new { user_id = _currentUserId };
                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_messageServiceBaseUrl}/chat/user", content);
                if (!response.IsSuccessStatusCode) return;
                var responseContent = await response.Content.ReadAsStringAsync();
                var chatsResponse = JsonSerializer.Deserialize<GetUserChatsResponse>(responseContent);
                if (chatsResponse?.Chats == null) return;

                // Проверить изменения
                var set = new HashSet<uint>(chatsResponse.Chats);
                var lastSet = new HashSet<uint>(_lastChatIds);
                if (!set.SetEquals(lastSet))
                {
                    Console.WriteLine($"Обнаружено изменение чатов, обновляем левую панель...");
                    _lastChatIds = chatsResponse.Chats;
                    _userChats = await LoadChatDetailsAsync(chatsResponse.Chats);
                    Avalonia.Threading.Dispatcher.UIThread.Post(UpdateChatsList);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Chat polling error: {ex.Message}");
            }
        }
    }

    // Классы для отображения в UI
    public class ChatListItem
    {
        public uint ChatId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string LastMessage { get; set; } = string.Empty;
        public string LastMessageTime { get; set; } = string.Empty;
    }

    public class MessageListItem
    {
        public string Content { get; set; } = string.Empty;
        public bool IsOwnMessage { get; set; }
        public string Time { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
    }
}