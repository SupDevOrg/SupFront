using Sup.ForTokens;
using Sup.Models;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sup.Services
{
    public class WebSocketService : IWebSocketService
    {
        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private Task? _listenTask;
        private uint _currentChatId;
        private uint _currentUserId;

        public event EventHandler<WebSocketMessageEventArgs>? OnMessageReceived;
        public event EventHandler<Exception>? OnError;

        public async Task<bool> OpenAsync(uint chatId, uint currentUserId, uint otherUserId)
        {
            Close();

            _ws = new ClientWebSocket();
            _cts = new CancellationTokenSource();
            _currentChatId = chatId;
            _currentUserId = currentUserId;

            try
            {
                var tokenData = await TokenManager.LoadTokensAsync();
                if (tokenData == null || string.IsNullOrEmpty(tokenData.AccessToken))
                    return false;

                _ws.Options.SetRequestHeader("Authorization", $"Bearer {tokenData.AccessToken}");
                var wsUrl = $"{App.WsBaseUrl}?user_id_1={currentUserId}&user_id_2={otherUserId}";
                await _ws.ConnectAsync(new Uri(wsUrl), _cts.Token);
                _listenTask = ListenAsync();
                return true;
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, ex);
                Close();
                return false;
            }
        }

        public async Task<bool> SendAsync(string message)
        {
            if (_ws?.State != WebSocketState.Open)
                return false;

            try
            {
                var data = Encoding.UTF8.GetBytes(message);
                await _ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, CancellationToken.None);
                return true;
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, ex);
                return false;
            }
        }

        public void Close()
        {
            _cts?.Cancel();
            _ws?.Dispose();
            _ws = null;
            _cts = null;
        }

        private async Task ListenAsync()
        {
            if (_ws == null)
                return;

            var buffer = new byte[4096];
            try
            {
                while (_ws.State == WebSocketState.Open)
                {
                    var ms = new System.IO.MemoryStream();
                    WebSocketReceiveResult? result = null;

                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    var json = Encoding.UTF8.GetString(ms.ToArray());
                    try
                    {
                        var msg = JsonSerializer.Deserialize<MessageDto>(json);
                        if (msg != null)
                        {
                            OnMessageReceived?.Invoke(this, new WebSocketMessageEventArgs
                            {
                                ChatId = msg.ChatId,
                                SenderUId = msg.SenderId,
                                Content = msg.Content,
                                CreatedAt = msg.CreatedAt
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        OnError?.Invoke(this, ex);
                    }
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, ex);
            }
        }
    }
}