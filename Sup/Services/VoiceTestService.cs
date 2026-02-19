using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Linq;

namespace Sup.Services
{
    public class VoiceTestService : IVoiceTestService
    {
        private WasapiCapture? _audioCapture;
        private WasapiOut? _audioPlayback;
        private bool _isActive = false;
        
        private double _currentVolume = 0;
        private double _peakVolume = 0;
        private DateTime _lastPeakTime = DateTime.Now;

        public bool IsActive => _isActive;

        public event EventHandler<AudioLevelChangedEventArgs>? OnAudioLevelChanged;

        public void Start(int microphoneIndex, int audioOutputIndex)
        {
            if (_isActive)
                return;

            try
            {
                var enumerator = new MMDeviceEnumerator();
                var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToArray();
                var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToArray();

                if (microphoneIndex < 0 || microphoneIndex >= captureDevices.Length ||
                    audioOutputIndex < 0 || audioOutputIndex >= renderDevices.Length)
                    return;

                var captureDevice = captureDevices[microphoneIndex];
                var renderDevice = renderDevices[audioOutputIndex];

                _audioCapture = new WasapiCapture(captureDevice);
                var waveProvider = new BufferedWaveProvider(_audioCapture.WaveFormat);
                waveProvider.BufferLength = 65536;
                waveProvider.DiscardOnBufferOverflow = true;

                _audioPlayback = new WasapiOut(renderDevice, AudioClientShareMode.Shared, false, 10);
                _audioPlayback.Init(waveProvider);

                _currentVolume = 0;
                _peakVolume = 0;
                _lastPeakTime = DateTime.Now;

                _audioCapture.DataAvailable += (s, e) => ProcessAudioData(e.Buffer, e.BytesRecorded, waveProvider);
                _audioCapture.RecordingStopped += (s, e) =>
                {
                    if (e.Exception != null)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            OnAudioLevelChanged?.Invoke(this, new AudioLevelChangedEventArgs(0, 0));
                        });
                    }
                };

                _audioPlayback.Play();
                _audioCapture.StartRecording();
                _isActive = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VoiceTestService] Ошибка запуска теста голоса: {ex.Message}");
                Stop();
            }
        }

        private void ProcessAudioData(byte[] buffer, int bytesRecorded, BufferedWaveProvider waveProvider)
        {
            waveProvider.AddSamples(buffer, 0, bytesRecorded);

            if (_audioCapture == null) return;

            int bytesPerSample = _audioCapture.WaveFormat.BitsPerSample / 8;
            int sampleCount = bytesRecorded / bytesPerSample;

            if (sampleCount == 0) return;

            double sumSquares = 0;
            double maxAmplitude = 0;

            if (_audioCapture.WaveFormat.Encoding == WaveFormatEncoding.Pcm)
            {
                if (_audioCapture.WaveFormat.BitsPerSample == 16)
                {
                    for (int i = 0; i < bytesRecorded; i += 2)
                    {
                        short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
                        double normalizedSample = sample / 32768.0;

                        sumSquares += normalizedSample * normalizedSample;
                        maxAmplitude = Math.Max(maxAmplitude, Math.Abs(normalizedSample));
                    }
                }
                else if (_audioCapture.WaveFormat.BitsPerSample == 32)
                {
                    for (int i = 0; i < bytesRecorded; i += 4)
                    {
                        int sample = BitConverter.ToInt32(buffer, i);
                        double normalizedSample = sample / 2147483648.0;

                        sumSquares += normalizedSample * normalizedSample;
                        maxAmplitude = Math.Max(maxAmplitude, Math.Abs(normalizedSample));
                    }
                }
                else if (_audioCapture.WaveFormat.BitsPerSample == 8)
                {
                    for (int i = 0; i < bytesRecorded; i++)
                    {
                        double normalizedSample = (buffer[i] - 128) / 128.0;
                        sumSquares += normalizedSample * normalizedSample;
                        maxAmplitude = Math.Max(maxAmplitude, Math.Abs(normalizedSample));
                    }
                }
            }
            else if (_audioCapture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                for (int i = 0; i < bytesRecorded; i += 4)
                {
                    float sample = BitConverter.ToSingle(buffer, i);
                    sumSquares += sample * sample;
                    maxAmplitude = Math.Max(maxAmplitude, Math.Abs(sample));
                }
            }

            double rms = Math.Sqrt(sumSquares / sampleCount);
            double db = rms > 0 ? 20.0 * Math.Log10(rms) : -60;
            double normalizedVolume = Math.Max(0, Math.Min(100, (db + 60) * 100 / 60));

            double attackCoeff = 0.3;
            double releaseCoeff = 0.1;

            if (normalizedVolume > _currentVolume)
                _currentVolume = _currentVolume * (1 - attackCoeff) + normalizedVolume * attackCoeff;
            else
                _currentVolume = _currentVolume * (1 - releaseCoeff) + normalizedVolume * releaseCoeff;

            if (normalizedVolume > _peakVolume)
            {
                _peakVolume = normalizedVolume;
                _lastPeakTime = DateTime.Now;
            }
            else if ((DateTime.Now - _lastPeakTime).TotalMilliseconds > 1500)
            {
                _peakVolume *= 0.95;
                if (_peakVolume < _currentVolume)
                    _peakVolume = _currentVolume;
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                OnAudioLevelChanged?.Invoke(this, new AudioLevelChangedEventArgs(_currentVolume, _peakVolume));
            });
        }

        public void Stop()
        {
            try
            {
                _audioCapture?.StopRecording();
                _audioCapture?.Dispose();
                _audioCapture = null;

                _audioPlayback?.Stop();
                _audioPlayback?.Dispose();
                _audioPlayback = null;

                _currentVolume = 0;
                _peakVolume = 0;
                _isActive = false;

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    OnAudioLevelChanged?.Invoke(this, new AudioLevelChangedEventArgs(0, 0));
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VoiceTestService] Ошибка при остановке теста голоса: {ex.Message}");
            }
        }
    }

}