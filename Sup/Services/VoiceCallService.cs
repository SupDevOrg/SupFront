using Concentus;
using Concentus.Enums;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Sup.Services
{
    public class VoiceCallService : IVoiceCallService
    {
        private WasapiCapture? _capture;
        private WasapiOut? _playback;
        private BufferedWaveProvider? _playbackBuffer;
        private IOpusEncoder? _opusEncoder;
        private IOpusDecoder? _opusDecoder;
        private const int OPUS_FRAME_SIZE = 960; // 20ms при 48kHz
        private readonly Queue<short> _pcmBuffer = new();
        private bool _isCallActive;

        public bool IsCallActive => _isCallActive;

        public event EventHandler? OnCallConnected;
        public event EventHandler? OnCallEnded;
        public event EventHandler<string>? OnOfferCreated;
        public event EventHandler<string>? OnAnswerCreated;
        public event EventHandler<SignalingCandidateEventArgs>? OnIceCandidateReady;
        public event EventHandler<byte[]>? OnRelayAudioReady;

        public async Task<bool> InitiateCallAsync(int micWasapiIndex, int speakerWasapiIndex)
        {
            Console.WriteLine($"[VoiceCallService.InitiateCallAsync] mic={micWasapiIndex} speaker={speakerWasapiIndex} (только relay)");
            try
            {
                InitializeAudio(micWasapiIndex, speakerWasapiIndex);
                _isCallActive = true;
                // Имитация создания offer – отправляем пустой SDP для сигнализации начала звонка
                OnOfferCreated?.Invoke(this, "relay");
                OnCallConnected?.Invoke(this, EventArgs.Empty);
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
            Console.WriteLine($"[VoiceCallService.HandleOfferAsync] Принимаем relay-звонок");
            try
            {
                InitializeAudio(micWasapiIndex, speakerWasapiIndex);
                _isCallActive = true;
                // Отправляем "answer" для подтверждения приёма
                OnAnswerCreated?.Invoke(this, "relay");
                OnCallConnected?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VoiceCallService.HandleOfferAsync] Ошибка: {ex.Message}");
            }
        }

        public Task HandleAnswerAsync(string sdp)
        {
            // В relay-режиме answer просто подтверждает готовность собеседника
            Console.WriteLine("[VoiceCallService.HandleAnswerAsync] Получен answer (relay)");
            OnCallConnected?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task HandleIceCandidateAsync(string candidate, string sdpMid, int sdpMLineIndex)
        {
            // В relay-режиме ICE не используется
            return Task.CompletedTask;
        }

        public async Task HangUpAsync()
        {
            if (!_isCallActive)
                return;

            Console.WriteLine("[VoiceCallService.HangUpAsync] Завершение звонка");
            _isCallActive = false;
            CleanupAudio();
            OnCallEnded?.Invoke(this, EventArgs.Empty);
        }

        public void ReceiveRelayAudio(byte[] encodedOpus)
        {
            if (!_isCallActive || _opusDecoder == null)
                return;

            try
            {
                short[] pcmMono = new short[OPUS_FRAME_SIZE];
                int samplesDecoded = _opusDecoder.Decode(encodedOpus, pcmMono.AsSpan(), OPUS_FRAME_SIZE, false);
                if (samplesDecoded <= 0) return;

                // Дублируем моно в стерео
                short[] pcmStereo = new short[samplesDecoded * 2];
                for (int i = 0; i < samplesDecoded; i++)
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
                Console.WriteLine($"[VoiceCallService.ReceiveRelayAudio] Ошибка: {ex.Message}");
            }
        }

        public void Dispose()
        {
            HangUpAsync().GetAwaiter().GetResult();
        }

        private void InitializeAudio(int micWasapiIndex, int speakerWasapiIndex)
        {
            CleanupAudio();

            var enumerator = new MMDeviceEnumerator();
            var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToArray();
            var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToArray();

            var micDevice = micWasapiIndex >= 0 && micWasapiIndex < captureDevices.Length
                ? captureDevices[micWasapiIndex]
                : enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            var speakerDevice = speakerWasapiIndex >= 0 && speakerWasapiIndex < renderDevices.Length
                ? renderDevices[speakerWasapiIndex]
                : enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            // Инициализация Opus кодеков
            _opusEncoder = OpusCodecFactory.CreateEncoder(48000, 1, OpusApplication.OPUS_APPLICATION_VOIP);
            _opusEncoder.Bitrate = 64000;
            _opusDecoder = OpusCodecFactory.CreateDecoder(48000, 1);

            // Воспроизведение
            _playbackBuffer = new BufferedWaveProvider(new WaveFormat(48000, 16, 2))
            {
                BufferLength = 131072,
                DiscardOnBufferOverflow = true
            };
            _playback = new WasapiOut(speakerDevice, AudioClientShareMode.Shared, false, 20);
            _playback.Init(_playbackBuffer);
            _playback.Play();

            // Захват
            var captureFormat = new WaveFormat(48000, 16, 1);
            _capture = new WasapiCapture(micDevice, false, 20);
            try { _capture.WaveFormat = captureFormat; } catch { }
            _capture.DataAvailable += OnCaptureData;
            _capture.StartRecording();

            Console.WriteLine($"[VoiceCallService] Аудио запущено: mic={micDevice.FriendlyName}, speaker={speakerDevice.FriendlyName}");
        }

        private void OnCaptureData(object? sender, WaveInEventArgs e)
        {
            if (!_isCallActive || _opusEncoder == null)
                return;

            try
            {
                short[] pcmMono = GetMono48kHzShorts(e.Buffer, e.BytesRecorded, _capture!.WaveFormat);
                foreach (var sample in pcmMono)
                    _pcmBuffer.Enqueue(sample);

                while (_pcmBuffer.Count >= OPUS_FRAME_SIZE)
                {
                    short[] frame = new short[OPUS_FRAME_SIZE];
                    for (int i = 0; i < OPUS_FRAME_SIZE; i++)
                        frame[i] = _pcmBuffer.Dequeue();

                    byte[] encoded = new byte[4000];
                    int packetSize = _opusEncoder.Encode(frame.AsSpan(), OPUS_FRAME_SIZE, encoded, encoded.Length);
                    byte[] packet = new byte[packetSize];
                    Array.Copy(encoded, packet, packetSize);

                    OnRelayAudioReady?.Invoke(this, packet);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VoiceCallService.OnCaptureData] Ошибка: {ex.Message}");
            }
        }

        private static short[] GetMono48kHzShorts(byte[] buffer, int bytesRecorded, WaveFormat fmt)
        {
            if (fmt.SampleRate == 48000 && fmt.Channels == 1 && fmt.Encoding == WaveFormatEncoding.Pcm && fmt.BitsPerSample == 16)
            {
                short[] samples = new short[bytesRecorded / 2];
                Buffer.BlockCopy(buffer, 0, samples, 0, bytesRecorded);
                return samples;
            }

            using var rawStream = new RawSourceWaveStream(buffer, 0, bytesRecorded, fmt);
            using var resampler = new MediaFoundationResampler(rawStream, new WaveFormat(48000, 16, 1))
            {
                ResamplerQuality = 60
            };
            var outStream = new MemoryStream();
            byte[] temp = new byte[8192];
            int read;
            while ((read = resampler.Read(temp, 0, temp.Length)) > 0)
                outStream.Write(temp, 0, read);
            byte[] finalBytes = outStream.ToArray();
            short[] finalSamples = new short[finalBytes.Length / 2];
            Buffer.BlockCopy(finalBytes, 0, finalSamples, 0, finalBytes.Length);
            return finalSamples;
        }

        private void CleanupAudio()
        {
            try { _capture?.StopRecording(); } catch { }
            try { _capture?.Dispose(); } catch { }
            _capture = null;

            try { _playback?.Stop(); } catch { }
            try { _playback?.Dispose(); } catch { }
            _playback = null;
            _playbackBuffer = null;

            _opusEncoder = null;
            _opusDecoder = null;
            _pcmBuffer.Clear();
        }
    }
}