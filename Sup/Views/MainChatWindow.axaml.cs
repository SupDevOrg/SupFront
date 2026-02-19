using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Sup.ForTokens;
using Sup.Models;
using Sup.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sup.Views
{
    public partial class MainChatWindow : Window
    {
        private readonly IChatService _chatService;
        private readonly IUserSearchService _userSearchService;
        private readonly IVoiceTestService _voiceTestService;
        private readonly IWebSocketService _webSocketService;

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

        public MainChatWindow() : this("user")
        {
        }

        public MainChatWindow(string username)
        {
            InitializeComponent();

            try
            {
                _chatService = new ChatService();
                _userSearchService = new UserSearchService();
                _voiceTestService = new VoiceTestService();
                _webSocketService = new WebSocketService();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainChatWindow] Ошибка инициализации сервисов: {ex.Message}");
            }

            _currentUsername = username;
            UserNameLabel.Text = username;

            SetupEventHandlers();
            Console.WriteLine($"[MainChatWindow] Инициализация для пользователя: {username}");
            
            // Инициализируем в фоне
            _ = InitializeAsync();

            _chatListTimer = new System.Timers.Timer(1000);
            _chatListTimer.Elapsed += async (s, e) => await OnChatListPollingAsync();
            _chatListTimer.AutoReset = true;
            _chatListTimer.Start();
            Console.WriteLine("[MainChatWindow] Таймер полинга чатов запущен");
        }

        private void SetupEventHandlers()
        {
            SearchUsersButton.Click += OnSearchUsersClicked;
            BackToChatButton.Click += OnBackToChatClicked;
            SearchGlobalTextBox.KeyUp += async (s, e) => await OnSearchUsersAsync();
            GlobalUsersListBox.DoubleTapped += OnGlobalUserSelected;
            SettingsButton.Click += OnSettingsClicked;
            VoiceSettingsButton.Click += OnVoiceSettingsClicked;
            BackFromSettingsButton.Click += OnBackFromSettingsClicked;
            TestVoiceButton.Click += OnTestVoiceClicked;
            PrevPageButton.Click += OnPrevPageClick;
            NextPageButton.Click += OnNextPageClick;
            SendMessageButton.Click += OnSendMessageClicked;
            UsersListBox.SelectionChanged += OnChatSelected;

            _webSocketService.OnMessageReceived += OnWebSocketMessageReceived;
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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[InitializeAsync] Неожиданная ошибка: {ex.Message}");
            }
        }

        private void UpdateChatsList(List<ChatDto> chats)
        {
            try
            {
                var validChats = chats.Where(c => c != null && c.Id > 0).ToList();
                
                var pendingItems = _pendingChats.Values
                    .Where(c => c.IsPending)
                    .ToList();

                var items = validChats.Select(c => new ChatListItem
                {
                    ChatId = c.Id,
                    DisplayName = c.Name,
                    LastMessage = c.LastMessage,
                    LastMessageTime = c.LastMessageTime.ToString("HH:mm")
                }).ToList();

                var pendingChatItems = pendingItems.Select(c => new ChatListItem
                {
                    ChatId = c.Id,
                    DisplayName = c.Name,
                    LastMessage = c.LastMessage,
                    LastMessageTime = c.LastMessageTime.ToString("HH:mm")
                }).ToList();

                var allItems = items.Concat(pendingChatItems).ToList();
                UsersListBox.ItemsSource = allItems;
                Console.WriteLine($"[UpdateChatsList] Список обновлен: {validChats.Count} реальных чатов + {pendingItems.Count} временных");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateChatsList] Ошибка: {ex.Message}");
            }
        }

        private async void OnChatSelected(object? sender, SelectionChangedEventArgs e)
        {
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
                    }
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
                    string senderName = msg.SenderId == _currentUserId ? "Вы" : $"Пользователь {msg.SenderId}";
                    items.Add(new MessageListItem
                    {
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
                await _webSocketService.SendAsync(message);
                Console.WriteLine("[OnSendMessageClicked] Сообщение отправлено через WebSocket");
                ScrollToLatest();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OnSendMessageClicked] Ошибка: {ex.Message}");
            }
        }

        private async void OnWebSocketMessageReceived(object? sender, WebSocketMessageEventArgs e)
        {
            if (e.ChatId != _currentChatId)
                return;

            Console.WriteLine($"[OnWebSocketMessageReceived] Сообщение в чат {e.ChatId}: {e.Content.Substring(0, Math.Min(50, e.Content.Length))}");

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var messages = (MessagesListBox.ItemsSource as List<MessageListItem> ?? new()).ToList();
                messages.Add(new MessageListItem
                {
                    Content = e.Content,
                    IsOwnMessage = e.SenderUId == _currentUserId,
                    Time = e.CreatedAt.ToString("HH:mm"),
                    SenderName = e.SenderUId == _currentUserId ? "Вы" : $"Пользователь {e.SenderUId}"
                });
                MessagesListBox.ItemsSource = messages;
                ScrollToLatest();
            });
        }

        private async Task PerformSearchAsync(int page = 0)
        {
            var query = SearchGlobalTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                Console.WriteLine("[PerformSearchAsync] Запрос пуст");
                GlobalUsersListBox.ItemsSource = new List<string>();
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
                    var usernames = response.Users?.Select(u => u.Username).ToList() ?? new();
                    GlobalUsersListBox.ItemsSource = usernames;
                    Console.WriteLine($"[PerformSearchAsync] Найдено {usernames.Count} пользователей on page {page + 1}/{_totalPages}");
                }
                else
                {
                    Console.WriteLine("[PerformSearchAsync] Ошибка запроса");
                    ResetPagination();
                    GlobalUsersListBox.ItemsSource = new List<string>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PerformSearchAsync] Ошибка: {ex.Message}");
                GlobalUsersListBox.ItemsSource = new List<string> { "Ошибка сети" };
                ResetPagination();
            }
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
            GlobalSearchPanel.IsVisible = true;
            ChatPanel.IsVisible = false;
        }

        private void OnBackToChatClicked(object? sender, RoutedEventArgs e)
        {
            Console.WriteLine("[OnBackToChatClicked] Возврат к чатам");
            GlobalSearchPanel.IsVisible = false;
            ChatPanel.IsVisible = true;
        }

        private async void OnGlobalUserSelected(object? sender, RoutedEventArgs e)
        {
            if (GlobalUsersListBox.SelectedItem is string userName)
            {
                Console.WriteLine($"[OnGlobalUserSelected] Выбран пользователь: {userName}");

                var userId = await _userSearchService.GetUserIdByNameAsync(userName);
                if (!userId.HasValue)
                {
                    Console.WriteLine($"[OnGlobalUserSelected] Не найден ID для {userName}");
                    return;
                }

                Console.WriteLine($"[OnGlobalUserSelected] Найден ID: {userId}");

                GlobalSearchPanel.IsVisible = false;
                ChatPanel.IsVisible = true;

                _currentChatUserName = userName;
                ChatUserName.Text = userName;
                
                var tempChatId = (uint)(Guid.NewGuid().GetHashCode() & 0x7FFFFFFF);
                var pendingChat = new ChatDto
                {
                    Id = tempChatId,
                    Name = userName,
                    LastMessage = "Начните общение",
                    LastMessageTime = DateTime.Now,
                    OtherUserId = userId.Value,
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
            LoadAudioDevices();
        }

        private void OnBackFromSettingsClicked(object? sender, RoutedEventArgs e)
        {
            Console.WriteLine("[OnBackFromSettingsClicked] Закрыты настройки");
            SettingsPanel.IsVisible = false;
            VoicePanel.IsVisible = false;
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
    }
}
