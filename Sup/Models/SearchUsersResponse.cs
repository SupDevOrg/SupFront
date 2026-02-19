using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Sup.Models
{
    public class SearchUsersResponse
    {
        [JsonPropertyName("users")]
        public List<UserDto> Users { get; set; } = new();

        [JsonPropertyName("currentPage")]
        public int CurrentPage { get; set; }

        [JsonPropertyName("totalItems")]
        public int TotalItems { get; set; }

        [JsonPropertyName("totalPages")]
        public int TotalPages { get; set; }
    }
}