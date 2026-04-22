using Concentus;
using Concentus.Enums;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
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
        private IOpusEncoder? _opusEncoder;
        private IOpusDecoder? _opusDecoder;
        private const int OPUS_FRAME_SIZE = 960; // 20ms при 48kHz
        private readonly List<RTCIceCandidateInit> _pendingCandidates = new();
        private bool _remoteDescriptionSet;
        private bool _isRelayMode;
        private readonly Queue<short> _pcmBuffer = new();
        public bool IsCallActive { get; private set; }

        public event EventHandler? OnCallConnected;
        public event EventHandler? OnCallEnded;
        public event EventHandler<string>? OnOfferCreated;
        public event EventHandler<string>? OnAnswerCreated;
        public event EventHandler<SignalingCandidateEventArgs>? OnIceCandidateReady;
        public event EventHandler<byte[]>? OnRelayAudioReady;

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

        public Task HangUpAsync()
        {
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

            try { _capture?.StopRecording(); } catch { }
            _pcmBuffer?.Clear(); // ← очистка буфера PCM
            try { _capture?.Dispose(); } catch { }
            _capture = null;

            try { _playback?.Stop(); } catch { }
            try { _playback?.Dispose(); } catch { }
            _playback = null;
            _playbackBuffer = null;

            var pc = _peerConnection;
            _peerConnection = null;
            if (pc != null)
            {
                try { pc.Close("hangup"); } catch { }
                try { pc.Dispose(); } catch { }
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

        public void ReceiveRelayAudio(byte[] encodedG711)
        {
            if (_isRelayMode && _hasNegotiatedFormat)
                PlayDecodedAudio(encodedG711);
        }

        private RTCPeerConnection CreatePeerConnection(int micWasapiIndex, int speakerWasapiIndex)
        {
            Console.WriteLine("[VoiceCallService.CreatePeerConnection] Создание PeerConnection");
            var config = new RTCConfiguration
            {
                iceServers = new List<RTCIceServer>
                {
                    new RTCIceServer { urls = "stun:stun.cloudflare.com:3478" },
                    new RTCIceServer { urls = "stun:stun.l.google.com:19302" },
                    new RTCIceServer { urls = "turn:freestun.net:3478", username = "free", credential = "free" },
                    new RTCIceServer { urls = "turns:freestun.net:5350", username = "free", credential = "free" },
                    new RTCIceServer { urls = "turn:openrelay.metered.ca:80", username = "openrelayproject", credential = "openrelayproject" },
                    new RTCIceServer { urls = "turn:openrelay.metered.ca:443?transport=tcp", username = "openrelayproject", credential = "openrelayproject" }
                }
            };

            var pc = new RTCPeerConnection(config);

            // Моно 48 kHz (типично для голоса)
            var opusFormat = new AudioFormat(96, "opus", 48000, 1);
            var audioTrack = new MediaStreamTrack(opusFormat, MediaStreamStatusEnum.SendRecv);
            pc.addTrack(audioTrack);
            Console.WriteLine("[VoiceCallService.CreatePeerConnection] OPUS 48 kHz (моно)");

            pc.OnAudioFormatsNegotiated += (formats) =>
            {
                _negotiatedFormat = formats.FirstOrDefault();
                _hasNegotiatedFormat = true;
                Console.WriteLine($"[VoiceCallService] Аудиоформат согласован: {_negotiatedFormat.FormatName} (PT={_negotiatedFormat.FormatID})");

                if (_negotiatedFormat.FormatName.Contains("opus", StringComparison.OrdinalIgnoreCase))
                {
                    _opusEncoder = OpusCodecFactory.CreateEncoder(48000, 1, OpusApplication.OPUS_APPLICATION_AUDIO);
                    _opusEncoder.Bitrate = 64000; // 64 kbps для моно голоса
                    _opusDecoder = OpusCodecFactory.CreateDecoder(48000, 1);
                    Console.WriteLine("[VoiceCallService] Opus моно кодеры инициализированы (64 kbps)");
                }

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
                else Console.WriteLine("[VoiceCallService] ICE-кандидаты завершены (null)");
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
                    Console.WriteLine("[VoiceCallService] ICE не удалось — активируем WebSocket-ретрансляцию");
                    _isRelayMode = true;
                    var failedPc = _peerConnection;
                    _peerConnection = null;
                    try { failedPc?.Close("relay"); } catch { }
                    try { failedPc?.Dispose(); } catch { }
                    OnCallConnected?.Invoke(this, EventArgs.Empty);
                }
                else if (state == RTCPeerConnectionState.closed && !_isRelayMode)
                {
                    Console.WriteLine("[VoiceCallService] Соединение закрыто");
                    await HangUpAsync();
                }
            };

            pc.oniceconnectionstatechange += (state) => Console.WriteLine($"[VoiceCallService] ICE-состояние: {state}");

            Console.WriteLine("[VoiceCallService.CreatePeerConnection] PeerConnection готов");
            return pc;
        }

        private void StartAudio(RTCPeerConnection pc, int micWasapiIndex, int speakerWasapiIndex)
        {
            Console.WriteLine($"[VoiceCallService.StartAudio] Запуск аудио: mic={micWasapiIndex} speaker={speakerWasapiIndex}");
            try
            {
                var enumerator = new MMDeviceEnumerator();
                var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToArray();
                var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToArray();

                var micDevice = micWasapiIndex >= 0 && micWasapiIndex < captureDevices.Length
                    ? captureDevices[micWasapiIndex]
                    : enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                Console.WriteLine($"[VoiceCallService.StartAudio] Микрофон: {micDevice.FriendlyName}");

                var speakerDevice = speakerWasapiIndex >= 0 && speakerWasapiIndex < renderDevices.Length
                    ? renderDevices[speakerWasapiIndex]
                    : enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                Console.WriteLine($"[VoiceCallService.StartAudio] Динамик: {speakerDevice.FriendlyName}");

                // Воспроизведение: стерео буфер (для дублирования моно)
                _playbackBuffer = new BufferedWaveProvider(new WaveFormat(48000, 16, 2))
                {
                    BufferLength = 131072,
                    DiscardOnBufferOverflow = true
                };

                _playback = new WasapiOut(speakerDevice, AudioClientShareMode.Shared, false, 20);
                _playback.Init(_playbackBuffer);
                _playback.Play();
                Console.WriteLine("[VoiceCallService.StartAudio] Воспроизведение запущено (стерео 48 kHz)");

                // Захват: моно 48 kHz 16 bit
                var captureFormat = new WaveFormat(48000, 16, 1);
                _capture = new WasapiCapture(micDevice, false, 20);
                try { _capture.WaveFormat = captureFormat; } catch { /* устройство может не поддерживать, тогда оставим родной формат */ }
                Console.WriteLine($"[VoiceCallService.StartAudio] Формат захвата: {_capture.WaveFormat}");
                _capture.DataAvailable += (s, e) => OnCaptureData(e.Buffer, e.BytesRecorded, _capture.WaveFormat, pc);
                _capture.StartRecording();
                Console.WriteLine("[VoiceCallService.StartAudio] Захват запущен (моно)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VoiceCallService.StartAudio] Ошибка: {ex.Message}");
            }
        }

        private void OnCaptureData(byte[] buffer, int bytesRecorded, WaveFormat fmt, RTCPeerConnection pc)
        {
            if (!IsCallActive || !_hasNegotiatedFormat) return;
            try
            {
                short[] pcmMono = GetMono48kHzShorts(buffer, bytesRecorded, fmt);
                if (pcmMono.Length == 0) return;

                // Накапливаем сэмплы в очередь
                foreach (var sample in pcmMono)
                    _pcmBuffer.Enqueue(sample);

                // Кодируем пока есть хотя бы один полный фрейм
                while (_pcmBuffer.Count >= OPUS_FRAME_SIZE)
                {
                    short[] frame = new short[OPUS_FRAME_SIZE];
                    for (int i = 0; i < OPUS_FRAME_SIZE; i++)
                        frame[i] = _pcmBuffer.Dequeue();

                    byte[] encoded;
                    if (_opusEncoder != null)
                    {
                        var packet = new byte[4000];
                        int packetSize = _opusEncoder.Encode(frame.AsSpan(), OPUS_FRAME_SIZE, packet, packet.Length);
                        encoded = new byte[packetSize];
                        Array.Copy(packet, encoded, packetSize);
                    }
                    else
                    {
                        encoded = _audioEncoder.EncodeAudio(frame, _negotiatedFormat);
                    }

                    if (_isRelayMode)
                        OnRelayAudioReady?.Invoke(this, encoded);
                    else
                        pc.SendAudio((uint)OPUS_FRAME_SIZE, encoded);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VoiceCallService.OnCaptureData] Ошибка: {ex.Message}");
            }
        }

        private void PlayDecodedAudio(byte[] payload)
        {
            if (!_hasNegotiatedFormat) return;
            try
            {
                short[] pcmMono;
                if (_opusDecoder != null)
                {
                    pcmMono = new short[OPUS_FRAME_SIZE];
                    int samplesDecoded = _opusDecoder.Decode(payload, pcmMono.AsSpan(), OPUS_FRAME_SIZE, false);
                    if (samplesDecoded < OPUS_FRAME_SIZE)
                        pcmMono = pcmMono.Take(samplesDecoded).ToArray();
                }
                else
                {
                    pcmMono = _audioEncoder.DecodeAudio(payload, _negotiatedFormat);
                }

                if (pcmMono == null || pcmMono.Length == 0) return;

                // Дублируем моно в стерео (для обоих динамиков)
                short[] pcmStereo = new short[pcmMono.Length * 2];
                for (int i = 0; i < pcmMono.Length; i++)
                {
                    short sample = pcmMono[i];
                    pcmStereo[i * 2] = sample;
                    pcmStereo[i * 2 + 1] = sample;
                }

                var bytes = new byte[pcmStereo.Length * 2];
                Buffer.BlockCopy(pcmStereo, 0, bytes, 0, bytes.Length);
                _playbackBuffer?.AddSamples(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VoiceCallService.PlayDecodedAudio] Ошибка: {ex.Message}");
            }
        }

        // Быстрое преобразование любых входных данных в моно 48kHz 16bit
        private static short[] GetMono48kHzShorts(byte[] buffer, int bytesRecorded, WaveFormat fmt)
        {
            // Если формат уже подходящий — просто копируем
            if (fmt.SampleRate == 48000 && fmt.Channels == 1 && fmt.Encoding == WaveFormatEncoding.Pcm && fmt.BitsPerSample == 16)
            {
                short[] samples = new short[bytesRecorded / 2];
                Buffer.BlockCopy(buffer, 0, samples, 0, bytesRecorded);
                return samples;
            }

            // Иначе используем MediaFoundationResampler (быстро, качественно)
            using (var rawStream = new RawSourceWaveStream(buffer, 0, bytesRecorded, fmt))
            using (var resampler = new MediaFoundationResampler(rawStream, new WaveFormat(48000, 16, 1)))
            {
                resampler.ResamplerQuality = 60;
                var outStream = new MemoryStream();
                byte[] resampledData = new byte[8192];
                int bytesRead;
                while ((bytesRead = resampler.Read(resampledData, 0, resampledData.Length)) > 0)
                {
                    outStream.Write(resampledData, 0, bytesRead);
                }
                byte[] finalBytes = outStream.ToArray();
                short[] finalSamples = new short[finalBytes.Length / 2];
                Buffer.BlockCopy(finalBytes, 0, finalSamples, 0, finalBytes.Length);
                return finalSamples;
            }
        }

        private void FlushPendingCandidates()
        {
            Console.WriteLine($"[VoiceCallService.FlushPendingCandidates] Применяем {_pendingCandidates.Count} кандидатов");
            foreach (var c in _pendingCandidates)
                _peerConnection?.addIceCandidate(c);
            _pendingCandidates.Clear();
        }
    }
}