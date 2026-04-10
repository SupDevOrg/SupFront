using System;
using System.Threading.Tasks;

namespace Sup.Services
{
    public class SignalingCandidateEventArgs : EventArgs
    {
        public string Candidate { get; init; } = string.Empty;
        public string SdpMid { get; init; } = string.Empty;
        public int SdpMLineIndex { get; init; }
    }

    // Сервис WebSocket-сигнализации для WebRTC звонков
    public interface ISignalingService
    {
        bool IsConnected { get; }

        Task ConnectAsync(string roomId, string userId);
        Task SendOfferAsync(string sdp);
        Task SendAnswerAsync(string sdp);
        Task SendIceCandidateAsync(string candidate, string sdpMid, int sdpMLineIndex);
        Task LeaveAsync();
        void Disconnect();
        Task SendAudioAsync(byte[] encodedG711);

        event EventHandler<string>? OnOfferReceived;
        event EventHandler<string>? OnAnswerReceived;
        event EventHandler<SignalingCandidateEventArgs>? OnIceCandidateReceived;
        event EventHandler? OnPeerJoined;
        event EventHandler? OnPeerLeft;
        event EventHandler<byte[]>? OnAudioReceived;
    }
}
