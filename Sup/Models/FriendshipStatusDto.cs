using System.Text.Json.Serialization;

namespace Sup.Models
{
    public class FriendshipStatusDto
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("isOutgoingRequest")]
        public bool IsOutgoingRequest { get; set; }
    }
}
