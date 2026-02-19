using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Avalonia.Media;
using System.Text.Json.Serialization;
using Sup.ForTokens;
using Sup.Views;
using System.Threading.Tasks;

namespace Sup
{
    public partial class MainWindow : Window
    {
        private enum AppMode { Login, Register, EmailVerification }
        private AppMode _currentMode = AppMode.Login;

        private string _pendingUsername = string.Empty;
        private string _pendingPassword = string.Empty;
        private string _pendingEmail = string.Empty;
        private string _pendingAccessToken = string.Empty;

        public MainWindow()
        {
            InitializeComponent();
            // Навешиваем обработчики событий на кнопки
            LoginButton.Click += OnLoginClicked;
            RegisterButton.Click += OnRegisterClicked;
            VerifyEmailButton.Click += OnVerifyEmailClicked;
            ResendCodeButton.Click += OnResendCodeClicked;
            SwitchToRegisterButton.Click += OnSwitchToRegisterClicked;
            SwitchToLoginButton.Click += OnSwitchToLoginClicked;

            // Устанавливаем начальный режим
            SetAppMode(AppMode.Login);
        }

        // Переключение между режимами
        private void SetAppMode(AppMode mode)
        {
            _currentMode = mode;

            // Сбрасываем видимость всех элементов
            EmailField.IsVisible = false;
            EmailVerificationPanel.IsVisible = false;
            LoginButtons.IsVisible = false;
            RegisterButtons.IsVisible = false;
            VerifyButtons.IsVisible = false;

            switch (mode)
            {
                case AppMode.Login:
                    TitleText.Text = "Вход в Sup";
                    SubtitleText.Text = "Введите данные аккаунта, чтобы продолжить";
                    LoginButtons.IsVisible = true;
                    break;

                case AppMode.Register:
                    TitleText.Text = "Регистрация в Sup";
                    SubtitleText.Text = "Создайте новый аккаунт";
                    EmailField.IsVisible = true;
                    RegisterButtons.IsVisible = true;
                    break;

                case AppMode.EmailVerification:
                    TitleText.Text = "Подтверждение email";
                    SubtitleText.Text = "Введите код из письма";
                    EmailVerificationPanel.IsVisible = true;
                    VerifyButtons.IsVisible = true;
                    break;
            }

            StatusMessage.Text = string.Empty;
        }

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

