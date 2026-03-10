namespace Sup.Models
{
    public class SearchResultItemDto
    {
        public uint Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FriendshipStatus { get; set; } = "none";
        public bool IsFriend { get; set; }
        public bool HasIncomingRequest { get; set; }
        public bool HasOutgoingRequest { get; set; }

        // Свойство для определения видимости кнопки "Добавить в друзья"
        public bool CanAddFriend => !IsFriend && !HasIncomingRequest && !HasOutgoingRequest;
    }
}
