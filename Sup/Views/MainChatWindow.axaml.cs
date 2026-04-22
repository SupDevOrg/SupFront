using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NAudio.CoreAudioApi;
using Sup.Models;
using Sup.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sup.Views
{
    public partial class MainChatWindow : Window
    {
        private readonly IChatService _chatService;
        private readonly IUserSearchService _userSearchService;
        private readonly IVoiceTestService _voiceTestService;
        private readonly IWebSocketService _webSocketService;
        private readonly IFriendService _friendService;
        private readonly IUserAvatarService _userAvatarService;
        private readonly ISignalingService _signalingService;
        private readonly IVoiceCallService _voiceCallService;

        /// <summary>
        /// Срабатывает когда первоначальная загрузка данных (чаты, профиль) завершена.
        /// </summary>
        public event EventHandler? LoadingCompleted;

        private uint _currentUserId = 0;
        private uint? _currentChatId = null;
        private string _currentChatUserName = string.Empty;
        private string _currentUsername = string.Empty;

        private int _currentPage = 0;
        private int _totalPages = 1;
        private string _currentSearchQuery = string.Empty;

        private System.Timers.Timer? _chatListTimer;
        private Dictionary<uint, ChatDto> _pendingChats = new Dictionary<uint, ChatDto>();
        private bool _isVoiceTestActive = false;
        private List<SearchResultItemDto> _currentSearchResults = new();
        private string _selectedAvatarFilePath = string.Empty;
        private bool _isAvatarPendingConfirmation = false;
        private byte[] _resizedAvatarData = Array.Empty<byte>();

        // Индексы выбранных аудиоустройств из настроек
        private int _selectedMicIndex = -1;
        private int _selectedSpeakerIndex = -1;
        // SDP входящего звонка, ожидающего принятия
        private string _pendingOfferSdp = string.Empty;

        public MainChatWindow() : this("user")
        {
        }

        public MainChatWindow(string username)
        {
            InitializeComponent();

            _signalingService = new SignalingService();
            _voiceCallService = new VoiceCallService();

            try
            {
                _chatService = new ChatService();
                _userSearchService = new UserSearchService();
                _voiceTestService = new VoiceTestService();
                _webSocketService = new WebSocketService();
                _friendService = new FriendService();
                _userAvatarService = new UserAvatarService();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainChatWindow] Ошибка инициализации сервисов: {ex.Message}");
            }

            _currentUsername = username;
            UserNameLabel.Text = username;

            SetupEventHandlers();
            SetupGlobalButtonHandlers();
            ResetTabsState();
            Console.WriteLine($"[MainChatWindow] Инициализация для пользователя: {username}");

            // Инициализируем в фоне
            _ = InitializeAsync();

            _chatListTimer = new System.Timers.Timer(1000);
            _chatListTimer.Elapsed += async (s, e) => await OnChatListPollingAsync();
            _chatListTimer.AutoReset = true;
            _chatListTimer.Start();
            Console.WriteLine("[MainChatWindow] Таймер полинга чатов запущен");

            this.Closed += (s, e) =>
            {
                _chatListTimer?.Stop();
                _signalingService.Disconnect();
                _voiceCallService.Dispose();
            };
        }

        private void SetupEventHandlers()
        {
            SearchTabButton.Click += OnSearchTabClicked;
            FriendsTabButton.Click += OnFriendsTabClicked;
            BackToChatButton.Click += OnBackToChatClicked;
            BackFromFriendsButton.Click += OnBackFromFriendsClicked;
            SearchGlobalTextBox.KeyUp += async (s, e) => await OnSearchUsersAsync();
            GlobalUsersListBox.DoubleTapped += OnGlobalUserSelected;
            SettingsButton.Click += OnSettingsClicked;
            VoiceSettingsButton.Click += OnVoiceSettingsClicked;
            AvatarSettingsButton.Click += OnAvatarSettingsClicked;
            BackFromSettingsButton.Click += OnBackFromSettingsClicked;
            TestVoiceButton.Click += OnTestVoiceClicked;
            SelectAvatarButton.Click += OnSelectAvatarClicked;
            PrevPageButton.Click += OnPrevPageClick;
            NextPageButton.Click += OnNextPageClick;
            SendMessageButton.Click += OnSendMessageClicked;
            CallButton.Click += OnCallButtonClicked;
            UsersListBox.SelectionChanged += OnChatSelected;

            FriendsTabButton2.Click += OnFriendsTabButton2Clicked;
            RequestsTabButton.Click += OnRequestsTabButtonClicked;

            _webSocketService.OnMessageReceived += OnWebSocketMessageReceived;

            AcceptCallButton.Click += OnAcceptCallClicked;
            RejectCallButton.Click += OnRejectCallClicked;
            EndCallButton.Click += OnEndCallClicked;

            MicrophoneComboBox.SelectionChanged += (s, e) => _selectedMicIndex = MicrophoneComboBox.SelectedIndex;
            AudioOutputComboBox.SelectionChanged += (s, e) => _selectedSpeakerIndex = AudioOutputComboBox.SelectedIndex;

            _signalingService.OnOfferReceived += OnSignalingOfferReceived;
            _signalingService.OnAnswerReceived += OnSignalingAnswerReceived;
            _signalingService.OnIceCandidateReceived += OnSignalingIceCandidateReceived;
            _signalingService.OnPeerLeft += OnSignalingPeerLeft;
            _signalingService.OnAudioReceived += OnSignalingRelayAudioReceived;

            _voiceCallService.OnOfferCreated += OnVoiceCallOfferCreated;
            _voiceCallService.OnAnswerCreated += OnVoiceCallAnswerCreated;
            _voiceCallService.OnIceCandidateReady += OnVoiceCallIceCandidateReady;
            _voiceCallService.OnCallConnected += OnVoiceCallConnected;
            _voiceCallService.OnCallEnded += OnVoiceCallEnded;
            _voiceCallService.OnRelayAudioReady += OnVoiceRelayAudioReady;
        }

        private void SetupGlobalButtonHandlers()
        {
            Console.WriteLine("[SetupGlobalButtonHandlers] Инициализация обработчиков кнопок");
            Button.ClickEvent.AddClassHandler<Button>(OnAnyButtonClicked, handledEventsToo: true);
            MenuItem.ClickEvent.AddClassHandler<MenuItem>(OnAnyMenuItemClicked, handledEventsToo: true);
        }

        private async void OnAnyButtonClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button) return;

            if (button.Tag is not uint userId) return;

            var content = button.Content?.ToString() ?? "";

            // Обработка кнопок поиска
            if (GlobalSearchPanel.IsVisible)
            {
                Console.WriteLine($"[OnAnyButtonClicked] Кнопка поиска: {content}, ID: {userId}");
                await OnSearchResultButtonClicked(content, userId);
                e.Handled = true;
                return;
            }

            // Обработка кнопок в списках запросов
            if (button.Name == "AcceptIncomingButton")
            {
                Console.WriteLine($"[OnAnyButtonClicked] Принятие входящего запроса от {userId}");
                await OnAcceptRequestClicked(userId);
                e.Handled = true;
            }
            else if (button.Name == "RejectIncomingButton")
            {
                Console.WriteLine($"[OnAnyButtonClicked] Отклонение входящего запроса от {userId}");
                await OnRejectRequestClicked(userId);
                e.Handled = true;
            }
            else if (button.Name == "CancelOutgoingButton")
            {
                Console.WriteLine($"[OnAnyButtonClicked] Отмена исходящего запроса для {userId}");
                await OnCancelRequestClicked(userId);
                e.Handled = true;
            }
        }

        private async void OnAnyMenuItemClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem) return;

            var headerText = menuItem.Header?.ToString() ?? "";
            
            // Проверяем что это пункт меню "Удалить из друзей"
            if (headerText != "Удалить из друзей") return;

            if (menuItem.Tag is not uint friendId)
            {
                Console.WriteLine("[OnAnyMenuItemClicked] Tag не содержит friendId");
                return;
            }

            Console.WriteLine($"[OnAnyMenuItemClicked] Контекстное меню: {headerText}, FriendID: {friendId}");
            await OnRemoveFriendClicked(friendId);
            e.Handled = true;
        }

        private async Task OnSearchResultButtonClicked(string buttonContent, uint userId)
        {
            Console.WriteLine($"[OnSearchResultButtonClicked] Action: {buttonContent}, UserID: {userId}");
            
            switch (buttonContent)
            {
                case "Добавить в друзья":
                    await OnAddFriendClicked(userId);
                    break;
                case "Принять":
                    await OnAcceptRequestClicked(userId);
                    break;
                case "Отклонить":
                    await OnRejectRequestClicked(userId);
                    break;
                case "Отменить запрос":
                    await OnCancelRequestClicked(userId);
                    break;
            }
        }

        private async Task OnAddFriendClicked(uint targetUserId)
        {
            Console.WriteLine($"[OnAddFriendClicked] Добавление в друзья пользователя {targetUserId}");
            
            bool success = await _friendService.SendFriendRequestAsync(_currentUserId, targetUserId);
            if (success)
            {
                Console.WriteLine($"[OnAddFriendClicked] Запрос успешно отправлен");
                if (GlobalSearchPanel.IsVisible)
                    await UpdateSearchResultUserStatusAsync(targetUserId);
            }
            else
            {
                Console.WriteLine($"[OnAddFriendClicked] Ошибка при отправлении запроса");
            }
        }

        private async Task OnAcceptRequestClicked(uint friendId)
        {
            Console.WriteLine($"[OnAcceptRequestClicked] Принятие запроса от {friendId}");
            
            bool success = await _friendService.AcceptFriendRequestAsync(_currentUserId, friendId);
            if (success)
            {
                Console.WriteLine($"[OnAcceptRequestClicked] Запрос принят");
                if (GlobalSearchPanel.IsVisible)
                    await UpdateSearchResultUserStatusAsync(friendId);
                else
                    await LoadFriendRequestsAsync();
            }
            else
            {
                Console.WriteLine($"[OnAcceptRequestClicked] Ошибка при принятии запроса");
            }
        }

        private async Task OnRejectRequestClicked(uint friendId)
        {
            Console.WriteLine($"[OnRejectRequestClicked] Отклонение запроса от {friendId}");
            
            bool success = await _friendService.RejectFriendRequestAsync(_currentUserId, friendId);
            if (success)
            {
                Console.WriteLine($"[OnRejectRequestClicked] Запрос отклонен");
                if (GlobalSearchPanel.IsVisible)
                    await UpdateSearchResultUserStatusAsync(friendId);
                else
                    await LoadFriendRequestsAsync();
            }
            else
            {
                Console.WriteLine($"[OnRejectRequestClicked] Ошибка при отклонении запроса");
            }
        }

        private async Task OnCancelRequestClicked(uint targetUserId)
        {
            Console.WriteLine($"[OnCancelRequestClicked] Отмена исходящего запроса для пользователя {targetUserId}");
            
            bool success = await _friendService.CancelFriendRequestAsync(_currentUserId, targetUserId);
            if (success)
            {
                Console.WriteLine($"[OnCancelRequestClicked] Запрос отменен");
                if (GlobalSearchPanel.IsVisible)
                    await UpdateSearchResultUserStatusAsync(targetUserId);
                else
                    await LoadFriendRequestsAsync();
            }
            else
            {
                Console.WriteLine($"[OnCancelRequestClicked] Ошибка при отмене запроса");
            }
        }

        private void ResetTabsState()
        {
            SearchTabButton.Classes.Remove("secondary");
            FriendsTabButton.Classes.Remove("secondary");

            if (!SearchTabButton.Classes.Contains("outlined"))
                SearchTabButton.Classes.Add("outlined");

            if (!FriendsTabButton.Classes.Contains("outlined"))
                FriendsTabButton.Classes.Add("outlined");
        }

        private void SetActiveTab(Button activeButton, Button inactiveButton)
        {
            if (!activeButton.Classes.Contains("secondary"))
                activeButton.Classes.Add("secondary");
            activeButton.Classes.Remove("outlined");

            inactiveButton.Classes.Remove("secondary");
            if (!inactiveButton.Classes.Contains("outlined"))
                inactiveButton.Classes.Add("outlined");
        }

        private void ShowChatPanel()
        {
            ChatPanel.IsVisible = true;
            GlobalSearchPanel.IsVisible = false;
            FriendsPanel.IsVisible = false;
            ResetTabsState();
        }

        private void ShowSearchPanel()
        {
            ChatPanel.IsVisible = false;
            GlobalSearchPanel.IsVisible = true;
            FriendsPanel.IsVisible = false;
            SearchGlobalTextBox.Text = string.Empty;
            GlobalUsersListBox.ItemsSource = new List<SearchResultItemDto>();
            ResetPagination();
            _currentSearchQuery = string.Empty;
            SetActiveTab(SearchTabButton, FriendsTabButton);
        }

        private void ShowFriendsPanel()
        {
            ChatPanel.IsVisible = false;
            GlobalSearchPanel.IsVisible = false;
            FriendsPanel.IsVisible = true;
            SetActiveTab(FriendsTabButton, SearchTabButton);
        }

        private async Task RemoveEmptyPendingChatIfCurrentAsync()
        {
            try
            {
                if (_currentChatId.HasValue &&
                    _pendingChats.TryGetValue(_currentChatId.Value, out _))
                {
                    var messages = MessagesListBox.ItemsSource as List<MessageListItem>;
                    var hasMessages = messages != null && messages.Count > 0;
                    var hasDraft = !string.IsNullOrWhiteSpace(MessageTextBox.Text);

                    if (!hasMessages && !hasDraft)
                    {
                        var removedId = _currentChatId.Value;
                        _pendingChats.Remove(removedId);
                        Console.WriteLine($"[RemoveEmptyPendingChatIfCurrentAsync] Удален временный чат без сообщений: {removedId}");

                        var chats = await _chatService.GetUserChatsAsync();
                        await Dispatcher.UIThread.InvokeAsync(() => UpdateChatsList(chats));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoveEmptyPendingChatIfCurrentAsync] Ошибка: {ex.Message}");
            }
        }

        private async Task InitializeAsync()
        {
            try
            {
                Console.WriteLine($"[InitializeAsync] Начинаем инициализацию пользователя: {_currentUsername}");

                // Устанавливаем timeout для инициализации
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));

                try
                {
                    _currentUserId = await _chatService.InitializeUserAsync(_currentUsername);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[InitializeAsync] Ошибка инициализации пользователя: {ex.Message}");
                    _currentUserId = 0;
                }

                Console.WriteLine($"[InitializeAsync] User ID загружен: {_currentUserId}");

                try
                {
                    var chats = await _chatService.GetUserChatsAsync();
                    await Dispatcher.UIThread.InvokeAsync(() => UpdateChatsList(chats));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[InitializeAsync] Ошибка загрузки чатов: {ex.Message}");
                }

                // Загружаем аватарку пользователя в фоне, не дожидаясь
                try
                {
                    Console.WriteLine($"[InitializeAsync] Загружаем аватарку пользователя");
                    _ = LoadUserAvatarAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[InitializeAsync] Ошибка загрузки аватарки: {ex.Message}");
                }

                // Сигнализируем что основная загрузка завершена
                await Dispatcher.UIThread.InvokeAsync(() => LoadingCompleted?.Invoke(this, EventArgs.Empty));

                // Открываем панель друзей и выбираем вкладку "Друзья"
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ShowFriendsPanel();
                    OnFriendsTabButton2Clicked(FriendsTabButton2, new RoutedEventArgs());
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[InitializeAsync] Неожиданная ошибка: {ex.Message}");

                // Даже при ошибке снимаем блокировку загрузочного окна
                await Dispatcher.UIThread.InvokeAsync(() => LoadingCompleted?.Invoke(this, EventArgs.Empty));
            }
        }

        private void UpdateChatsList(List<ChatDto> chats)
        {
            try
            {
                var validChats = chats.Where(c => c != null && c.Id > 0).ToList();

                var items = validChats.Select(c => new ChatListItem
                {
                    ChatId = c.Id,
                    DisplayName = c.Name,
                    LastMessage = c.LastMessage,
                    LastMessageTime = c.LastMessageTime.ToString("HH:mm")
                }).ToList();

                UsersListBox.ItemsSource = items;
                Console.WriteLine($"[UpdateChatsList] Список обновлен: {validChats.Count} реальных чатов, временные не отображаются");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateChatsList] Ошибка: {ex.Message}");
            }
        }

        private void UpdateCurrentChatPreview(uint chatId, string content, DateTime timestamp)
        {
            try
            {
                var items = (UsersListBox.ItemsSource as List<ChatListItem> ?? new List<ChatListItem>()).ToList();
                var existing = items.FirstOrDefault(i => i.ChatId == chatId);
                if (existing == null)
                    return;

                existing.LastMessage = content.Length > 50 ? content.Substring(0, 50) + "..." : content;
                existing.LastMessageTime = timestamp.ToString("HH:mm");

                items = items
                    .OrderByDescending(i => i.ChatId == chatId)
                    .ThenByDescending(i =>
                    {
                        if (DateTime.TryParse(i.LastMessageTime, out var parsed))
                            return parsed;
                        return DateTime.MinValue;
                    })
                    .ToList();

                UsersListBox.ItemsSource = items;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateCurrentChatPreview] Ошибка: {ex.Message}");
            }
        }

        private async void OnChatSelected(object? sender, SelectionChangedEventArgs e)
        {
            ChatPanel.IsVisible = true;
            GlobalSearchPanel.IsVisible = false;
            FriendsPanel.IsVisible = false;

            await RemoveEmptyPendingChatIfCurrentAsync();

            if (UsersListBox.SelectedItem is ChatListItem selectedChat && selectedChat.ChatId != 0)
            {
                Console.WriteLine($"[OnChatSelected] Выбран чат: {selectedChat.DisplayName} (ID: {selectedChat.ChatId})");

                _currentChatId = selectedChat.ChatId;
                _currentChatUserName = selectedChat.DisplayName;
                ChatUserName.Text = selectedChat.DisplayName;

                if (_pendingChats.TryGetValue(selectedChat.ChatId, out var pendingChat))
                {
                    Console.WriteLine($"[OnChatSelected] Это временный чат с пользователем {pendingChat.Name}");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        MessagesListBox.ItemsSource = new List<MessageListItem>();
                    });
                }
                else
                {
                    Console.WriteLine($"[OnChatSelected] Загружаем историю чата {selectedChat.ChatId}");
                    var messages = await _chatService.LoadChatHistoryAsync(selectedChat.ChatId);
                    await UpdateMessagesListAsync(messages);

                    var otherUserId = await _chatService.GetOtherUserIdForChat(selectedChat.ChatId);
                    if (otherUserId > 0)
                    {
                        Console.WriteLine($"[OnChatSelected] Открываем WebSocket для чата {selectedChat.ChatId} с пользователем {otherUserId}");
                        await _webSocketService.OpenAsync(selectedChat.ChatId, _currentUserId, otherUserId);
                        _ = LoadChatUserAvatarAsync(otherUserId);
                    }

                    // Подключаемся к сигнальной комнате для получения входящих звонков
                    if (!_voiceCallService.IsCallActive)
                        _ = ConnectToSignalingRoomAsync(selectedChat.ChatId);
                }
            }
        }

        private async Task UpdateMessagesListAsync(List<MessageDto> messages)
        {
            try
            {
                Console.WriteLine($"[UpdateMessagesListAsync] Обновляем список сообщений: {messages.Count} сообщений");
                var sortedMessages = messages.OrderBy(m => m.CreatedAt).ToList();
                var items = new List<MessageListItem>();

                foreach (var msg in sortedMessages)
                {
                    var senderName = await ResolveSenderNameAsync(msg.SenderId);
                    items.Add(new MessageListItem
                    {
                        Id = msg.Id,
                        ChatId = msg.ChatId,
                        SenderId = msg.SenderId,
                        Content = msg.Content,
                        IsOwnMessage = msg.SenderId == _currentUserId,
                        Time = msg.CreatedAt.ToString("HH:mm"),
                        SenderName = senderName
                    });
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    MessagesListBox.ItemsSource = items;
                    ScrollToLatest();
                });

                Console.WriteLine($"[UpdateMessagesListAsync] Готово. Показано {items.Count} сообщений");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateMessagesListAsync] Ошибка: {ex.Message}");
            }
        }

        private async Task<string> ResolveSenderNameAsync(uint senderId)
        {
            if (senderId == _currentUserId)
                return _currentUsername;

            if (!string.IsNullOrWhiteSpace(_currentChatUserName))
                return _currentChatUserName;

            var loaded = await _chatService.GetUserNameByIdAsync(senderId);
            return !string.IsNullOrWhiteSpace(loaded) ? loaded : $"Пользователь {senderId}";
        }

        private void ScrollToLatest()
        {
            try
            {
                if (MessagesListBox.ItemCount > 0)
                {
                    MessagesListBox.ScrollIntoView(MessagesListBox.ItemCount - 1);
                    Console.WriteLine("[ScrollToLatest] Прокручено к последнему сообщению");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScrollToLatest] Ошибка: {ex.Message}");
            }
        }

        private async void OnSendMessageClicked(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(MessageTextBox.Text))
            {
                Console.WriteLine("[OnSendMessageClicked] Сообщение пусто");
                return;
            }

            if (!_currentChatId.HasValue)
            {
                Console.WriteLine("[OnSendMessageClicked] No active chat");
                return;
            }

            try
            {
                var message = MessageTextBox.Text.Trim();
                Console.WriteLine($"[OnSendMessageClicked] Отправляем в чат {_currentChatId}: {message.Substring(0, Math.Min(50, message.Length))}");

                if (_pendingChats.TryGetValue(_currentChatId.Value, out var pendingChat))
                {
                    Console.WriteLine($"[OnSendMessageClicked] Обнаружен временный чат. Создаем на сервере...");
                    
                    var realChatId = await _chatService.CreateChatAsync(pendingChat.OtherUserId);
                    if (realChatId == null)
                    {
                        Console.WriteLine("[OnSendMessageClicked] Ошибка создания чата на сервере");
                        return;
                    }

                    Console.WriteLine($"[OnSendMessageClicked] Чат создан на сервере: {realChatId}");
                    
                    // Кэшируем информацию о чате перед обновлением списка
                    _chatService.PreCacheChatInfo(realChatId.Value, pendingChat.OtherUserId, _currentChatUserName);
                    
                    _pendingChats.Remove(_currentChatId.Value);
                    _currentChatId = realChatId;

                    await _webSocketService.OpenAsync(realChatId.Value, _currentUserId, pendingChat.OtherUserId);
                    
                    var chats = await _chatService.GetUserChatsAsync();
                    UpdateChatsList(chats);
                }

                MessageTextBox.Text = string.Empty;

                // Пытаемся сохранить сообщение через REST, чтобы оно грузилось после перезапуска
                var saved = await _chatService.SendMessageAsync(_currentChatId.Value, message);
                var sentViaWebSocketFallback = saved == null;
                if (saved == null)
                {
                    // Фоллбек на WebSocket (если backend не поддерживает REST-эндпоинт сохранения)
                    await _webSocketService.SendAsync(message);
                }

                if (!sentViaWebSocketFallback && saved != null)
                {
                    var messages = (MessagesListBox.ItemsSource as List<MessageListItem> ?? new()).ToList();
                    messages.Add(new MessageListItem
                    {
                        Id = saved.Id,
                        ChatId = saved.ChatId,
                        SenderId = saved.SenderId,
                        Content = saved.Content,
                        IsOwnMessage = saved.SenderId == _currentUserId,
                        Time = saved.CreatedAt.ToString("HH:mm"),
                        SenderName = $"{_currentUsername}"
                    });
                    MessagesListBox.ItemsSource = messages;
                    UpdateCurrentChatPreview(saved.ChatId, saved.Content, saved.CreatedAt);
                }

                Console.WriteLine("[OnSendMessageClicked] Сообщение отправлено");
                if (!sentViaWebSocketFallback)
                    ScrollToLatest();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OnSendMessageClicked] Ошибка: {ex.Message}");
            }
        }

        private async void OnCallButtonClicked(object? sender, RoutedEventArgs e)
        {
            if (_voiceCallService.IsCallActive)
            {
                await EndCallAsync();
                return;
            }

            if (!_currentChatId.HasValue || _pendingChats.ContainsKey(_currentChatId.Value))
            {
                Console.WriteLine("[OnCallButtonClicked] Нет активного чата для звонка");
                return;
            }

            Console.WriteLine($"[OnCallButtonClicked] Инициируем звонок: {_currentChatUserName} (чат {_currentChatId})");
            var roomId = $"chat-{_currentChatId.Value}";
            await _signalingService.ConnectAsync(roomId, _currentUserId.ToString());
            var started = await _voiceCallService.InitiateCallAsync(_selectedMicIndex, _selectedSpeakerIndex);
            if (started)
            {
                UpdateCallUI(true);
                CallStatusText.Text = $"📞 Звонок: {_currentChatUserName}...";
            }
            else
            {
                Console.WriteLine("[OnCallButtonClicked] InitiateCallAsync вернул false — звонок не начат");
                await _signalingService.LeaveAsync();
                _signalingService.Disconnect();
            }
        }

        private async void OnWebSocketMessageReceived(object? sender, WebSocketMessageEventArgs e)
        {
            if (e.ChatId != _currentChatId)
                return;

            Console.WriteLine($"[OnWebSocketMessageReceived] Сообщение в чат {e.ChatId}: {e.Content.Substring(0, Math.Min(50, e.Content.Length))}");

            var senderName = await ResolveSenderNameAsync(e.SenderUId);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var messages = (MessagesListBox.ItemsSource as List<MessageListItem> ?? new()).ToList();
                if (e.Id != 0 && messages.Any(m => m.Id == e.Id))
                    return;

                if (e.Id == 0 && messages.Any(m =>
                    m.ChatId == e.ChatId &&
                    m.SenderId == e.SenderUId &&
                    string.Equals(m.Content, e.Content, StringComparison.Ordinal) &&
                    m.Time == e.CreatedAt.ToString("HH:mm")))
                    return;

                messages.Add(new MessageListItem
                {
                    Id = e.Id,
                    ChatId = e.ChatId,
                    SenderId = e.SenderUId,
                    Content = e.Content,
                    IsOwnMessage = e.SenderUId == _currentUserId,
                    Time = e.CreatedAt.ToString("HH:mm"),
                    SenderName = senderName
                });
                MessagesListBox.ItemsSource = messages;
                UpdateCurrentChatPreview(e.ChatId, e.Content, e.CreatedAt);
                ScrollToLatest();
            });
        }

        private async Task PerformSearchAsync(int page = 0)
        {
            var query = SearchGlobalTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                Console.WriteLine("[PerformSearchAsync] Запрос пуст");
                GlobalUsersListBox.ItemsSource = new List<SearchResultItemDto>();
                ResetPagination();
                return;
            }

            try
            {
                Console.WriteLine($"[PerformSearchAsync] Поиск: '{query}' (страница {page + 1})");
                _currentSearchQuery = query;
                _currentPage = page;

                var response = await _userSearchService.SearchAsync(query, page, 8);
                if (response != null)
                {
                    _totalPages = response.TotalPages;
                    UpdatePaginationInfo();

                    var searchResults = new List<SearchResultItemDto>();
                    foreach (var user in response.Users ?? new())
                    {
                        var status = await _friendService.CheckFriendshipStatusAsync(_currentUserId, (uint)user.Id);

                        // Получаем полную информацию о пользователе, включая AvatarUrl
                        var userInfo = await _userSearchService.GetUserByIdAsync((uint)user.Id);
                        var avatarUrl = userInfo?.AvatarUrl ?? user.AvatarUrl;

                        var result = new SearchResultItemDto
                        {
                            Id = (uint)user.Id,
                            Username = user.Username,
                            AvatarUrl = avatarUrl,
                            IsFriend = status?.Status == "ACCEPTED",
                            HasIncomingRequest = status?.Status == "PENDING" && !status.IsOutgoingRequest,
                            HasOutgoingRequest = status?.Status == "PENDING" && status.IsOutgoingRequest
                        };
                        searchResults.Add(result);
                    }
                    
                    _currentSearchResults = searchResults;
                    GlobalUsersListBox.ItemsSource = searchResults;
                    Console.WriteLine($"[PerformSearchAsync] Найдено {searchResults.Count} пользователей на странице {page + 1}/{_totalPages}");

                    // Загружаем аватарки в фоне
                    _ = LoadSearchUserAvatarsAsync(searchResults);

                    AttachSearchResultButtonHandlers();
                }
                else
                {
                    Console.WriteLine("[PerformSearchAsync] Ошибка запроса");
                    ResetPagination();
                    GlobalUsersListBox.ItemsSource = new List<SearchResultItemDto>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PerformSearchAsync] Ошибка: {ex.Message}");
                GlobalUsersListBox.ItemsSource = new List<SearchResultItemDto>();
                ResetPagination();
            }
        }

        private async Task UpdateSearchResultUserStatusAsync(uint userId)
        {
            Console.WriteLine($"[UpdateSearchResultUserStatusAsync] Обновляем статус пользователя {userId}");
            
            // Ищем пользователя в текущих результатах
            var userResult = _currentSearchResults?.FirstOrDefault(u => u.Id == userId);
            if (userResult == null)
            {
                Console.WriteLine($"[UpdateSearchResultUserStatusAsync] Пользователь {userId} не найден в результатах");
                return;
            }

            // Получаем свежий статус с сервера
            var status = await _friendService.CheckFriendshipStatusAsync(_currentUserId, userId);
            
            // Обновляем свойства на основе нового статуса
            userResult.IsFriend = status?.Status == "ACCEPTED";
            userResult.HasIncomingRequest = status?.Status == "PENDING" && !status.IsOutgoingRequest;
            userResult.HasOutgoingRequest = status?.Status == "PENDING" && status.IsOutgoingRequest;
            
            Console.WriteLine($"[UpdateSearchResultUserStatusAsync] Статус обновлен: IsFriend={userResult.IsFriend}, HasIncoming={userResult.HasIncomingRequest}, HasOutgoing={userResult.HasOutgoingRequest}");
            
            // Обновляем список в UI
            GlobalUsersListBox.ItemsSource = new List<SearchResultItemDto>(_currentSearchResults);
            AttachSearchResultButtonHandlers();
        }

        private async Task OnSearchUsersAsync()
        {
            await Task.Delay(300);
            if (SearchGlobalTextBox.Text != _currentSearchQuery)
            {
                Console.WriteLine("[OnSearchUsersAsync] Запускаем поиск");
                _ = PerformSearchAsync(0);
            }
        }

        private void OnSearchUsersClicked(object? sender, RoutedEventArgs e)
        {
            Console.WriteLine("[OnSearchUsersClicked] Открыта панель поиска");
            ShowSearchPanel();
        }

        private void OnBackToChatClicked(object? sender, RoutedEventArgs e)
        {
            Console.WriteLine("[OnBackToChatClicked] Возврат к чатам");
            ShowChatPanel();
        }

        private void OnSearchTabClicked(object? sender, RoutedEventArgs e)
        {
            _ = RemoveEmptyPendingChatIfCurrentAsync();
            Console.WriteLine("[OnSearchTabClicked] Выбрана вкладка поиска");
            ShowSearchPanel();
        }

        private async void OnFriendsTabClicked(object? sender, RoutedEventArgs e)
        {
            _ = RemoveEmptyPendingChatIfCurrentAsync();
            Console.WriteLine("[OnFriendsTabClicked] Выбрана вкладка друзей");
            ShowFriendsPanel();
            
            FriendsTabContent.IsVisible = true;
            RequestsTabContent.IsVisible = false;
            
            if (FriendsTabButton2.Classes.Contains("outlined"))
                FriendsTabButton2.Classes.Remove("outlined");
            if (!FriendsTabButton2.Classes.Contains("secondary"))
                FriendsTabButton2.Classes.Add("secondary");

            if (!RequestsTabButton.Classes.Contains("outlined"))
                RequestsTabButton.Classes.Add("outlined");
            RequestsTabButton.Classes.Remove("secondary");

            await LoadFriendsAsync();
        }

        private void OnBackFromFriendsClicked(object? sender, RoutedEventArgs e)
        {
            Console.WriteLine("[OnBackFromFriendsClicked] Возврат к чатам из друзей");
            ShowChatPanel();
        }

        private async void OnGlobalUserSelected(object? sender, RoutedEventArgs e)
        {
            if (GlobalUsersListBox.SelectedItem is SearchResultItemDto item)
            {
                Console.WriteLine($"[OnGlobalUserSelected] Выбран пользователь: {item.Username}");

                GlobalSearchPanel.IsVisible = false;
                ChatPanel.IsVisible = true;

                _currentChatUserName = item.Username;
                ChatUserName.Text = item.Username;
                _ = LoadChatUserAvatarAsync(item.Id);

                var tempChatId = (uint)(Guid.NewGuid().GetHashCode() & 0x7FFFFFFF);
                var pendingChat = new ChatDto
                {
                    Id = tempChatId,
                    Name = item.Username,
                    LastMessage = "Начните общение",
                    LastMessageTime = DateTime.Now,
                    OtherUserId = item.Id,
                    IsPending = true
                };

                _pendingChats[tempChatId] = pendingChat;
                _currentChatId = pendingChat.Id;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    MessagesListBox.ItemsSource = new List<MessageListItem>();
                });

                Console.WriteLine($"[OnGlobalUserSelected] Создан временный чат. ID: {tempChatId}");
                var chats = await _chatService.GetUserChatsAsync();
                UpdateChatsList(chats);
            }
        }

        private void OnPrevPageClick(object? sender, RoutedEventArgs e)
        {
            if (_currentPage > 0)
            {
                Console.WriteLine($"[OnPrevPageClick] Предыдущая страница: {_currentPage} -> {_currentPage - 1}");
                _ = PerformSearchAsync(_currentPage - 1);
            }
        }

        private void OnNextPageClick(object? sender, RoutedEventArgs e)
        {
            if (_currentPage < _totalPages - 1)
            {
                Console.WriteLine($"[OnNextPageClick] Следующая страница: {_currentPage} -> {_currentPage + 1}");
                _ = PerformSearchAsync(_currentPage + 1);
            }
        }

        private void UpdatePaginationInfo()
        {
            PageInfoText.Text = $"Страница {_currentPage + 1} из {_totalPages}";
            PrevPageButton.IsEnabled = _currentPage > 0;
            NextPageButton.IsEnabled = _currentPage < _totalPages - 1;
            Console.WriteLine($"[UpdatePaginationInfo] {PageInfoText.Text}");
        }

        private void ResetPagination()
        {
            _currentPage = 0;
            _totalPages = 1;
            PageInfoText.Text = "Страница 1 из 1";
            PrevPageButton.IsEnabled = false;
            NextPageButton.IsEnabled = false;
            Console.WriteLine("[ResetPagination] Пагинация сброшена");
        }

        private void OnSettingsClicked(object? sender, RoutedEventArgs e)
        {
            Console.WriteLine("[OnSettingsClicked] Открыты настройки");
            MainPanels.IsVisible = false;
            LeftSearchPanel.IsVisible = false;
            SettingsPanel.IsVisible = true;
        }

        private void OnVoiceSettingsClicked(object? sender, RoutedEventArgs e)
        {
            Console.WriteLine("[OnVoiceSettingsClicked] Открыты голосовые настройки");
            VoicePanel.IsVisible = true;
            AvatarPanel.IsVisible = false;
            LoadAudioDevices();
        }

        private void ResetAvatarButtonState()
        {
            Console.WriteLine("[MainChatWindow.ResetAvatarButtonState] Сброс состояния кнопки аватарки");
            _isAvatarPendingConfirmation = false;
            _selectedAvatarFilePath = string.Empty;
            _resizedAvatarData = Array.Empty<byte>();
            SelectAvatarButton.Content = "Сменить аватарку";
        }

        private async void OnAvatarSettingsClicked(object? sender, RoutedEventArgs e)
        {
            Console.WriteLine("[MainChatWindow.OnAvatarSettingsClicked] Открыта панель настроек аватарки");
            AvatarPanel.IsVisible = true;
            VoicePanel.IsVisible = false;
            AvatarStatusMessage.Text = string.Empty;
            ResetAvatarButtonState();
            Console.WriteLine("[MainChatWindow.OnAvatarSettingsClicked] Состояние сброшено, начинаем загрузку аватарки");
            await LoadUserAvatarAsync();
        }

        private void OnBackFromSettingsClicked(object? sender, RoutedEventArgs e)
        {
            Console.WriteLine("[OnBackFromSettingsClicked] Закрыты настройки");
            SettingsPanel.IsVisible = false;
            VoicePanel.IsVisible = false;
            AvatarPanel.IsVisible = false;
            MainPanels.IsVisible = true;
            LeftSearchPanel.IsVisible = true;
        }

        private void LoadAudioDevices()
        {
            try
            {
                Console.WriteLine("[LoadAudioDevices] Загружаем устройства...");
                var enumerator = new MMDeviceEnumerator();
                var mics = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                    .Select(d => d.FriendlyName).ToList();
                MicrophoneComboBox.ItemsSource = mics;
                Console.WriteLine($"[LoadAudioDevices] Микрофонов: {mics.Count}");

                var outputs = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                    .Select(d => d.FriendlyName).ToList();
                AudioOutputComboBox.ItemsSource = outputs;
                Console.WriteLine($"[LoadAudioDevices] Выходов: {outputs.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LoadAudioDevices] Ошибка: {ex.Message}");
            }
        }

        private void OnTestVoiceClicked(object? sender, RoutedEventArgs e)
        {
            if (!_isVoiceTestActive)
            {
                int micIndex = MicrophoneComboBox.SelectedIndex;
                int outputIndex = AudioOutputComboBox.SelectedIndex;

                if (micIndex < 0 || outputIndex < 0)
                {
                    Console.WriteLine("[OnTestVoiceClicked] Не выбраны устройства");
                    return;
                }

                _voiceTestService.OnAudioLevelChanged += OnAudioLevelChanged;
                _voiceTestService.Start(micIndex, outputIndex);
                TestVoiceButton.Content = "Остановить";
                _isVoiceTestActive = true;
                VoiceVolumeSlider.IsEnabled = true;
            }
            else
            {
                _voiceTestService.Stop();
                _voiceTestService.OnAudioLevelChanged -= OnAudioLevelChanged;
                TestVoiceButton.Content = "Проверить голос";
                _isVoiceTestActive = false;
                VoiceVolumeSlider.IsEnabled = false;
                VoiceVolumeSlider.Value = 0;
            }
        }

        private void OnAudioLevelChanged(object? sender, AudioLevelChangedEventArgs e)
        {
            VoiceVolumeSlider.Value = e.CurrentVolume;
        }

        private async void OnSelectAvatarClicked(object? sender, RoutedEventArgs e)
        {
            // Если аватарка уже выбрана и ожидает подтверждения
            if (_isAvatarPendingConfirmation)
            {
                Console.WriteLine("[MainChatWindow.OnSelectAvatarClicked] Подтверждение смены аватарки");
                await OnConfirmAvatarClicked();
                return;
            }

            // Иначе - выбор файла
            Console.WriteLine("[MainChatWindow.OnSelectAvatarClicked] Открыт диалог выбора аватарки");
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null)
                {
                    Console.WriteLine("[MainChatWindow.OnSelectAvatarClicked] Ошибка: не удалось получить TopLevel");
                    return;
                }

                Console.WriteLine("[MainChatWindow.OnSelectAvatarClicked] Показываем диалог выбора файла");
                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Выберите файл аватарки",
                    AllowMultiple = false,
                    FileTypeFilter = new[] {
                        new FilePickerFileType("Изображения") {
                            Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif" }
                        }
                    }
                });

                if (files.Count > 0)
                {
                    var file = files[0];
                    var path = file.Path.LocalPath;
                    Console.WriteLine($"[MainChatWindow.OnSelectAvatarClicked] Файл выбран: {path}");

                    // Сразу масштабируем для предпросмотра
                    Console.WriteLine("[MainChatWindow.OnSelectAvatarClicked] Масштабируем изображение до 512x512 для предпросмотра");
                    var resizedImageData = _userAvatarService.ResizeImageTo512(path);

                    if (resizedImageData.Length == 0)
                    {
                        Console.WriteLine("[MainChatWindow.OnSelectAvatarClicked] Ошибка: не удалось масштабировать изображение");
                        AvatarStatusMessage.Text = "Ошибка обработки изображения";
                        AvatarStatusMessage.Foreground = Avalonia.Media.Brushes.Red;
                        return;
                    }

                    // Показываем масштабированный предпросмотр
                    Console.WriteLine("[MainChatWindow.OnSelectAvatarClicked] Отображаем масштабированный предпросмотр (512x512)");
                    var previewImage = new Avalonia.Media.Imaging.Bitmap(new System.IO.MemoryStream(resizedImageData));
                    AvatarImage.Source = previewImage;

                    // Сохраняем путь, масштабированные данные и меняем состояние
                    _selectedAvatarFilePath = path;
                    _resizedAvatarData = resizedImageData;
                    _isAvatarPendingConfirmation = true;
                    SelectAvatarButton.Content = "Подтвердить смену аватарки";
                    AvatarStatusMessage.Text = string.Empty;
                    Console.WriteLine("[MainChatWindow.OnSelectAvatarClicked] Кнопка изменена на 'Подтвердить смену аватарки'");
                }
                else
                {
                    Console.WriteLine("[MainChatWindow.OnSelectAvatarClicked] Файл не выбран");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainChatWindow.OnSelectAvatarClicked] Ошибка: {ex.Message}");
                AvatarStatusMessage.Text = "Ошибка выбора файла";
                AvatarStatusMessage.Foreground = Avalonia.Media.Brushes.Red;
            }
        }

        private async Task OnConfirmAvatarClicked()
        {
            Console.WriteLine("[MainChatWindow.OnConfirmAvatarClicked] Начинаем процесс подтверждения и загрузки аватарки");

            if (string.IsNullOrEmpty(_selectedAvatarFilePath) || _resizedAvatarData.Length == 0)
            {
                Console.WriteLine("[MainChatWindow.OnConfirmAvatarClicked] Ошибка: путь к файлу или масштабированные данные не установлены");
                AvatarStatusMessage.Text = "Неудачная смена аватарки";
                AvatarStatusMessage.Foreground = Avalonia.Media.Brushes.Red;
                ResetAvatarButtonState();
                return;
            }

            try
            {
                // Используем уже масштабированные данные из предпросмотра
                Console.WriteLine("[MainChatWindow.OnConfirmAvatarClicked] Используем сохранённые масштабированные данные (512x512)");
                var resizedImageData = _resizedAvatarData;

                // Определяем тип контента и имя файла
                var ext = System.IO.Path.GetExtension(_selectedAvatarFilePath).ToLower();
                var fileName = System.IO.Path.GetFileName(_selectedAvatarFilePath);
                var contentType = ext switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".webp" => "image/webp",
                    _ => "image/jpeg"
                };
                Console.WriteLine($"[MainChatWindow.OnConfirmAvatarClicked] Тип контента: {contentType}, Имя файла: {fileName}");

                // Получаем URL для загрузки
                Console.WriteLine("[MainChatWindow.OnConfirmAvatarClicked] Запрашиваем URL для загрузки");
                var uploadUrlResponse = await _userAvatarService.GetAvatarUploadUrlAsync(contentType, fileName);
                if (string.IsNullOrEmpty(uploadUrlResponse.UploadUrl))
                {
                    Console.WriteLine("[MainChatWindow.OnConfirmAvatarClicked] Ошибка: не удалось получить URL для загрузки");
                    AvatarStatusMessage.Text = "Неудачная смена аватарки";
                    AvatarStatusMessage.Foreground = Avalonia.Media.Brushes.Red;
                    ResetAvatarButtonState();
                    return;
                }

                // Загружаем аватарку
                Console.WriteLine("[MainChatWindow.OnConfirmAvatarClicked] Начинаем загрузку аватарки на сервер");
                bool uploadSuccess = await _userAvatarService.UploadAvatarAsync(uploadUrlResponse.UploadUrl, resizedImageData, contentType);

                if (uploadSuccess)
                {
                    Console.WriteLine("[MainChatWindow.OnConfirmAvatarClicked] Загрузка успешна, обновляем данные пользователя");
                    AvatarStatusMessage.Text = "Успешная смена аватарки";
                    AvatarStatusMessage.Foreground = Avalonia.Media.Brushes.Green;
                    ResetAvatarButtonState();
                    // Перезагружаем пользовательские данные
                    await LoadUserAvatarAsync();
                }
                else
                {
                    Console.WriteLine("[MainChatWindow.OnConfirmAvatarClicked] Ошибка при загрузке аватарки");
                    AvatarStatusMessage.Text = "Неудачная смена аватарки";
                    AvatarStatusMessage.Foreground = Avalonia.Media.Brushes.Red;
                    ResetAvatarButtonState();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainChatWindow.OnConfirmAvatarClicked] Ошибка: {ex.Message}");
                AvatarStatusMessage.Text = "Неудачная смена аватарки";
                AvatarStatusMessage.Foreground = Avalonia.Media.Brushes.Red;
                ResetAvatarButtonState();
            }
        }

        /// <summary>Загружает аватарку собеседника в шапку чата по его userId.</summary>
        private async Task LoadChatUserAvatarAsync(uint userId)
        {
            // Сразу сбрасываем старую аватарку — до первого await, на UI-потоке
            ChatUserAvatarImage.Source = null;

            try
            {
                var userInfo = await _userSearchService.GetUserByIdAsync(userId);
                if (string.IsNullOrEmpty(userInfo?.AvatarUrl))
                {
                    await Dispatcher.UIThread.InvokeAsync(() => ChatUserAvatarImage.Source = null);
                    return;
                }

                using var httpClient = new System.Net.Http.HttpClient();
                var imageData = await httpClient.GetByteArrayAsync(userInfo.AvatarUrl);
                var bitmap = new Avalonia.Media.Imaging.Bitmap(new System.IO.MemoryStream(imageData));
                await Dispatcher.UIThread.InvokeAsync(() => ChatUserAvatarImage.Source = bitmap);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LoadChatUserAvatarAsync] Ошибка загрузки аватарки для userId={userId}: {ex.Message}");
            }
        }

        private async Task LoadUserAvatarAsync()
        {
            try
            {
                Console.WriteLine("[MainChatWindow.LoadUserAvatarAsync] Начало загрузки аватарки пользователя");
                var user = await _userAvatarService.GetCurrentUserAsync();

                if (!string.IsNullOrEmpty(user.AvatarUrl))
                {
                    Console.WriteLine($"[MainChatWindow.LoadUserAvatarAsync] URL аватарки: {user.AvatarUrl}");
                    using (var httpClient = new System.Net.Http.HttpClient())
                    {
                        try
                        {
                            Console.WriteLine($"[MainChatWindow.LoadUserAvatarAsync] Загружаем изображение с сервера");
                            var imageData = await httpClient.GetByteArrayAsync(user.AvatarUrl);
                            Console.WriteLine($"[MainChatWindow.LoadUserAvatarAsync] Изображение получено, размер: {imageData.Length} байт");

                            var bitmap = new Avalonia.Media.Imaging.Bitmap(new System.IO.MemoryStream(imageData));

                            // Загружаем аватарку в панель настроек
                            AvatarImage.Source = bitmap;
                            Console.WriteLine("[MainChatWindow.LoadUserAvatarAsync] Аватарка отображена в панели настроек");

                            // Загружаем аватарку в левый нижний угол
                            UserAvatarImage.Source = bitmap;
                            Console.WriteLine("[MainChatWindow.LoadUserAvatarAsync] Аватарка отображена в левом нижнем углу");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[MainChatWindow.LoadUserAvatarAsync] Ошибка загрузки изображения: {ex.Message}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("[MainChatWindow.LoadUserAvatarAsync] URL аватарки не установлен, очищаем изображение");
                    AvatarImage.Source = null;
                    UserAvatarImage.Source = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainChatWindow.LoadUserAvatarAsync] Ошибка: {ex.Message}");
            }
        }

        private async Task LoadSearchUserAvatarsAsync(List<SearchResultItemDto> users)
        {
            try
            {
                Console.WriteLine($"[MainChatWindow.LoadSearchUserAvatarsAsync] Загружаем аватарки для {users.Count} пользователей");

                using (var httpClient = new System.Net.Http.HttpClient())
                {
                    var tasks = users.Select(async user =>
                    {
                        if (string.IsNullOrEmpty(user.AvatarUrl))
                            return;
                        var imageData = await httpClient.GetByteArrayAsync(user.AvatarUrl);
                        var bitmap = new Avalonia.Media.Imaging.Bitmap(new System.IO.MemoryStream(imageData));
                        await Dispatcher.UIThread.InvokeAsync(() => user.AvatarBitmap = bitmap);
                    });
                    await Task.WhenAll(tasks);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainChatWindow.LoadSearchUserAvatarsAsync] Ошибка: {ex.Message}");
            }
        }

        // Вспомогательный метод для поиска визуального потомка
        private T? FindVisualChild<T>(Avalonia.Visual visual) where T : Avalonia.Visual
        {
            if (visual is T child)
                return child;

            var visualChildren = Avalonia.VisualTree.VisualExtensions.GetVisualChildren(visual);
            foreach (var v in visualChildren)
            {
                var result = FindVisualChild<T>(v);
                if (result != null)
                    return result;
            }
            return null;
        }

        private async Task OnChatListPollingAsync()
        {
            try
            {
                if (await _chatService.PollForChatListChangesAsync())
                {
                    Console.WriteLine("[OnChatListPollingAsync] Обнаружено изменение в списке чатов");
                    var chats = await _chatService.GetUserChatsAsync();
                    await Dispatcher.UIThread.InvokeAsync(() => UpdateChatsList(chats));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OnChatListPollingAsync] Ошибка: {ex.Message}");
            }
        }

        private async void OnFriendsTabButton2Clicked(object? sender, RoutedEventArgs e)
        {
            Console.WriteLine("[OnFriendsTabButton2Clicked] Выбрана вкладка друзей");
            FriendsTabContent.IsVisible = true;
            RequestsTabContent.IsVisible = false;
            
            if (FriendsTabButton2.Classes.Contains("outlined"))
                FriendsTabButton2.Classes.Remove("outlined");
            if (!FriendsTabButton2.Classes.Contains("secondary"))
                FriendsTabButton2.Classes.Add("secondary");

            if (!RequestsTabButton.Classes.Contains("outlined"))
                RequestsTabButton.Classes.Add("outlined");
            RequestsTabButton.Classes.Remove("secondary");

            await LoadFriendsAsync();
        }

        private async void OnRequestsTabButtonClicked(object? sender, RoutedEventArgs e)
        {
            Console.WriteLine("[OnRequestsTabButtonClicked] Выбрана вкладка запросов");
            FriendsTabContent.IsVisible = false;
            RequestsTabContent.IsVisible = true;

            if (!FriendsTabButton2.Classes.Contains("outlined"))
                FriendsTabButton2.Classes.Add("outlined");
            FriendsTabButton2.Classes.Remove("secondary");

            if (RequestsTabButton.Classes.Contains("outlined"))
                RequestsTabButton.Classes.Remove("outlined");
            if (!RequestsTabButton.Classes.Contains("secondary"))
                RequestsTabButton.Classes.Add("secondary");

            await LoadFriendRequestsAsync();
        }

        private async Task LoadFriendsAsync()
        {
            Console.WriteLine($"[LoadFriendsAsync] Загрузка списка друзей для пользователя {_currentUserId}");
            var friends = await _friendService.GetFriendsAsync(_currentUserId);

            if (friends != null && friends.Count > 0)
            {
                var friendItems = friends.Select(f => new FriendListItemDto 
                { 
                    Id = (uint)f.Id, 
                    Username = f.Username,
                    AvatarUrl = f.AvatarUrl
                }).ToList();

                FriendsListBox.ItemsSource = friendItems;
                Console.WriteLine($"[LoadFriendsAsync] Загружено {friendItems.Count} друзей");

                AttachFriendsListHandlers();

                // Загружаем аватарки в фоне
                _ = LoadFriendsAvatarsAsync(friendItems);
            }
            else
            {
                FriendsListBox.ItemsSource = new List<FriendListItemDto>();
                Console.WriteLine("[LoadFriendsAsync] Список друзей пуст");
            }
        }

        private async Task LoadFriendRequestsAsync()
        {
            Console.WriteLine($"[LoadFriendRequestsAsync] Загрузка входящих и исходящих запросов для пользователя {_currentUserId}");

            var incoming = await _friendService.GetIncomingFriendRequestsAsync(_currentUserId);
            var outgoing = await _friendService.GetOutgoingFriendRequestsAsync(_currentUserId);

            // Загружаем информацию о пользователях для входящих запросов
            var incomingItems = new List<FriendListItemDto>();
            if (incoming != null && incoming.Count > 0)
            {
                foreach (var f in incoming)
                {
                    var userInfo = await _userSearchService.GetUserByIdAsync((uint)f.Id);
                    incomingItems.Add(new FriendListItemDto 
                    { 
                        Id = (uint)f.Id, 
                        Username = f.Username,
                        AvatarUrl = userInfo?.AvatarUrl
                    });
                }
            }

            // Загружаем информацию о пользователях для исходящих запросов
            var outgoingItems = new List<FriendListItemDto>();
            if (outgoing != null && outgoing.Count > 0)
            {
                foreach (var f in outgoing)
                {
                    var userInfo = await _userSearchService.GetUserByIdAsync((uint)f.Id);
                    outgoingItems.Add(new FriendListItemDto 
                    { 
                        Id = (uint)f.Id, 
                        Username = f.Username,
                        AvatarUrl = userInfo?.AvatarUrl
                    });
                }
            }

            IncomingRequestsListBox.ItemsSource = incomingItems;
            OutgoingRequestsListBox.ItemsSource = outgoingItems;

            Console.WriteLine($"[LoadFriendRequestsAsync] Входящих: {incomingItems.Count}, Исходящих: {outgoingItems.Count}");

            AttachRequestsListHandlers();

            // Загружаем аватарки входящих запросов
            if (incomingItems.Count > 0)
                _ = LoadFriendsAvatarsAsync(incomingItems);

            // Загружаем аватарки исходящих запросов
            if (outgoingItems.Count > 0)
                _ = LoadFriendsAvatarsAsync(outgoingItems);
        }

        private async Task LoadFriendsAvatarsAsync(List<FriendListItemDto> friends)
        {
            try
            {
                Console.WriteLine($"[LoadFriendsAvatarsAsync] Загружаем аватарки для {friends.Count} друзей");
                using (var httpClient = new System.Net.Http.HttpClient())
                {
                    var tasks = friends.Select(async friend =>
                    {
                        if (string.IsNullOrEmpty(friend.AvatarUrl)) return;
                        var imageData = await httpClient.GetByteArrayAsync(friend.AvatarUrl);
                        var bitmap = new Avalonia.Media.Imaging.Bitmap(new System.IO.MemoryStream(imageData));
                        await Dispatcher.UIThread.InvokeAsync(() => friend.AvatarBitmap = bitmap);
                    });
                    await Task.WhenAll(tasks);
                }
                Console.WriteLine($"[LoadFriendsAvatarsAsync] Загрузка аватарок завершена");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LoadFriendsAvatarsAsync] Ошибка: {ex.Message}");
            }
        }

        private void AttachFriendsListHandlers()
        {
            Console.WriteLine("[AttachFriendsListHandlers] Прикрепление обработчиков для списка друзей");
            
            // Обработчик для ЛКМ на друзьях
            Border.PointerPressedEvent.AddClassHandler<Border>(OnFriendBorderPointerPressed, handledEventsToo: true);
            
            // Обработчик для контекстного меню (ПКМ)
            MenuItem.ClickEvent.AddClassHandler<MenuItem>(OnFriendContextMenuItemClicked, handledEventsToo: true);
        }

        private async void OnFriendContextMenuItemClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem) return;

            var headerText = menuItem.Header?.ToString() ?? "";
            
            // Проверяем что это пункт меню "Удалить из друзей"
            if (headerText != "Удалить из друзей") return;
            
            // Получаем DataContext из родительского элемента (Border)
            if (menuItem.Parent is ContextMenu contextMenu && contextMenu.PlacementTarget is Border border)
            {
                if (border.DataContext is FriendListItemDto friend)
                {
                    Console.WriteLine($"[OnFriendContextMenuItemClicked] Удаление друга {friend.Username} (ID: {friend.Id})");
                    await OnRemoveFriendClicked(friend.Id);
                    e.Handled = true;
                }
            }
        }

        private async void OnFriendBorderPointerPressed(object? sender, PointerPressedEventArgs e)
        {

            if (sender is not Border border) return;
            
            if (border.DataContext is not FriendListItemDto friend) return;

            // Проверяем это Border из списка друзей (имеет имя FriendBorder)
            if (border.Name != "FriendBorder") return;

            var point = e.GetCurrentPoint(border);
            
            // Проверяем тип клика
            if (point.Properties.IsLeftButtonPressed)
            {
                // ЛКМ - открыть чат
                Console.WriteLine($"[OnFriendBorderPointerPressed] ЛКМ на друге: {friend.Username} (ID: {friend.Id})");
                
                // Сохраняем имя чата
                _currentChatUserName = friend.Username;
                ChatUserName.Text = friend.Username;

                // Проверяем есть ли уже чат с этим пользователем
                var existingChat = _pendingChats.Values.FirstOrDefault(c => c.OtherUserId == friend.Id && c.IsPending);
                
                if (existingChat != null)
                {
                    // Временный чат уже есть
                    Console.WriteLine($"[OnFriendBorderPointerPressed] Найден существующий временный чат с {friend.Username}");
                    _currentChatId = existingChat.Id;
                }
                else
                {
                    // Ищем реальный чат на сервере
                    var chats = await _chatService.GetUserChatsAsync();
                    var realChat = chats?.FirstOrDefault(c => c.OtherUserId == friend.Id);
                    
                    if (realChat != null)
                    {
                        // Чат существует на сервере
                        Console.WriteLine($"[OnFriendBorderPointerPressed] Найден существующий чат на сервере с {friend.Username}. ID: {realChat.Id}");
                        _currentChatId = realChat.Id;
                    }
                    else
                    {
                        // Создаем новый временный чат
                        var tempChatId = (uint)(Guid.NewGuid().GetHashCode() & 0x7FFFFFFF);
                        var pendingChat = new ChatDto
                        {
                            Id = tempChatId,
                            Name = friend.Username,
                            LastMessage = "Начните общение",
                            LastMessageTime = DateTime.Now,
                            OtherUserId = friend.Id,
                            IsPending = true
                        };
                        
                        _pendingChats[tempChatId] = pendingChat;
                        _currentChatId = pendingChat.Id;
                        Console.WriteLine($"[OnFriendBorderPointerPressed] Создан новый временный чат. ID: {tempChatId}");
                    }
                }

                // Переключаемся на панель чатов
                ChatPanel.IsVisible = true;
                GlobalSearchPanel.IsVisible = false;
                FriendsPanel.IsVisible = false;

                // Загружаем аватарку собеседника в шапку
                _ = LoadChatUserAvatarAsync(friend.Id);

                // Загружаем сообщения для текущего чата (если это реальный чат)
                if (!_pendingChats.ContainsKey(_currentChatId.Value))
                {
                    var messages = await _chatService.LoadChatHistoryAsync(_currentChatId.Value);
                    await UpdateMessagesListAsync(messages);
                    
                    var otherUserId = await _chatService.GetOtherUserIdForChat(_currentChatId.Value);
                    if (otherUserId > 0)
                    {
                        Console.WriteLine($"[OnFriendBorderPointerPressed] Открываем WebSocket для чата {_currentChatId}");
                        await _webSocketService.OpenAsync(_currentChatId.Value, _currentUserId, otherUserId);
                    }
                }
                else
                {
                    // Это временный чат - очищаем сообщения
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        MessagesListBox.ItemsSource = new List<MessageListItem>();
                    });
                }

                e.Handled = true;
            }
            else if (point.Properties.IsRightButtonPressed)
            {
                // ПКМ - удалить друга (вызываем напрямую, контекстное меню показывается после)
                Console.WriteLine($"[OnFriendBorderPointerPressed] ПКМ на друге: {friend.Username} (ID: {friend.Id}), ожидаем выбор в контекстном меню");
                // Не блокируем событие - пусть контекстное меню показывается
                // Обработчик для выбора пункта меню будет добавлен через AttachFriendsListHandlers
            }
        }

        private void AttachRequestsListHandlers()
        {
            Console.WriteLine("[AttachRequestsListHandlers] Прикрепление обработчиков для списка запросов");
        }

        private async void OnFriendSelected(object? sender, RoutedEventArgs e)
        {
            if (FriendsListBox.SelectedItem is FriendListItemDto friend)
            {
                Console.WriteLine($"[OnFriendSelected] Двойной клик на друга: {friend.Username}");
                
                FriendsPanel.IsVisible = false;
                ChatPanel.IsVisible = true;

                _currentChatUserName = friend.Username;
                ChatUserName.Text = friend.Username;
                
                var tempChatId = (uint)(Guid.NewGuid().GetHashCode() & 0x7FFFFFFF);
                var pendingChat = new ChatDto
                {
                    Id = tempChatId,
                    Name = friend.Username,
                    LastMessage = "Продолжите общение",
                    LastMessageTime = DateTime.Now,
                    OtherUserId = friend.Id,
                    IsPending = true
                };

                _pendingChats[tempChatId] = pendingChat;
                _currentChatId = pendingChat.Id;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    MessagesListBox.ItemsSource = new List<MessageListItem>();
                });

                var chats = await _chatService.GetUserChatsAsync();
                UpdateChatsList(chats);
            }
        }

        private async void OnRequestButtonClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button) return;

            if (button.Tag is uint userId)
            {
                var content = button.Content?.ToString() ?? "";
                Console.WriteLine($"[OnRequestButtonClicked] Кнопка: {content}, UserID: {userId}");

                switch (content)
                {
                    case "Принять":
                        await OnAcceptIncomingClicked(userId);
                        break;
                    case "Отклонить":
                        await OnRejectIncomingClicked(userId);
                        break;
                    case "Отменить":
                        await OnCancelOutgoingClicked(userId);
                        break;
                }
            }
        }

        private async void OnRemoveFriendClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button) return;

            if (button.Tag is uint friendId)
            {
                Console.WriteLine($"[OnRemoveFriendClicked] Удаление друга {friendId}");
                await OnRemoveFriendAsync(friendId);
            }
        }

        private async Task OnRemoveFriendAsync(uint friendId)
        {
            Console.WriteLine($"[OnRemoveFriendAsync] Удаление друга {friendId}");
            
            bool success = await _friendService.RemoveFriendAsync(_currentUserId, friendId);
            if (success)
            {
                Console.WriteLine($"[OnRemoveFriendAsync] Друг успешно удален");
                await LoadFriendsAsync();
            }
            else
            {
                Console.WriteLine($"[OnRemoveFriendAsync] Ошибка при удалении друга");
            }
        }

        private async Task OnAcceptIncomingClicked(uint friendId)
        {
            Console.WriteLine($"[OnAcceptIncomingClicked] Принятие входящего запроса от {friendId}");
            
            bool success = await _friendService.AcceptFriendRequestAsync(_currentUserId, friendId);
            if (success)
            {
                Console.WriteLine($"[OnAcceptIncomingClicked] Входящий запрос принят");
                await LoadFriendRequestsAsync();
            }
            else
            {
                Console.WriteLine($"[OnAcceptIncomingClicked] Ошибка при принятии входящего запроса");
            }
        }

        private async Task OnRejectIncomingClicked(uint friendId)
        {
            Console.WriteLine($"[OnRejectIncomingClicked] Отклонение входящего запроса от {friendId}");
            
            bool success = await _friendService.RejectFriendRequestAsync(_currentUserId, friendId);
            if (success)
            {
                Console.WriteLine($"[OnRejectIncomingClicked] Входящий запрос отклонен");
                await LoadFriendRequestsAsync();
            }
            else
            {
                Console.WriteLine($"[OnRejectIncomingClicked] Ошибка при отклонении входящего запроса");
            }
        }

        private async Task OnCancelOutgoingClicked(uint targetUserId)
        {
            Console.WriteLine($"[OnCancelOutgoingClicked] Отмена исходящего запроса для {targetUserId}");
            
            bool success = await _friendService.RejectFriendRequestAsync(_currentUserId, targetUserId);
            if (success)
            {
                Console.WriteLine($"[OnCancelOutgoingClicked] Исходящий запрос отменен");
                await LoadFriendRequestsAsync();
            }
            else
            {
                Console.WriteLine($"[OnCancelOutgoingClicked] Ошибка при отмене исходящего запроса");
            }
        }

        private async Task OnRemoveFriendClicked(uint friendId)
        {
            Console.WriteLine($"[OnRemoveFriendClicked] Удаление друга {friendId}");
            
            bool success = await _friendService.RemoveFriendAsync(_currentUserId, friendId);
            if (success)
            {
                Console.WriteLine($"[OnRemoveFriendClicked] Друг удален");
                await LoadFriendsAsync();
            }
            else
            {
                Console.WriteLine($"[OnRemoveFriendClicked] Ошибка при удалении друга");
            }
        }

        private void AttachSearchResultButtonHandlers()
        {
            Console.WriteLine("[AttachSearchResultButtonHandlers] Прикрепление обработчиков кнопок");
        }

        private async void OnSearchResultButtonClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button) return;

            if (button.Tag is uint userId)
            {
                var content = button.Content?.ToString() ?? "";
                Console.WriteLine($"[OnSearchResultButtonClicked] Кнопка: {content}, UserID: {userId}");

                switch (content)
                {
                    case "Добавить в друзья":
                        await OnAddFriendClicked(userId);
                        break;
                    case "Принять":
                        await OnAcceptRequestClicked(userId);
                        break;
                    case "Отклонить":
                        await OnRejectRequestClicked(userId);
                        break;
                    case "Отменить запрос":
                        await OnCancelRequestClicked(userId);
                        break;
                }
            }
        }
    }

    // ───────────────────────────── Сигнализация и звонки ─────────────────────────────

    partial class MainChatWindow
    {
        // Подключается к сигнальной комнате пассивно (для приёма входящих звонков)
        private async Task ConnectToSignalingRoomAsync(uint chatId)
        {
            try
            {
                var roomId = $"chat-{chatId}";
                Console.WriteLine($"[ConnectToSignalingRoomAsync] Подключаемся к комнате {roomId}");
                await _signalingService.ConnectAsync(roomId, _currentUserId.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConnectToSignalingRoomAsync] Ошибка: {ex.Message}");
            }
        }

        // Принять входящий звонок
        private async void OnAcceptCallClicked(object? sender, RoutedEventArgs e)
        {
            Console.WriteLine("[OnAcceptCallClicked] Принимаем звонок");
            IncomingCallOverlay.IsVisible = false;
            UpdateCallUI(true);
            CallStatusText.Text = $"📞 Звонок: {_currentChatUserName}";
            await _voiceCallService.HandleOfferAsync(_pendingOfferSdp, _selectedMicIndex, _selectedSpeakerIndex);
            _pendingOfferSdp = string.Empty;
        }

        // Отклонить входящий звонок
        private async void OnRejectCallClicked(object? sender, RoutedEventArgs e)
        {
            Console.WriteLine("[OnRejectCallClicked] Отклоняем звонок");
            IncomingCallOverlay.IsVisible = false;
            _pendingOfferSdp = string.Empty;
            await _signalingService.LeaveAsync();
            _signalingService.Disconnect();
            // Переподключаемся пассивно для приёма новых входящих звонков
            if (_currentChatId.HasValue && !_pendingChats.ContainsKey(_currentChatId.Value))
                _ = ConnectToSignalingRoomAsync(_currentChatId.Value);
        }

        // Завершить активный звонок
        private async void OnEndCallClicked(object? sender, RoutedEventArgs e)
        {
            await EndCallAsync();
        }

        // Общая логика завершения звонка
        private async Task EndCallAsync()
        {
            Console.WriteLine("[EndCallAsync] Завершаем звонок");
            await _signalingService.LeaveAsync();
            // Отключаемся сразу, чтобы не получать эхо-ответные leave и не создавать бесконечный цикл
            _signalingService.Disconnect();
            await _voiceCallService.HangUpAsync();
            UpdateCallUI(false);
            // Переподключаемся пассивно для приёма новых входящих звонков
            if (_currentChatId.HasValue && !_pendingChats.ContainsKey(_currentChatId.Value))
                _ = ConnectToSignalingRoomAsync(_currentChatId.Value);
        }

        // Обновляет видимость панели звонка и иконку кнопки
        private void UpdateCallUI(bool callActive)
        {
            Dispatcher.UIThread.Post(() =>
            {
                ActiveCallBar.IsVisible = callActive;
                CallButton.Content = callActive ? "📵" : "📞";
            });
        }

        // ───── Обработчики событий сигнального сервиса ─────

        // Получен входящий offer — показываем оверлей
        private void OnSignalingOfferReceived(object? sender, string sdp)
        {
            Console.WriteLine("[OnSignalingOfferReceived] Входящий звонок");
            _pendingOfferSdp = sdp;
            Dispatcher.UIThread.Post(() =>
            {
                IncomingCallUserName.Text = _currentChatUserName;
                IncomingCallOverlay.IsVisible = true;
            });
        }

        // Получен answer — передаём в VoiceCallService
        private async void OnSignalingAnswerReceived(object? sender, string sdp)
        {
            Console.WriteLine("[OnSignalingAnswerReceived] Получен answer");
            await _voiceCallService.HandleAnswerAsync(sdp);
        }

        // Получен ICE-кандидат — передаём в VoiceCallService
        private async void OnSignalingIceCandidateReceived(object? sender, SignalingCandidateEventArgs e)
        {
            await _voiceCallService.HandleIceCandidateAsync(e.Candidate, e.SdpMid, e.SdpMLineIndex);
        }

        // Собеседник покинул комнату — завершаем звонок
        private async void OnSignalingPeerLeft(object? sender, EventArgs e)
        {
            Console.WriteLine("[OnSignalingPeerLeft] Собеседник покинул комнату");
            if (!_voiceCallService.IsCallActive)
            {
                Console.WriteLine("[OnSignalingPeerLeft] Звонок не активен, пропуск");
                return;
            }
            await EndCallAsync();
        }

        // ───── Обработчики событий VoiceCallService ─────

        // Offer создан — отправляем через сигналинг
        private async void OnVoiceCallOfferCreated(object? sender, string sdp)
        {
            Console.WriteLine("[OnVoiceCallOfferCreated] Отправляем offer");
            await _signalingService.SendOfferAsync(sdp);
        }

        // Answer создан — отправляем через сигналинг
        private async void OnVoiceCallAnswerCreated(object? sender, string sdp)
        {
            Console.WriteLine("[OnVoiceCallAnswerCreated] Отправляем answer");
            await _signalingService.SendAnswerAsync(sdp);
        }

        // ICE-кандидат готов — отправляем через сигналинг
        private async void OnVoiceCallIceCandidateReady(object? sender, SignalingCandidateEventArgs e)
        {
            await _signalingService.SendIceCandidateAsync(e.Candidate, e.SdpMid, e.SdpMLineIndex);
        }

        // Звонок установлен — обновляем статус
        private void OnVoiceCallConnected(object? sender, EventArgs e)
        {
            Console.WriteLine("[OnVoiceCallConnected] Звонок установлен");
            Dispatcher.UIThread.Post(() => CallStatusText.Text = $"📞 {_currentChatUserName}");
        }

        // Звонок завершён со стороны сервиса — обновляем UI
        private async void OnVoiceCallEnded(object? sender, EventArgs e)
        {
            Console.WriteLine("[OnVoiceCallEnded] Звонок завершён");
            await Dispatcher.UIThread.InvokeAsync(() => UpdateCallUI(false));
        }

        // Аудио через WebSocket-ретрансляцию: отправка
        private async void OnVoiceRelayAudioReady(object? sender, byte[] encoded)
        {
            await _signalingService.SendAudioAsync(encoded);
        }

        // Аудио через WebSocket-ретрансляцию: приём
        private void OnSignalingRelayAudioReceived(object? sender, byte[] data)
        {
            _voiceCallService.ReceiveRelayAudio(data);
        }
    }
}