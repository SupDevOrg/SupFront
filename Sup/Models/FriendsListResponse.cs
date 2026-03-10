using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Sup.Models
{
    public class FriendsListResponse
    {
        [JsonPropertyName("friends")]
        public List<FriendshipDto> Friends { get; set; } = new();

        [JsonPropertyName("requests")]
        public List<FriendshipDto>? Requests { get; set; }

        [JsonPropertyName("currentPage")]
        public int CurrentPage { get; set; }

        [JsonPropertyName("totalItems")]
        public int TotalItems { get; set; }

        [JsonPropertyName("totalPages")]
        public int TotalPages { get; set; }
    }
}
