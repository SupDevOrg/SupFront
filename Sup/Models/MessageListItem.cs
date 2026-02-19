namespace Sup.Models
{
    public class MessageListItem
    {
        public string Content { get; set; } = string.Empty;
        public bool IsOwnMessage { get; set; }
        public string Time { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
    }
}