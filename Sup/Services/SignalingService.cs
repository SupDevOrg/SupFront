using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sup.Services
{
    public class SignalingService : ISignalingService
    {
        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private string _currentRoom = string.Empty;

        public bool IsConnected => _ws?.State == WebSocketState.Open;

        public event EventHandler<string>? OnOfferReceived;
        public event EventHandler<string>? OnAnswerReceived;
        public event EventHandler<SignalingCandidateEventArgs>? OnIceCandidateReceived;
        public event EventHandler? OnPeerJoined;
        public event EventHandler? OnPeerLeft;
        public event EventHandler<byte[]>? OnAudioReceived;

        // Подключение к комнате сигнализации (комната передаётся в query-строке URL)
        public async Task ConnectAsync(string roomId, string userId)
        {
            Console.WriteLine($"[SignalingService.ConnectAsync] Подключаемся к комнате '{roomId}' как userId='{userId}'");
            Disconnect();
            _ws = new ClientWebSocket();
            _cts = new CancellationTokenSource();
            _currentRoom = roomId;

            // Сервер принимает комнату через query-параметр /ws?room={roomId}
            var uri = new Uri($"{App.SignalingBaseUrl}/ws?room={Uri.EscapeDataString(roomId)}");
            Console.WriteLine($"[SignalingService.ConnectAsync] URI: {uri}");

            await _ws.ConnectAsync(uri, _cts.Token);
            Console.WriteLine($"[SignalingService.ConnectAsync] WebSocket подключён. Состояние: {_ws.State}");

            // Уведомляем других участников комнаты о нашем присутствии
            await SendJsonAsync(new { type = "joined", userId });
            Console.WriteLine($"[SignalingService.ConnectAsync] Отправлено 'joined' в комнату '{roomId}'");

            _ = ListenAsync();
        }

        // Отправка Offer
        public async Task SendOfferAsync(string sdp)
        {
            Console.WriteLine($"[SignalingService.SendOfferAsync] Отправка offer, SDP длина={sdp.Length}");
            await SendJsonAsync(new { type = "offer", sdp });
            Console.WriteLine("[SignalingService.SendOfferAsync] Offer отправлен");
        }

        // Отправка Answer
        public async Task SendAnswerAsync(string sdp)
        {
            Console.WriteLine($"[SignalingService.SendAnswerAsync] Отправка answer, SDP длина={sdp.Length}");
            await SendJsonAsync(new { type = "answer", sdp });
            Console.WriteLine("[SignalingService.SendAnswerAsync] Answer отправлен");
        }

        // Отправка ICE-кандидата
        public async Task SendIceCandidateAsync(string candidate, string sdpMid, int sdpMLineIndex)
        {
            Console.WriteLine($"[SignalingService.SendIceCandidateAsync] Кандидат: mid={sdpMid} idx={sdpMLineIndex} val={candidate}");
            await SendJsonAsync(new { type = "candidate", candidate, sdpMid, sdpMLineIndex });
        }

        // Выход из комнаты — отправляем leave, чтобы собеседник мог завершить звонок
        public async Task LeaveAsync()
        {
            Console.WriteLine($"[SignalingService.LeaveAsync] Выход из комнаты '{_currentRoom}'");
            if (_ws?.State == WebSocketState.Open)
            {
                await SendJsonAsync(new { type = "leave" });
                Console.WriteLine("[SignalingService.LeaveAsync] Сообщение leave отправлено");
            }
        }

        // Отправка аудио-пакета через WebSocket-ретрансляцию (фолбек при недоступности TURN)
        public async Task SendAudioAsync(byte[] encodedG711)
        {
            await SendJsonAsync(new { type = "audio", data = Convert.ToBase64String(encodedG711) }, log: false);
        }

        // Полное отключение WebSocket
        public void Disconnect()
        {
            Console.WriteLine($"[SignalingService.Disconnect] Отключение (комната: '{_currentRoom}')");
            _cts?.Cancel();
            _ws?.Dispose();
            _ws = null;
            _cts = null;
            _currentRoom = string.Empty;
        }

        private async Task SendJsonAsync(object payload, bool log = true)
        {
            if (_ws?.State != WebSocketState.Open)
            {
                if (log) Console.WriteLine($"[SignalingService.SendJsonAsync] Пропуск — WS не открыт (State={_ws?.State})");
                return;
            }
            var json = JsonSerializer.Serialize(payload);
            if (log) Console.WriteLine($"[SignalingService.SendJsonAsync] → {json}");
            var data = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private async Task ListenAsync()
        {
            if (_ws == null) return;
            Console.WriteLine("[SignalingService.ListenAsync] Начало прослушивания сообщений");
            var buffer = new byte[16384];
            try
            {
                while (_ws.State == WebSocketState.Open)
                {
                    using var ms = new System.IO.MemoryStream();
                    WebSocketReceiveResult? result;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts!.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            Console.WriteLine("[SignalingService.ListenAsync] Получен Close-фрейм");
                            return;
                        }
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    var json = Encoding.UTF8.GetString(ms.ToArray());
                    if (!json.StartsWith("{\"type\":\"audio\"", StringComparison.Ordinal))
                        Console.WriteLine($"[SignalingService.ListenAsync] ← {json}");
                    HandleMessage(json);
                }
                Console.WriteLine($"[SignalingService.ListenAsync] WS закрыт, State={_ws?.State}");
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
            {
                Console.WriteLine($"[SignalingService.ListenAsync] Соединение закрыто: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SignalingService.ListenAsync] Неожиданная ошибка: {ex.Message}");
            }
        }

        private void HandleMessage(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeProp)) return;
                var type = typeProp.GetString();
                if (type != "audio") Console.WriteLine($"[SignalingService.HandleMessage] Тип: {type}");

                switch (type)
                {
                    case "offer":
                        var offerSdp = root.TryGetProperty("sdp", out var oSdp) ? oSdp.GetString() ?? string.Empty : string.Empty;
                        Console.WriteLine($"[SignalingService.HandleMessage] Получен offer, SDP длина={offerSdp.Length}");
                        OnOfferReceived?.Invoke(this, offerSdp);
                        break;

                    case "answer":
                        var answerSdp = root.TryGetProperty("sdp", out var aSdp) ? aSdp.GetString() ?? string.Empty : string.Empty;
                        Console.WriteLine($"[SignalingService.HandleMessage] Получен answer, SDP длина={answerSdp.Length}");
                        OnAnswerReceived?.Invoke(this, answerSdp);
                        break;

                    case "candidate":
                        var candidate = root.TryGetProperty("candidate", out var c) ? c.GetString() ?? string.Empty : string.Empty;
                        var sdpMid = root.TryGetProperty("sdpMid", out var m) ? m.GetString() ?? "0" : "0";
                        var idx = root.TryGetProperty("sdpMLineIndex", out var i) ? i.GetInt32() : 0;
                        Console.WriteLine($"[SignalingService.HandleMessage] Получен ICE-кандидат: mid={sdpMid} idx={idx}");
                        OnIceCandidateReceived?.Invoke(this, new SignalingCandidateEventArgs
                        {
                            Candidate = candidate,
                            SdpMid = sdpMid,
                            SdpMLineIndex = idx
                        });
                        break;

                    case "peer_joined":
                    case "joined":
                    case "join":
                        Console.WriteLine("[SignalingService.HandleMessage] Собеседник подключился к комнате");
                        OnPeerJoined?.Invoke(this, EventArgs.Empty);
                        break;

                    case "peer_left":
                    case "left":
                    case "leave":
                        Console.WriteLine("[SignalingService.HandleMessage] Собеседник покинул комнату");
                        OnPeerLeft?.Invoke(this, EventArgs.Empty);
                        break;

                    case "audio":
                        var audioData = root.TryGetProperty("data", out var ad) ? ad.GetString() : null;
                        if (audioData != null)
                            OnAudioReceived?.Invoke(this, Convert.FromBase64String(audioData));
                        break;

                    default:
                        Console.WriteLine($"[SignalingService.HandleMessage] Неизвестный тип: '{type}'");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SignalingService.HandleMessage] Ошибка: {ex.Message}");
            }
        }
    }
}
