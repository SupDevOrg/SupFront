using System.Text.Json.Serialization;

namespace Sup.Models
{
    public class FriendRequestDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("requesterId")]
        public int RequesterId { get; set; }

        [JsonPropertyName("addresseeId")]
        public int AddresseeId { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("createdAt")]
        public string CreatedAt { get; set; } = string.Empty;

        [JsonPropertyName("updatedAt")]
        public string UpdatedAt { get; set; } = string.Empty;
    }
}
