using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sup.ForTokens;
using Sup.Models;

namespace Sup.Services
{
    public class NotificationService : INotificationService
    {
        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private uint _userId;

        public event EventHandler<NotificationDto>? OnNotificationReceived;

        public async Task StartAsync(uint userId)
        {
            Console.WriteLine($"[NotificationService] Запуск для userId={userId}");
            Stop();
            _userId = userId;

            var tokenData = await TokenManager.LoadTokensAsync();
            if (tokenData == null || string.IsNullOrEmpty(tokenData.AccessToken))
            {
                Console.WriteLine("[NotificationService] Токен не найден, остановка.");
                return;
            }

            Console.WriteLine($"[NotificationService] Токен получен, подключаюсь к {App.NotificationWsUrl}");
            _ws = new ClientWebSocket();
            _ws.Options.SetRequestHeader("Authorization", $"Bearer {tokenData.AccessToken}");
            _ws.Options.SetRequestHeader("X-Auth-User-ID", userId.ToString());

            _cts = new CancellationTokenSource();
            try
            {
                await _ws.ConnectAsync(new Uri(App.NotificationWsUrl), _cts.Token);
                Console.WriteLine("[NotificationService] WebSocket для уведомлений подключён.");
                _ = ListenAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NotificationService] Ошибка подключения: {ex.Message}");
                Stop();
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            _ws?.Dispose();
            _ws = null;
            _cts = null;
        }

        private async Task ListenAsync()
        {
            var buffer = new byte[4096];
            try
            {
                while (_ws?.State == WebSocketState.Open)
                {
                    var ms = new System.IO.MemoryStream();
                    WebSocketReceiveResult? result;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts!.Token);
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    var json = Encoding.UTF8.GetString(ms.ToArray());
                    NotificationDto? notification = null;
                    try
                    {
                        notification = JsonSerializer.Deserialize<NotificationDto>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                    }
                    catch { /* игнорируем битые сообщения */ }

                    if (notification != null)
                        OnNotificationReceived?.Invoke(this, notification);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"[NotificationService] Ошибка WebSocket: {ex.Message}");
            }
        }
    }
}