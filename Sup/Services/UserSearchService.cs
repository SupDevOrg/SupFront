using Sup.Models;
using Sup.ForTokens;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sup.Services
{
    public class UserSearchService : IUserSearchService
    {
        private readonly HttpClient _httpClient;

        public UserSearchService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? HttpClientFactory.CreateAuthenticatedClient();
        }

        public async Task<SearchUsersResponse?> SearchAsync(string query, int page = 0, int size = 8)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new SearchUsersResponse();

            var url = $"{App.ApiBaseUrl}user/{Uri.EscapeDataString(query)}?page={page}&size={size}";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();

            try
            {
                var result = JsonSerializer.Deserialize<SearchUsersResponse>(content, 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserSearchService.SearchAsync] Ошибка десериализации: {ex.Message}");
                return null;
            }
        }

        public async Task<UserDto?> GetUserByIdAsync(uint userId)
        {
            try
            {
                var url = $"{App.ApiBaseUrl}user/id/{userId}";
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;
                using var stream = await response.Content.ReadAsStreamAsync();
                return await JsonSerializer.DeserializeAsync<UserDto>(stream);
            }
            catch
            {
                return null;
            }
        }

        public async Task<uint?> GetUserIdByNameAsync(string username)
        {
            try
            {
                var url = $"{App.ApiBaseUrl}user/{Uri.EscapeDataString(username)}?page=0&size=8";
                var resp = await _httpClient.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return null;
                using var stream = await resp.Content.ReadAsStreamAsync();
                var result = await JsonSerializer.DeserializeAsync<SearchUsersResponse>(stream);
                var user = result?.Users?.FirstOrDefault(u => u.Username == username);
                return user?.Id != null ? (uint?)user.Id : null;
            }
            catch
            {
                return null;
            }
        }
    }
}