using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Sup.Models
{
    public class NotificationDto
    {
        [JsonPropertyName("recipient_id")]
        public long RecipientId { get; set; }

        [JsonPropertyName("sender_id")]
        public long SenderId { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("payload")]
        public Dictionary<string, string> Payload { get; set; } = new();

        [JsonPropertyName("created_at_unix_ms")]
        public long CreatedAtUnixMs { get; set; }
    }
}