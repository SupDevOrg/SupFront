using System;
using System.Threading.Tasks;

namespace Sup.Services
{
    // Сервис WebRTC голосового звонка
    public interface IVoiceCallService : IDisposable
    {
        bool IsCallActive { get; }

        Task<bool> InitiateCallAsync(int micWasapiIndex, int speakerWasapiIndex);
        Task HandleOfferAsync(string sdp, int micWasapiIndex, int speakerWasapiIndex);
        Task HandleAnswerAsync(string sdp);
        Task HandleIceCandidateAsync(string candidate, string sdpMid, int sdpMLineIndex);
        Task HangUpAsync();
        void ReceiveRelayAudio(byte[] encodedG711);

        event EventHandler? OnCallConnected;
        event EventHandler? OnCallEnded;
        event EventHandler<string>? OnOfferCreated;
        event EventHandler<string>? OnAnswerCreated;
        event EventHandler<SignalingCandidateEventArgs>? OnIceCandidateReady;
        event EventHandler<byte[]>? OnRelayAudioReady;
    }
}
