namespace Sup.Models
{
    public class MessageListItem
    {
        public uint Id { get; set; }
        public uint ChatId { get; set; }
        public uint SenderId { get; set; }
        public string Content { get; set; } = string.Empty;
        public bool IsOwnMessage { get; set; }
        public string Time { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
    }
}