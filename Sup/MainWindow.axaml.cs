using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Avalonia.Media;
using System.Text.Json.Serialization;

namespace Sup
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // Навешиваем обработчики событий на кнопки
            LoginButton.Click += OnLoginClicked;
            RegisterButton.Click += OnRegisterClicked;
        }

        // Обработчик для кнопки "Войти"
        // Обработчик для кнопки "Войти"
        private async void OnLoginClicked(object? sender, RoutedEventArgs e)
        {
            var login = LoginTextBox.Text;
            var password = PasswordTextBox.Text;

            // Очищаем сообщение
            StatusMessage.Text = string.Empty;
            StatusMessage.Foreground = Brushes.Transparent;

            // Проверка полей
            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                StatusMessage.Text = "Введите логин и пароль";
                StatusMessage.Foreground = Brushes.Red;
                return;
            }

            // Создаём http клиент
            using var client = new HttpClient();
            try
            {
                // URL вашего backend API
                var url = $"{App.ApiBaseUrl}user/login";
                // Формируем json-запрос
                var payload = new { username = login, password = password };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    // Читаем ответ и получаем токены
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var authResponse = JsonSerializer.Deserialize<AuthResponse>(responseContent);

                    if (authResponse != null && !string.IsNullOrEmpty(authResponse.accessToken))
                    {
                        // СОХРАНЯЕМ ТОКЕНЫ В КЭШ - ключевой момент!
                        await TokenManager.SaveTokensAsync(authResponse.accessToken, authResponse.refreshToken);

                        StatusMessage.Text = "Успешный вход!";
                        StatusMessage.Foreground = Brushes.Green;
                        var chatWindow = new MainChatWindow();
                        chatWindow.Show();
                        this.Close();
                    }
                    else
                    {
                        StatusMessage.Text = "Ошибка: не удалось получить токен";
                        StatusMessage.Foreground = Brushes.Red;
                    }
                }
                else
                {
                    // Ошибка авторизации, читаем сообщение сервера
                    var errorMsg = await response.Content.ReadAsStringAsync();
                    StatusMessage.Text = $"Ошибка: {errorMsg}";
                    StatusMessage.Foreground = Brushes.Red;
                }
            }
            catch (Exception ex)
            {
                StatusMessage.Text = $"Ошибка соединения: {ex.Message}";
                StatusMessage.Foreground = Brushes.Red;
            }
        }

        // Обработчик для кнопки "Зарегистрироваться"
        private async void OnRegisterClicked(object? sender, RoutedEventArgs e)
        {
            var login = LoginTextBox.Text;
            var password = PasswordTextBox.Text;

            StatusMessage.Text = string.Empty;
            StatusMessage.Foreground = Brushes.Transparent;

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                StatusMessage.Text = "Введите логин и пароль";
                StatusMessage.Foreground = Brushes.Red;
                return;
            }

            using var client = new HttpClient();
            try
            {
                var url = $"{App.ApiBaseUrl}user/register";
                var payload = new { username = login, password = password };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    StatusMessage.Text = "Успешная регистрация!";
                    StatusMessage.Foreground = Brushes.Green;
                }
                else
                {
                    var errorMsg = await response.Content.ReadAsStringAsync();
                    StatusMessage.Text = $"Ошибка: {errorMsg}";
                    StatusMessage.Foreground = Brushes.Red;
                }
            }
            catch (Exception ex)
            {
                StatusMessage.Text = $"Ошибка соединения: {ex.Message}";
                StatusMessage.Foreground = Brushes.Red;
            }
        }
    }

    public class AuthResponse
    {
        [JsonPropertyName("accessToken")]
        public string accessToken { get; set; } = string.Empty;
        [JsonPropertyName("refreshToken")]
        public string refreshToken { get; set; } = string.Empty;
    }
}