            try
            {
                // Отключаем кнопку входа
                LoginButton.IsEnabled = false;
                StatusMessage.Text = "Подключение...";
                StatusMessage.Foreground = Brushes.Blue;

                // Создаём http клиент
                using var client = new HttpClient();
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
                        // СОХРАНЯЕМ ТОКЕНЫ В КЭШ 
                        await TokenManager.SaveTokensAsync(authResponse.accessToken, authResponse.refreshToken);

                        StatusMessage.Text = "Успешный вход, загружаем чаты...";
                        StatusMessage.Foreground = Brushes.Green;
                        
                        Console.WriteLine($"[OnLoginClicked] Успешный вход для пользователя: {login}");
                        
                        var chatWindow = new MainChatWindow(login);
                        chatWindow.Show();
                        this.Close();
                    }
                    else
                    {
                        StatusMessage.Text = "Ошибка: не удалось получить токен";
                        StatusMessage.Foreground = Brushes.Red;
                        Console.WriteLine("[OnLoginClicked] Не получен accessToken");
                    }
                }
                else
                {
                    // Ошибка авторизации, читаем сообщение сервера
                    var errorMsg = await response.Content.ReadAsStringAsync();
                    StatusMessage.Text = $"Ошибка: {errorMsg}";
                    StatusMessage.Foreground = Brushes.Red;
                    Console.WriteLine($"[OnLoginClicked] Ошибка входа: {response.StatusCode} - {errorMsg}");
                }
            }
            catch (HttpRequestException httpEx)
            {
                StatusMessage.Text = $"Ошибка соединения: {httpEx.Message}";
                StatusMessage.Foreground = Brushes.Red;
                Console.WriteLine($"[OnLoginClicked] Ошибка соединения: {httpEx.Message}");
            }
            catch (Exception ex)
            {
                StatusMessage.Text = $"Ошибка: {ex.Message}";
                StatusMessage.Foreground = Brushes.Red;
                Console.WriteLine($"[OnLoginClicked] Неожиданная ошибка: {ex.Message}");
            }
            finally
            {
                LoginButton.IsEnabled = true;
            }
        }

        // Обработчик для кнопки "Зарегистрироваться"
        private async void OnRegisterClicked(object? sender, RoutedEventArgs e)
        {
            var login = LoginTextBox.Text;
            var password = PasswordTextBox.Text;
            var email = EmailTextBox.Text?.Trim();

            StatusMessage.Text = string.Empty;
            StatusMessage.Foreground = Brushes.Transparent;

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                StatusMessage.Text = "Введите логин и пароль";
                StatusMessage.Foreground = Brushes.Red;
                return;
            }

            if (!string.IsNullOrWhiteSpace(email) && !IsValidEmail(email))
            {
                StatusMessage.Text = "Введите корректный email";
                StatusMessage.Foreground = Brushes.Red;
                return;
            }

            using var client = new HttpClient();
            try
            {
                // 1. Регистрируем пользователя
                var registerUrl = $"{App.ApiBaseUrl}user/register";
                var registerPayload = new { username = login, password = password };
                var registerContent = new StringContent(JsonSerializer.Serialize(registerPayload), Encoding.UTF8, "application/json");
                var registerResponse = await client.PostAsync(registerUrl, registerContent);

                if (registerResponse.IsSuccessStatusCode)
                {
                    // Сохраняем данные
                    _pendingUsername = login;
                    _pendingPassword = password;
                    _pendingEmail = email;

                    if (string.IsNullOrWhiteSpace(email))
                    {
                        // Если email не указан - сразу переходим ко входу
                        StatusMessage.Text = "Успешная регистрация! Теперь вы можете войти.";
                        StatusMessage.Foreground = Brushes.Green;
                        SetAppMode(AppMode.Login);
                    }
                    else
                    {
                        // Если email указан - логинимся и добавляем email
                        var loginUrl = $"{App.ApiBaseUrl}user/login";
                        var loginPayload = new { username = login, password = password };
                        var loginContent = new StringContent(JsonSerializer.Serialize(loginPayload), Encoding.UTF8, "application/json");
                        var loginResponse = await client.PostAsync(loginUrl, loginContent);

                        if (loginResponse.IsSuccessStatusCode)
                        {
                            var loginResponseContent = await loginResponse.Content.ReadAsStringAsync();
                            var authResponse = JsonSerializer.Deserialize<AuthResponse>(loginResponseContent);

                            if (authResponse != null && !string.IsNullOrEmpty(authResponse.accessToken))
                            {
                                _pendingAccessToken = authResponse.accessToken;

                                // Добавляем email через update
                                var updateUrl = $"{App.ApiBaseUrl}user/update";
                                var updatePayload = new { email = email };
                                var updateContent = new StringContent(JsonSerializer.Serialize(updatePayload), Encoding.UTF8, "application/json");

                                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _pendingAccessToken);
                                var updateResponse = await client.PutAsync(updateUrl, updateContent);

                                if (updateResponse.IsSuccessStatusCode)
                                {
                                    StatusMessage.Text = "Регистрация успешна! Проверьте email для подтверждения.";
                                    StatusMessage.Foreground = Brushes.Green;
                                    SetAppMode(AppMode.EmailVerification);
                                }
                                else
                                {
                                    StatusMessage.Text = "Регистрация успешна, но не удалось добавить email";
                                    StatusMessage.Foreground = Brushes.Orange;
                                    SetAppMode(AppMode.Login);
                                }
                            }
                            else
                            {
                                StatusMessage.Text = "Регистрация успешна, но не удалось войти";
                                StatusMessage.Foreground = Brushes.Orange;
                                SetAppMode(AppMode.Login);
                            }
                        }
                        else
                        {
                            StatusMessage.Text = "Регистрация успешна, но не удалось войти";
                            StatusMessage.Foreground = Brushes.Orange;
                            SetAppMode(AppMode.Login);
                        }
                    }
                }
                else
                {
                    var errorMsg = await registerResponse.Content.ReadAsStringAsync();
                    StatusMessage.Text = $"Ошибка регистрации: {errorMsg}";
                    StatusMessage.Foreground = Brushes.Red;
                }
            }
            catch (Exception ex)
            {
                StatusMessage.Text = $"Ошибка соединения: {ex.Message}";
                StatusMessage.Foreground = Brushes.Red;
            }
        }

        // Обработчик для кнопки подтверждения email
        private async void OnVerifyEmailClicked(object? sender, RoutedEventArgs e)
        {
            var code = VerificationCodeTextBox.Text;

            if (string.IsNullOrWhiteSpace(code))
            {
                StatusMessage.Text = "Введите код подтверждения";
                StatusMessage.Foreground = Brushes.Red;
                return;
            }

            using var client = new HttpClient();
            try
            {
                var verifyUrl = $"{App.ApiBaseUrl}user/verifyEmail";
                var verifyPayload = new { code = code };
                var verifyContent = new StringContent(JsonSerializer.Serialize(verifyPayload), Encoding.UTF8, "application/json");

                // Добавляем токен в заголовок
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _pendingAccessToken);

                var verifyResponse = await client.PostAsync(verifyUrl, verifyContent);

                if (verifyResponse.IsSuccessStatusCode)
                {
                    StatusMessage.Text = "Email успешно подтвержден! Теперь вы можете войти.";
                    StatusMessage.Foreground = Brushes.Green;

                    // Возвращаемся к окну входа
                    SetAppMode(AppMode.Login);
                    VerificationCodeTextBox.Text = string.Empty;
                }
                else
                {
                    var errorMsg = await verifyResponse.Content.ReadAsStringAsync();
                    StatusMessage.Text = $"Ошибка подтверждения: {errorMsg}";
                    StatusMessage.Foreground = Brushes.Red;
                }
            }
            catch (Exception ex)
            {
                StatusMessage.Text = $"Ошибка соединения: {ex.Message}";
                StatusMessage.Foreground = Brushes.Red;
            }
        }

        // Обработчик для кнопки повторной отправки кода
        private async void OnResendCodeClicked(object? sender, RoutedEventArgs e)
        {
            // Можно добавить логику для повторной отправки кода
            StatusMessage.Text = "Функция в разработке";
            StatusMessage.Foreground = Brushes.Blue;
        }

        // Переключение на регистрацию
        private void OnSwitchToRegisterClicked(object? sender, RoutedEventArgs e)
        {
            SetAppMode(AppMode.Register);
            EmailTextBox.Text = string.Empty;
        }

        // Переключение на вход
        private void OnSwitchToLoginClicked(object? sender, RoutedEventArgs e)
        {
            SetAppMode(AppMode.Login);
        }

        // Простая валидация email
        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
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