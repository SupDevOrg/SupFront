using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Sup.ForTokens
{
    public class AuthenticatedHttpClientHandler : HttpClientHandler
    {
        private bool _isRefreshing = false;
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Загружаем токен из кэша
            var tokenData = await TokenManager.LoadTokensAsync();

            // Если есть токен - добавляем его в запрос
            if (tokenData != null && !string.IsNullOrEmpty(tokenData.AccessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
            }

            // Отправляем запрос
            var response = await base.SendAsync(request, cancellationToken);

            // Если получили 401 Unauthorized - пробуем обновить токен
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                await _refreshLock.WaitAsync();
                try
                {
                    if (!_isRefreshing)
                    {
                        _isRefreshing = true;

                        // Пробуем обновить access token
                        var refreshSuccess = await TokenManager.RefreshAccessTokenAsync();

                        if (refreshSuccess)
                        {
                            // Повторяем запрос с новым токеном
                            var newTokenData = await TokenManager.LoadTokensAsync();
                            if (newTokenData != null)
                            {
                                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newTokenData.AccessToken);
                                // Отправляем запрос заново
                                response = await base.SendAsync(request, cancellationToken);
                            }
                        }
                        _isRefreshing = false;
                    }
                }
                finally
                {
                    _refreshLock.Release();
                }
            }

            return response;
        }
    }

    public static class HttpClientFactory
    {
        public static HttpClient CreateAuthenticatedClient()
        {
            return new HttpClient(new AuthenticatedHttpClientHandler())
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }
    }
}