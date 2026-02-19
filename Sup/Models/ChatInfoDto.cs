using System;
using System.Text.Json.Serialization;

namespace Sup.Models
{
    public class ChatInfoDto
    {
        [JsonPropertyName("id")]
        public uint Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("last_message")]
        public string LastMessage { get; set; } = string.Empty;

        [JsonPropertyName("last_message_time")]
        public DateTime LastMessageTime { get; set; }
    }
}
