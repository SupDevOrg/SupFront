using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Sup.Models
{
    public class RequestsListResponse
    {
        [JsonPropertyName("requests")]
        public List<FriendshipDto> Requests { get; set; } = new();

        [JsonPropertyName("currentPage")]
        public int CurrentPage { get; set; }

        [JsonPropertyName("totalItems")]
        public int TotalItems { get; set; }

        [JsonPropertyName("totalPages")]
        public int TotalPages { get; set; }
    }
}
