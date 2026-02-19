using System;
using System.Threading.Tasks;

namespace Sup.Services
{
    public interface IVoiceTestService
    {
        void Start(int microphoneIndex, int audioOutputIndex);
        void Stop();
        bool IsActive { get; }
        event EventHandler<AudioLevelChangedEventArgs>? OnAudioLevelChanged;
    }

    public class AudioLevelChangedEventArgs : EventArgs
    {
        public double CurrentVolume { get; }
        public double PeakVolume { get; }

        public AudioLevelChangedEventArgs(double currentVolume, double peakVolume)
        {
            CurrentVolume = currentVolume;
            PeakVolume = peakVolume;
        }
    }
}