using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sup
{
    public static class TokenManager
    {
        private static readonly string TokenFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Sup", "tokens.json");

        private static TokenData _cachedTokens;

        /// <summary>
        /// Сохраняет токены в кэш (память + файл)
        /// </summary>
        public static async Task SaveTokensAsync(string accessToken, string refreshToken)
        {
            try
            {
                _cachedTokens = new TokenData
                {
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    CreatedAt = DateTime.UtcNow
                };

                var directory = Path.GetDirectoryName(TokenFilePath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var json = JsonSerializer.Serialize(_cachedTokens);
                await File.WriteAllTextAsync(TokenFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка сохранения токенов: {ex.Message}");
            }
        }

        /// <summary>
        /// Загружает токены из кэша
        /// </summary>
        public static async Task<TokenData> LoadTokensAsync()
        {
            try
            {
                // Если уже загружены в память - возвращаем их
                if (_cachedTokens != null && !string.IsNullOrEmpty(_cachedTokens.AccessToken))
                    return _cachedTokens;

                // Иначе загружаем из файла
                if (!File.Exists(TokenFilePath))
                    return null;

                var json = await File.ReadAllTextAsync(TokenFilePath);
                _cachedTokens = JsonSerializer.Deserialize<TokenData>(json);
                return _cachedTokens;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка загрузки токенов: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Очищает токены (при logout)
        /// </summary>
        public static void ClearTokens()
        {
            try
            {
                _cachedTokens = null;
                if (File.Exists(TokenFilePath))
                    File.Delete(TokenFilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка очистки токенов: {ex.Message}");
            }
        }

        /// <summary>
        /// Обновляет access token используя refresh token
        /// </summary>
        public static async Task<bool> RefreshAccessTokenAsync()
        {
            try
            {
                var tokens = await LoadTokensAsync();
                if (tokens == null || string.IsNullOrEmpty(tokens.RefreshToken))
                    return false;

                using var client = new HttpClient();
                var url = $"{App.ApiBaseUrl}user/refresh";
                var payload = new { refreshToken = tokens.RefreshToken };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var response = await client.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var authResponse = JsonSerializer.Deserialize<AuthResponse>(responseContent);

                    if (!string.IsNullOrEmpty(authResponse.accessToken))
                    {
                        await SaveTokensAsync(authResponse.accessToken, authResponse.refreshToken ?? tokens.RefreshToken);
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обновления токена: {ex.Message}");
                return false;
            }
        }
    }

    public class TokenData
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}