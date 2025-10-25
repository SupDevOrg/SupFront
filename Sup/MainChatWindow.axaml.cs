using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sup
{
    public partial class MainChatWindow : Window
    {
        private HttpClient _httpClient = new HttpClient();
        public MainChatWindow()
        {
            InitializeComponent();
            // События для кнопок
            SearchUsersButton.Click += OnSearchUsersClicked;
            BackToChatButton.Click += OnBackToChatClicked;
            SearchGlobalTextBox.KeyUp += async (s, e) => await OnSearchGlobalChangedAsync();
            GlobalUsersListBox.DoubleTapped += OnGlobalUserSelected;
        }

        private void OnSearchUsersClicked(object? sender, RoutedEventArgs e)
        {
            // Показать панель поиска, скрыть чат
            GlobalSearchPanel.IsVisible = true;
            ChatPanel.IsVisible = false;
        }

        private void OnBackToChatClicked(object? sender, RoutedEventArgs e)
        {
            // Скрыть глобальный поиск, показать чат
            GlobalSearchPanel.IsVisible = false;
            ChatPanel.IsVisible = true;
        }

        // Реальный запрос к серверу поиска пользователей
        private async Task OnSearchGlobalChangedAsync()
        {
            var query = SearchGlobalTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                GlobalUsersListBox.ItemsSource = new List<string>();
                return;
            }
            try
            {
                var url = $"http://localhost:8081/api/v1/user/{Uri.EscapeDataString(query)}";
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    GlobalUsersListBox.ItemsSource = new List<string> { "Ошибка поиска :(" };
                    return;
                }
                using var stream = await response.Content.ReadAsStreamAsync();
                var users = await JsonSerializer.DeserializeAsync<List<UserDto>>(stream);
                GlobalUsersListBox.ItemsSource = users != null ? users.ConvertAll(u => u.username) : new List<string>();
            }
            catch
            {
                GlobalUsersListBox.ItemsSource = new List<string> { "Ошибка сети" };
            }
        }

        private void OnGlobalUserSelected(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (GlobalUsersListBox.SelectedItem is string user)
            {
                ChatUserName.Text = user;
                GlobalSearchPanel.IsVisible = false;
                ChatPanel.IsVisible = true;
            }
        }
    }

    public class UserDto
    {
        public int id { get; set; }
        public string username { get; set; }
    }
}
