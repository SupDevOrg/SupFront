using System;
using System.Threading.Tasks;

namespace Sup.Services
{
    public interface IWebSocketService
    {
        Task<bool> OpenAsync(uint chatId, uint currentUserId, uint otherUserId);
        Task<bool> SendAsync(string message);
        void Close();
        
        event EventHandler<WebSocketMessageEventArgs>? OnMessageReceived;
        event EventHandler<Exception>? OnError;
    }

    public class WebSocketMessageEventArgs : EventArgs
    {
        public uint ChatId { get; set; }
        public uint SenderUId { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}