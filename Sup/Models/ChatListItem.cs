namespace Sup.Models
{
    public class ChatListItem
    {
        public uint ChatId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string LastMessage { get; set; } = string.Empty;
        public string LastMessageTime { get; set; } = string.Empty;
    }
}