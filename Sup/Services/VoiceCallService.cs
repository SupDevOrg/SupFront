using NAudio.CoreAudioApi;
using NAudio.Wave;
using SIPSorcery.Net;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sup.Services
{
    public class VoiceCallService : IVoiceCallService
    {
        private RTCPeerConnection? _peerConnection;
        private WasapiCapture? _capture;
        private WasapiOut? _playback;
        private BufferedWaveProvider? _playbackBuffer;
        private AudioFormat _negotiatedFormat;
        private bool _hasNegotiatedFormat;
        private readonly AudioEncoder _audioEncoder = new AudioEncoder();
        private readonly List<RTCIceCandidateInit> _pendingCandidates = new();
        private bool _remoteDescriptionSet;
        private bool _isRelayMode;

        public bool IsCallActive { get; private set; }

        public event EventHandler? OnCallConnected;
        public event EventHandler? OnCallEnded;
        public event EventHandler<string>? OnOfferCreated;
        public event EventHandler<string>? OnAnswerCreated;
        public event EventHandler<SignalingCandidateEventArgs>? OnIceCandidateReady;
        public event EventHandler<byte[]>? OnRelayAudioReady;

        // Инициирует исходящий звонок
        public async Task<bool> InitiateCallAsync(int micWasapiIndex, int speakerWasapiIndex)
        {
            Console.WriteLine($"[VoiceCallService.InitiateCallAsync] mic={micWasapiIndex} speaker={speakerWasapiIndex}");
            try
            {
                _peerConnection = CreatePeerConnection(micWasapiIndex, speakerWasapiIndex);
                var offer = _peerConnection.createOffer();
                await _peerConnection.setLocalDescription(offer);
                IsCallActive = true;
                Console.WriteLine("[VoiceCallService.InitiateCallAsync] Offer создан, отправляем");
                OnOfferCreated?.Invoke(this, offer.sdp);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VoiceCallService.InitiateCallAsync] Ошибка: {ex.Message}");
                return false;
            }
        }

        // Обрабатывает входящий Offer и создаёт Answer
        public async Task HandleOfferAsync(string sdp, int micWasapiIndex, int speakerWasapiIndex)
        {
            Console.WriteLine($"[VoiceCallService.HandleOfferAsync] mic={micWasapiIndex} speaker={speakerWasapiIndex} SDP_длина={sdp.Length}");
            try
            {
                _peerConnection = CreatePeerConnection(micWasapiIndex, speakerWasapiIndex);
                var offer = new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = sdp };
                _peerConnection.setRemoteDescription(offer);
                Console.WriteLine("[VoiceCallService.HandleOfferAsync] RemoteDescription установлен");
                _remoteDescriptionSet = true;
                FlushPendingCandidates();

                var answer = _peerConnection.createAnswer();
                await _peerConnection.setLocalDescription(answer);
                IsCallActive = true;
                Console.WriteLine("[VoiceCallService.HandleOfferAsync] Answer создан, отправляем");
                OnAnswerCreated?.Invoke(this, answer.sdp);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VoiceCallService.HandleOfferAsync] Ошибка: {ex.Message}");
            }
        }

        // Устанавливает удалённый Answer
        public Task HandleAnswerAsync(string sdp)
        {
            Console.WriteLine($"[VoiceCallService.HandleAnswerAsync] SDP_длина={sdp.Length}");
            if (_peerConnection == null)
            {
                Console.WriteLine("[VoiceCallService.HandleAnswerAsync] PeerConnection отсутствует, пропуск");
                return Task.CompletedTask;
            }
            try
            {
                var answer = new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = sdp };
                _peerConnection.setRemoteDescription(answer);
                Console.WriteLine("[VoiceCallService.HandleAnswerAsync] RemoteDescription установлен");
                _remoteDescriptionSet = true;
                FlushPendingCandidates();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VoiceCallService.HandleAnswerAsync] Ошибка: {ex.Message}");
            }
            return Task.CompletedTask;
        }

        // Добавляет ICE-кандидата (или ставит в очередь до setRemoteDescription)
        public Task HandleIceCandidateAsync(string candidate, string sdpMid, int sdpMLineIndex)
        {
            Console.WriteLine($"[VoiceCallService.HandleIceCandidateAsync] mid={sdpMid} idx={sdpMLineIndex}");
            var init = new RTCIceCandidateInit
            {
                candidate = candidate,
                sdpMid = sdpMid,
                sdpMLineIndex = (ushort)sdpMLineIndex
            };
            if (!_remoteDescriptionSet || _peerConnection == null)
            {
                Console.WriteLine("[VoiceCallService.HandleIceCandidateAsync] Кандидат поставлен в очередь");
                _pendingCandidates.Add(init);
            }
            else
            {
                Console.WriteLine("[VoiceCallService.HandleIceCandidateAsync] Кандидат добавлен");
                _peerConnection.addIceCandidate(init);
            }
            return Task.CompletedTask;
        }

        // Завершает звонок и освобождает ресурсы
        public Task HangUpAsync()
        {
            // Защита от повторного входа (onconnectionstatechange может вызвать нас повторно)
            if (!IsCallActive && _peerConnection == null)
            {
                Console.WriteLine("[VoiceCallService.HangUpAsync] Уже завершён, пропуск");
                return Task.CompletedTask;
            }

            Console.WriteLine("[VoiceCallService.HangUpAsync] Завершение звонка");
            IsCallActive = false;
            _remoteDescriptionSet = false;
            _hasNegotiatedFormat = false;
            _isRelayMode = false;
            _pendingCandidates.Clear();

            try { _capture?.StopRecording(); } catch (Exception ex) { Console.WriteLine($"[VoiceCallService.HangUpAsync] StopRecording: {ex.Message}"); }
            try { _capture?.Dispose(); } catch (Exception ex) { Console.WriteLine($"[VoiceCallService.HangUpAsync] Capture Dispose: {ex.Message}"); }
            _capture = null;

            try { _playback?.Stop(); } catch (Exception ex) { Console.WriteLine($"[VoiceCallService.HangUpAsync] Playback Stop: {ex.Message}"); }
            try { _playback?.Dispose(); } catch (Exception ex) { Console.WriteLine($"[VoiceCallService.HangUpAsync] Playback Dispose: {ex.Message}"); }
            _playback = null;
            _playbackBuffer = null;

            // Захватываем в локальную переменную и сразу обнуляем поле —
            // это предотвращает NPE при реентрантном вызове из onconnectionstatechange
            var pc = _peerConnection;
            _peerConnection = null;
            if (pc != null)
            {
                try
                {
                    pc.Close("hangup");
                    pc.Dispose();
                }
                catch (Exception ex) { Console.WriteLine($"[VoiceCallService.HangUpAsync] PC Close/Dispose: {ex.Message}"); }
            }

            Console.WriteLine("[VoiceCallService.HangUpAsync] Завершено");
            OnCallEnded?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Console.WriteLine("[VoiceCallService.Dispose] Освобождение ресурсов");
            HangUpAsync().GetAwaiter().GetResult();
        }

        // Воспроизводит аудио-пакет G.711, полученный через WebSocket-ретрансляцию
        public void ReceiveRelayAudio(byte[] encodedG711)
        {
            if (_isRelayMode && _hasNegotiatedFormat)
                PlayDecodedAudio(encodedG711);
        }

        // Создаёт RTCPeerConnection с STUN-серверами и аудиотреком
        private RTCPeerConnection CreatePeerConnection(int micWasapiIndex, int speakerWasapiIndex)
        {
            Console.WriteLine("[VoiceCallService.CreatePeerConnection] Создание PeerConnection");
            var config = new RTCConfiguration
            {
                iceServers = new List<RTCIceServer>
                {
                    new RTCIceServer { urls = "stun:stun.cloudflare.com:3478" },
                    new RTCIceServer { urls = "stun:stun.l.google.com:19302" },
                    // Бесплатные публичные TURN-серверы (для разработки; в продакшене — свой coturn)
                    new RTCIceServer
                    {
                        urls = "turn:freestun.net:3478",
                        username = "free",
                        credential = "free"
                    },
                    new RTCIceServer
                    {
                        urls = "turns:freestun.net:5350",
                        username = "free",
                        credential = "free"
                    },
                    new RTCIceServer
                    {
                        urls = "turn:openrelay.metered.ca:80",
                        username = "openrelayproject",
                        credential = "openrelayproject"
                    },
                    new RTCIceServer
                    {
                        urls = "turn:openrelay.metered.ca:443?transport=tcp",
                        username = "openrelayproject",
                        credential = "openrelayproject"
                    }
                }
            };

            var pc = new RTCPeerConnection(config);

            var audioTrack = new MediaStreamTrack(
                new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU),
                MediaStreamStatusEnum.SendRecv);
            pc.addTrack(audioTrack);
            Console.WriteLine("[VoiceCallService.CreatePeerConnection] Аудиотрек PCMU добавлен");

            pc.OnAudioFormatsNegotiated += (formats) =>
            {
                _negotiatedFormat = formats.FirstOrDefault();
                _hasNegotiatedFormat = true;
                Console.WriteLine($"[VoiceCallService] Аудиоформат согласован: {_negotiatedFormat.FormatName} (PT={_negotiatedFormat.FormatID})");
                StartAudio(pc, micWasapiIndex, speakerWasapiIndex);
            };

            pc.OnRtpPacketReceived += (ep, media, pkt) =>
            {
                if (media == SDPMediaTypesEnum.audio)
                    PlayDecodedAudio(pkt.Payload);
            };

            pc.onicecandidate += (candidate) =>
            {
                if (candidate != null)
                {
                    Console.WriteLine($"[VoiceCallService] Новый ICE-кандидат: {candidate.candidate}");
                    OnIceCandidateReady?.Invoke(this, new SignalingCandidateEventArgs
                    {
                        Candidate = candidate.candidate,
                        SdpMid = candidate.sdpMid ?? "0",
                        SdpMLineIndex = (int)(candidate.sdpMLineIndex)
                    });
                }
                else
                {
                    Console.WriteLine("[VoiceCallService] ICE-кандидаты завершены (null)");
                }
            };

            pc.onconnectionstatechange += async (state) =>
            {
                Console.WriteLine($"[VoiceCallService] Состояние соединения: {state}");
                if (state == RTCPeerConnectionState.connected)
                {
                    Console.WriteLine("[VoiceCallService] Соединение установлено!");
                    OnCallConnected?.Invoke(this, EventArgs.Empty);
                }
                else if (state == RTCPeerConnectionState.failed)
                {
                    // ICE не смог пробить NAT — переключаемся на WebSocket-ретрансляцию
                    Console.WriteLine("[VoiceCallService] ICE не удалось — активируем WebSocket-ретрансляцию");
                    _isRelayMode = true;
                    var failedPc = _peerConnection;
                    _peerConnection = null;
                    try { failedPc?.Close("relay"); } catch { }
                    try { failedPc?.Dispose(); } catch { }
                    // Сообщаем UI что звонок «подключён» (через релей)
                    OnCallConnected?.Invoke(this, EventArgs.Empty);
                }
                else if (state == RTCPeerConnectionState.closed && !_isRelayMode)
                {
                    Console.WriteLine("[VoiceCallService] Соединение закрыто");
                    await HangUpAsync();
                }
            };

            pc.oniceconnectionstatechange += (state) =>
            {
                Console.WriteLine($"[VoiceCallService] ICE-состояние: {state}");
            };

            Console.WriteLine("[VoiceCallService.CreatePeerConnection] PeerConnection готов");
            return pc;
        }

        // Запускает WASAPI-захват микрофона и воспроизведение через динамик
        private void StartAudio(RTCPeerConnection pc, int micWasapiIndex, int speakerWasapiIndex)
        {
            Console.WriteLine($"[VoiceCallService.StartAudio] Запуск аудио: mic={micWasapiIndex} speaker={speakerWasapiIndex}");
            try
            {
                var enumerator = new MMDeviceEnumerator();
                var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToArray();
                var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToArray();

                Console.WriteLine($"[VoiceCallService.StartAudio] Устройства захвата: {captureDevices.Length}, вывода: {renderDevices.Length}");

                var micDevice = micWasapiIndex >= 0 && micWasapiIndex < captureDevices.Length
                    ? captureDevices[micWasapiIndex]
                    : enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                Console.WriteLine($"[VoiceCallService.StartAudio] Микрофон: {micDevice.FriendlyName}");

                var speakerDevice = speakerWasapiIndex >= 0 && speakerWasapiIndex < renderDevices.Length
                    ? renderDevices[speakerWasapiIndex]
                    : enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                Console.WriteLine($"[VoiceCallService.StartAudio] Динамик: {speakerDevice.FriendlyName}");

                // Воспроизведение: буфер 8kHz 16-bit mono → ресэмплинг до 48kHz float stereo
                _playbackBuffer = new BufferedWaveProvider(new WaveFormat(8000, 16, 1))
                {
                    BufferLength = 65536,
                    DiscardOnBufferOverflow = true
                };
                var resampler = new MediaFoundationResampler(_playbackBuffer,
                    WaveFormat.CreateIeeeFloatWaveFormat(48000, 2))
                { ResamplerQuality = 60 };

                _playback = new WasapiOut(speakerDevice, AudioClientShareMode.Shared, false, 20);
                _playback.Init(resampler);
                _playback.Play();
                Console.WriteLine("[VoiceCallService.StartAudio] Воспроизведение запущено");

                // Захват: WASAPI → конвертируем в 8kHz 16-bit mono → кодируем G.711
                _capture = new WasapiCapture(micDevice);
                Console.WriteLine($"[VoiceCallService.StartAudio] Формат захвата: {_capture.WaveFormat}");
                _capture.DataAvailable += (s, e) => OnCaptureData(e.Buffer, e.BytesRecorded, _capture.WaveFormat, pc);
                _capture.StartRecording();
                Console.WriteLine("[VoiceCallService.StartAudio] Захват запущен");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VoiceCallService.StartAudio] Ошибка: {ex.Message}");
            }
        }

        // Конвертирует PCM из WASAPI → 8kHz 16-bit mono, кодирует G.711 и отправляет
        private void OnCaptureData(byte[] buffer, int bytesRecorded, WaveFormat srcFormat, RTCPeerConnection pc)
        {
            if (!IsCallActive || !_hasNegotiatedFormat) return;
            try
            {
                var pcm8k = ConvertToMono8KHz16Bit(buffer, bytesRecorded, srcFormat);
                if (pcm8k.Length == 0) return;

                var encoded = _audioEncoder.EncodeAudio(pcm8k, _negotiatedFormat);
                if (_isRelayMode)
                    OnRelayAudioReady?.Invoke(this, encoded);
                else
                    pc.SendAudio((uint)pcm8k.Length, encoded);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VoiceCallService.OnCaptureData] Ошибка: {ex.Message}");
            }
        }

        // Декодирует входящий RTP-пакет G.711 и добавляет в буфер воспроизведения
        private void PlayDecodedAudio(byte[] payload)
        {
            if (!_hasNegotiatedFormat) return;
            try
            {
                var pcm16 = _audioEncoder.DecodeAudio(payload, _negotiatedFormat);
                if (pcm16 == null || pcm16.Length == 0) return;

                var bytes = new byte[pcm16.Length * 2];
                Buffer.BlockCopy(pcm16, 0, bytes, 0, bytes.Length);
                _playbackBuffer?.AddSamples(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VoiceCallService.PlayDecodedAudio] Ошибка: {ex.Message}");
            }
        }

        // Применяет накопленные ICE-кандидаты после setRemoteDescription
        private void FlushPendingCandidates()
        {
            Console.WriteLine($"[VoiceCallService.FlushPendingCandidates] Применяем {_pendingCandidates.Count} кандидатов");
            foreach (var c in _pendingCandidates)
                _peerConnection?.addIceCandidate(c);
            _pendingCandidates.Clear();
        }

        // Конвертирует буфер WASAPI (любой формат) в 8kHz 16-bit PCM mono
        private static short[] ConvertToMono8KHz16Bit(byte[] buffer, int bytesRecorded, WaveFormat fmt)
        {
            // Шаг 1: нормализуем в 16-bit PCM (из float32 или int16)
            short[] sourceSamples;
            if (fmt.Encoding == WaveFormatEncoding.IeeeFloat && fmt.BitsPerSample == 32)
            {
                int count = bytesRecorded / 4;
                sourceSamples = new short[count];
                for (int i = 0; i < count; i++)
                {
                    float f = BitConverter.ToSingle(buffer, i * 4);
                    sourceSamples[i] = (short)Math.Clamp((int)(f * 32767f), -32768, 32767);
                }
            }
            else if (fmt.Encoding == WaveFormatEncoding.Pcm && fmt.BitsPerSample == 16)
            {
                sourceSamples = new short[bytesRecorded / 2];
                Buffer.BlockCopy(buffer, 0, sourceSamples, 0, bytesRecorded);
            }
            else
            {
                Console.WriteLine($"[ConvertToMono8KHz16Bit] Неподдерживаемый формат: {fmt.Encoding} {fmt.BitsPerSample}bit");
                return Array.Empty<short>();
            }

            // Шаг 2: микшируем в моно
            int channels = fmt.Channels;
            short[] monoSamples;
            if (channels > 1)
            {
                monoSamples = new short[sourceSamples.Length / channels];
                for (int i = 0; i < monoSamples.Length; i++)
                {
                    long sum = 0;
                    for (int ch = 0; ch < channels; ch++)
                        sum += sourceSamples[i * channels + ch];
                    monoSamples[i] = (short)(sum / channels);
                }
            }
            else
            {
                monoSamples = sourceSamples;
            }

            // Шаг 3: ресэмплинг до 8kHz (линейная интерполяция)
            int srcRate = fmt.SampleRate;
            if (srcRate == 8000)
                return monoSamples;

            double ratio = 8000.0 / srcRate;
            int outLen = (int)(monoSamples.Length * ratio);
            var result = new short[outLen];
            for (int i = 0; i < outLen; i++)
            {
                double srcIdx = i / ratio;
                int idx0 = (int)srcIdx;
                int idx1 = Math.Min(idx0 + 1, monoSamples.Length - 1);
                double frac = srcIdx - idx0;
                result[i] = (short)(monoSamples[idx0] * (1 - frac) + monoSamples[idx1] * frac);
            }
            return result;
        }
    }
}
