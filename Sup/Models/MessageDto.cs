using System;
using System.Text.Json.Serialization;

namespace Sup.Models
{
    public class MessageDto
    {
        [JsonPropertyName("id")]
        public uint Id { get; set; }

        [JsonPropertyName("message_id")]
        public uint MessageId
        {
            set => Id = value;
        }

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("sender_id")]
        public uint SenderId { get; set; }

        [JsonPropertyName("chat_id")]
        public uint ChatId { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }
    }
}
