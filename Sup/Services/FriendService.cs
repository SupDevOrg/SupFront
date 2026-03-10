using Sup.Models;
using Sup.ForTokens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sup.Services
{
    public class FriendService : IFriendService
    {
        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, FriendshipStatusDto> _statusCache = new();
        private readonly Dictionary<uint, DateTime> _cacheTimes = new();
        private const int CACHE_DURATION_SECONDS = 300;

        public FriendService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? HttpClientFactory.CreateAuthenticatedClient();
        }

        public async Task<List<FriendshipDto>?> GetFriendsAsync(uint userId)
        {
            try
            {
                Console.WriteLine($"[FriendService.GetFriendsAsync] Получение списка друзей для пользователя {userId}");
                InvalidateCacheForUser(userId);
                var url = $"{App.ApiBaseUrl}user/{userId}/friends";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[FriendService.GetFriendsAsync] Ошибка: {response.StatusCode}");
                    return null;
                }

                var jsonContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[FriendService.GetFriendsAsync] JSON (первые 300 символов): {jsonContent.Substring(0, Math.Min(300, jsonContent.Length))}");
                
                var pagedResponse = JsonSerializer.Deserialize<PagedResponse<FriendshipDto>>(jsonContent);
                if (pagedResponse?.Content != null && pagedResponse.Content.Count > 0)
                {
                    Console.WriteLine($"[FriendService.GetFriendsAsync] Получено {pagedResponse.Content.Count} друзей из paged response");
                    return pagedResponse.Content;
                }

                var friendsResponse = JsonSerializer.Deserialize<FriendsListResponse>(jsonContent);
                var friends = friendsResponse?.Friends ?? new();
                Console.WriteLine($"[FriendService.GetFriendsAsync] Получено {friends.Count} друзей");
                return friends;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FriendService.GetFriendsAsync] Исключение: {ex.Message}");
                return null;
            }
        }

        public async Task<FriendshipStatusDto?> CheckFriendshipStatusAsync(uint userId, uint targetId)
        {
            try
            {
                string cacheKey = $"{userId}_{targetId}";
                
                if (_statusCache.ContainsKey(cacheKey) && _cacheTimes.ContainsKey(targetId))
                {
                    var cacheTime = _cacheTimes[targetId];
                    if ((DateTime.Now - cacheTime).TotalSeconds < CACHE_DURATION_SECONDS)
                    {
                        Console.WriteLine($"[FriendService.CheckFriendshipStatusAsync] Используется кэш для {targetId}");
                        return _statusCache[cacheKey];
                    }
                }

                Console.WriteLine($"[FriendService.CheckFriendshipStatusAsync] Проверка статуса с пользователем {targetId}");
                var url = $"{App.ApiBaseUrl}user/{userId}/friends/{targetId}/status";
                Console.WriteLine($"[FriendService.CheckFriendshipStatusAsync] URL: {url}");
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[FriendService.CheckFriendshipStatusAsync] Ошибка: {response.StatusCode}");
                    return null;
                }

                using var stream = await response.Content.ReadAsStreamAsync();
                var jsonStr = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[FriendService.CheckFriendshipStatusAsync] JSON ответ: {jsonStr}");
                
                var status = JsonSerializer.Deserialize<FriendshipStatusDto>(jsonStr);
                
                _statusCache[cacheKey] = status ?? new();
                _cacheTimes[targetId] = DateTime.Now;
                
                Console.WriteLine($"[FriendService.CheckFriendshipStatusAsync] Статус: Status={status?.Status}, IsOutgoingRequest={status?.IsOutgoingRequest}");
                return status;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FriendService.CheckFriendshipStatusAsync] Исключение: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> SendFriendRequestAsync(uint userId, uint targetId)
        {
            try
            {
                Console.WriteLine($"[FriendService.SendFriendRequestAsync] Отправление запроса в друзья пользователю {targetId}");
                var url = $"{App.ApiBaseUrl}user/{userId}/friends/{targetId}";
                Console.WriteLine($"[FriendService.SendFriendRequestAsync] URL: {url}");
                Console.WriteLine($"[FriendService.SendFriendRequestAsync] HTTP Method: POST");
                
                var content = new StringContent("", System.Text.Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);

                bool success = response.IsSuccessStatusCode;
                Console.WriteLine($"[FriendService.SendFriendRequestAsync] Статус ответа: {response.StatusCode}");
                if (success)
                {
                    Console.WriteLine($"[FriendService.SendFriendRequestAsync] Запрос успешно отправлен");
                    InvalidateCache(userId, targetId);
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[FriendService.SendFriendRequestAsync] Ответ ошибки: {errorContent}");
                }
                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FriendService.SendFriendRequestAsync] Исключение: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> AcceptFriendRequestAsync(uint userId, uint friendId)
        {
            try
            {
                Console.WriteLine($"[FriendService.AcceptFriendRequestAsync] Принятие запроса от пользователя {friendId}");
                var url = $"{App.ApiBaseUrl}user/{userId}/friends/{friendId}/accept";
                Console.WriteLine($"[FriendService.AcceptFriendRequestAsync] URL: {url}");
                Console.WriteLine($"[FriendService.AcceptFriendRequestAsync] HTTP Method: PUT");
                var request = new HttpRequestMessage(HttpMethod.Put, url);
                var response = await _httpClient.SendAsync(request);

                bool success = response.IsSuccessStatusCode;
                Console.WriteLine($"[FriendService.AcceptFriendRequestAsync] Статус ответа: {response.StatusCode}");
                if (success)
                {
                    Console.WriteLine($"[FriendService.AcceptFriendRequestAsync] Запрос принят");
                    InvalidateCache(userId, friendId);
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[FriendService.AcceptFriendRequestAsync] Ответ ошибки: {errorContent}");
                }
                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FriendService.AcceptFriendRequestAsync] Исключение: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RejectFriendRequestAsync(uint userId, uint friendId)
        {
            try
            {
                Console.WriteLine($"[FriendService.RejectFriendRequestAsync] Отклонение входящего запроса от пользователя {friendId}");
                var url = $"{App.ApiBaseUrl}user/{userId}/friends/{friendId}/reject";
                Console.WriteLine($"[FriendService.RejectFriendRequestAsync] URL: {url}");
                var response = await _httpClient.DeleteAsync(url);

                bool success = response.IsSuccessStatusCode;
                if (success)
                {
                    Console.WriteLine($"[FriendService.RejectFriendRequestAsync] Запрос отклонен");
                    InvalidateCache(userId, friendId);
                }
                else
                {
                    Console.WriteLine($"[FriendService.RejectFriendRequestAsync] Ошибка: {response.StatusCode}");
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[FriendService.RejectFriendRequestAsync] Ответ ошибки: {errorContent}");
                }
                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FriendService.RejectFriendRequestAsync] Исключение: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CancelFriendRequestAsync(uint userId, uint friendId)
        {
            try
            {
                Console.WriteLine($"[FriendService.CancelFriendRequestAsync] Отмена исходящего запроса для пользователя {friendId}");
                var url = $"{App.ApiBaseUrl}user/{userId}/friends/{friendId}/cancel";
                Console.WriteLine($"[FriendService.CancelFriendRequestAsync] URL: {url}");
                var response = await _httpClient.DeleteAsync(url);

                bool success = response.IsSuccessStatusCode;
                Console.WriteLine($"[FriendService.CancelFriendRequestAsync] Код ответа: {response.StatusCode}");
                
                if (success)
                {
                    Console.WriteLine($"[FriendService.CancelFriendRequestAsync] Запрос успешно отменен");
                    InvalidateCache(userId, friendId);
                }
                else
                {
                    Console.WriteLine($"[FriendService.CancelFriendRequestAsync] Ошибка: {response.StatusCode}");
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[FriendService.CancelFriendRequestAsync] Ответ ошибки: {errorContent}");
                }
                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FriendService.CancelFriendRequestAsync] Исключение: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RemoveFriendAsync(uint userId, uint friendId)
        {
            try
            {
                Console.WriteLine($"[FriendService.RemoveFriendAsync] Удаление друга {friendId}");
                var url = $"{App.ApiBaseUrl}user/{userId}/friends/{friendId}";
                Console.WriteLine($"[FriendService.RemoveFriendAsync] URL: {url}");
                var response = await _httpClient.DeleteAsync(url);

                bool success = response.IsSuccessStatusCode;
                if (success)
                {
                    Console.WriteLine($"[FriendService.RemoveFriendAsync] Друг удален");
                    InvalidateCache(userId, friendId);
                }
                else
                {
                    Console.WriteLine($"[FriendService.RemoveFriendAsync] Ошибка: {response.StatusCode}");
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[FriendService.RemoveFriendAsync] Ответ ошибки: {errorContent}");
                }
                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FriendService.RemoveFriendAsync] Исключение: {ex.Message}");
                return false;
            }
        }

        private void InvalidateCacheForUser(uint userId)
        {
            // Инвалидируем весь кэш статуса для пользователя
            var keysToRemove = _statusCache.Keys.Where(k => k.StartsWith($"{userId}_")).ToList();
            foreach (var key in keysToRemove)
            {
                _statusCache.Remove(key);
            }
            Console.WriteLine($"[InvalidateCacheForUser] Кэш инвалидирован для пользователя {userId} ({keysToRemove.Count} записей)");
        }

        private void InvalidateCache(uint userId, uint targetId)
        {
            string cacheKey = $"{userId}_{targetId}";
            if (_statusCache.ContainsKey(cacheKey))
            {
                _statusCache.Remove(cacheKey);
                Console.WriteLine($"[InvalidateCache] Кэш инвалидирован для {targetId}");
            }
            if (_cacheTimes.ContainsKey(targetId))
            {
                _cacheTimes.Remove(targetId);
            }
        }

        private async Task<string?> GetUsernameByIdAsync(int userId)
        {
            try
            {
                var url = $"{App.ApiBaseUrl}user/id/{userId}";
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;
                using var stream = await response.Content.ReadAsStreamAsync();
                var user = await JsonSerializer.DeserializeAsync<UserDto>(stream);
                return user?.Username;
            }
            catch
            {
                return null;
            }
        }

        public async Task<List<FriendshipDto>?> GetIncomingFriendRequestsAsync(uint userId)
        {
            try
            {
                Console.WriteLine($"[FriendService.GetIncomingFriendRequestsAsync] Получение входящих запросов в друзья");
                InvalidateCacheForUser(userId);
                var url = $"{App.ApiBaseUrl}user/{userId}/friends/requests/incoming";
                Console.WriteLine($"[FriendService.GetIncomingFriendRequestsAsync] URL: {url}");
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var jsonContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[FriendService.GetIncomingFriendRequestsAsync] JSON (первые 300 символов): {jsonContent.Substring(0, Math.Min(300, jsonContent.Length))}");
                    
                    // V1 API возвращает просто массив, не PagedResponse
                    var requests = JsonSerializer.Deserialize<List<FriendRequestDto>>(jsonContent);
                    if (requests != null && requests.Count > 0)
                    {
                        var converted = new List<FriendshipDto>();
                        foreach (var req in requests)
                        {
                            // Загружаем username пользователя по ID
                            var username = await GetUsernameByIdAsync(req.RequesterId);
                            converted.Add(new FriendshipDto 
                            { 
                                Id = req.RequesterId,
                                Username = username ?? req.RequesterId.ToString()
                            });
                        }
                        Console.WriteLine($"[FriendService.GetIncomingFriendRequestsAsync] Найдено {converted.Count} входящих запросов");
                        return converted;
                    }
                    Console.WriteLine($"[FriendService.GetIncomingFriendRequestsAsync] Запросы не найдены");
                    return new List<FriendshipDto>();
                }

                Console.WriteLine($"[FriendService.GetIncomingFriendRequestsAsync] Ошибка ({response.StatusCode})");
                return new List<FriendshipDto>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FriendService.GetIncomingFriendRequestsAsync] Исключение: {ex.Message}");
                return new List<FriendshipDto>();
            }
        }

        public async Task<List<FriendshipDto>?> GetOutgoingFriendRequestsAsync(uint userId)
        {
            try
            {
                Console.WriteLine($"[FriendService.GetOutgoingFriendRequestsAsync] Получение исходящих запросов в друзья");
                InvalidateCacheForUser(userId);
                var url = $"{App.ApiBaseUrl}user/{userId}/friends/requests/outgoing";
                Console.WriteLine($"[FriendService.GetOutgoingFriendRequestsAsync] URL: {url}");
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var jsonContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[FriendService.GetOutgoingFriendRequestsAsync] JSON (первые 300 символов): {jsonContent.Substring(0, Math.Min(300, jsonContent.Length))}");
                    
                    // V1 API возвращает просто массив, не PagedResponse
                    var requests = JsonSerializer.Deserialize<List<FriendRequestDto>>(jsonContent);
                    if (requests != null && requests.Count > 0)
                    {
                        var converted = new List<FriendshipDto>();
                        foreach (var req in requests)
                        {
                            // Загружаем username пользователя по ID
                            var username = await GetUsernameByIdAsync(req.AddresseeId);
                            converted.Add(new FriendshipDto 
                            { 
                                Id = req.AddresseeId,
                                Username = username ?? req.AddresseeId.ToString()
                            });
                        }
                        Console.WriteLine($"[FriendService.GetOutgoingFriendRequestsAsync] Найдено {converted.Count} исходящих запросов");
                        return converted;
                    }
                    Console.WriteLine($"[FriendService.GetOutgoingFriendRequestsAsync] Запросы не найдены");
                    return new List<FriendshipDto>();
                }

                Console.WriteLine($"[FriendService.GetOutgoingFriendRequestsAsync] Ошибка ({response.StatusCode})");
                return new List<FriendshipDto>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FriendService.GetOutgoingFriendRequestsAsync] Исключение: {ex.Message}");
                return new List<FriendshipDto>();
            }
        }

    }
}
