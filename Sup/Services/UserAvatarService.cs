using Sup.Models;
using Sup.ForTokens;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Sup.Services
{
    public class UserAvatarService : IUserAvatarService
    {
        private readonly HttpClient _httpClient;
        private readonly string _userServiceBaseUrl = $"{App.ApiBaseUrl}user";

        public UserAvatarService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? HttpClientFactory.CreateAuthenticatedClient();
        }

        /// <summary>
        /// Получить информацию текущего пользователя
        /// </summary>
        public async Task<UserDto> GetCurrentUserAsync()
        {
            try
            {
                Console.WriteLine($"[UserAvatarService.GetCurrentUserAsync] Получение данных текущего пользователя");
                var response = await _httpClient.GetAsync($"{_userServiceBaseUrl}/me");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[UserAvatarService.GetCurrentUserAsync] Ответ получен, размер: {content.Length} байт");
                    var user = JsonSerializer.Deserialize<UserDto>(content, 
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (user != null)
                    {
                        Console.WriteLine($"[UserAvatarService.GetCurrentUserAsync] Пользователь загружен: {user.Username}, AvatarUrl: {(string.IsNullOrEmpty(user.AvatarUrl) ? "не установлена" : "установлена")}");
                    }
                    return user ?? new UserDto();
                }

                Console.WriteLine($"[UserAvatarService.GetCurrentUserAsync] ❌ Ошибка: {response.StatusCode}");
                try
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(errorContent))
                    {
                        Console.WriteLine($"[UserAvatarService.GetCurrentUserAsync] Тело ошибки: {errorContent}");
                    }
                }
                catch { }

                return new UserDto();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserAvatarService.GetCurrentUserAsync] ❌ Исключение: {ex.GetType().Name}: {ex.Message}");
                return new UserDto();
            }
        }

        /// <summary>
        /// Получить URL для загрузки аватарки
        /// </summary>
        public async Task<AvatarUploadUrlResponse> GetAvatarUploadUrlAsync(string contentType, string fileName)
        {
            try
            {
                Console.WriteLine($"[UserAvatarService.GetAvatarUploadUrlAsync] Запрос URL для загрузки аватарки");
                Console.WriteLine($"[UserAvatarService.GetAvatarUploadUrlAsync] ContentType: {contentType}, FileName: {fileName}");

                // Создаём тело запроса
                var requestBody = new
                {
                    contentType,
                    fileName
                };

                var json = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                Console.WriteLine($"[UserAvatarService.GetAvatarUploadUrlAsync] Тело запроса: {json}");

                var response = await _httpClient.PostAsync($"{_userServiceBaseUrl}/avatar/upload-url", httpContent);

                Console.WriteLine($"[UserAvatarService.GetAvatarUploadUrlAsync] Статус ответа: {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[UserAvatarService.GetAvatarUploadUrlAsync] Ответ получен, размер: {responseContent.Length} байт");
                    Console.WriteLine($"[UserAvatarService.GetAvatarUploadUrlAsync] Содержимое: {responseContent}");

                    var uploadUrlResponse = JsonSerializer.Deserialize<AvatarUploadUrlResponse>(responseContent,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (uploadUrlResponse != null && !string.IsNullOrEmpty(uploadUrlResponse.UploadUrl))
                    {
                        Console.WriteLine($"[UserAvatarService.GetAvatarUploadUrlAsync] URL получен успешно, срок действия: {uploadUrlResponse.ExpiresInSeconds} сек");
                    }
                    return uploadUrlResponse ?? new AvatarUploadUrlResponse();
                }

                Console.WriteLine($"[UserAvatarService.GetAvatarUploadUrlAsync] ❌ Ошибка: {response.StatusCode}");

                try
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(errorContent))
                    {
                        Console.WriteLine($"[UserAvatarService.GetAvatarUploadUrlAsync] Тело ошибки: {errorContent}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UserAvatarService.GetAvatarUploadUrlAsync] Ошибка при чтении тела ошибки: {ex.Message}");
                }

                return new AvatarUploadUrlResponse();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserAvatarService.GetAvatarUploadUrlAsync] Исключение: {ex.Message}");
                return new AvatarUploadUrlResponse();
            }
        }

        /// <summary>
        /// Загрузить аватарку на сервер
        /// </summary>
        public async Task<bool> UploadAvatarAsync(string uploadUrl, byte[] imageData, string contentType)
        {
            try
            {
                Console.WriteLine($"[UserAvatarService.UploadAvatarAsync] Начало загрузки аватарки, размер: {imageData.Length} байт, тип: {contentType}");

                using (var content = new ByteArrayContent(imageData))
                {
                    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                    var response = await _httpClient.PutAsync(uploadUrl, content);

                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[UserAvatarService.UploadAvatarAsync] Аватарка успешно загружена, код ответа: {response.StatusCode}");
                        return true;
                    }

                    Console.WriteLine($"[UserAvatarService.UploadAvatarAsync] Ошибка загрузки: {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserAvatarService.UploadAvatarAsync] Исключение: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Масштабировать изображение до 512x512
        /// </summary>
        public byte[] ResizeImageTo512(string filePath)
        {
            try
            {
                Console.WriteLine($"[UserAvatarService.ResizeImageTo512] Начало масштабирования файла: {filePath}");

                using (var originalImage = SixLabors.ImageSharp.Image.Load(filePath))
                {
                    Console.WriteLine($"[UserAvatarService.ResizeImageTo512] Исходный размер: {originalImage.Width}x{originalImage.Height}");

                    originalImage.Mutate(x => x
                        .Resize(new SixLabors.ImageSharp.Processing.ResizeOptions
                        {
                            Size = new SixLabors.ImageSharp.Size(512, 512),
                            Mode = SixLabors.ImageSharp.Processing.ResizeMode.Crop
                        }));

                    using (var ms = new MemoryStream())
                    {
                        originalImage.SaveAsJpeg(ms);
                        var result = ms.ToArray();
                        Console.WriteLine($"[UserAvatarService.ResizeImageTo512] Масштабирование завершено, размер результата: {result.Length} байт");
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserAvatarService.ResizeImageTo512] Ошибка при масштабировании: {ex.Message}");
                return Array.Empty<byte>();
            }
        }
    }
}
