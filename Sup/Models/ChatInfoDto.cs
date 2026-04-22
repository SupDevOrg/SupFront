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

        [JsonPropertyName("chat_name")]
        public string ChatName
        {
            set => Name = value ?? string.Empty;
        }

        [JsonPropertyName("last_message")]
        public string LastMessage { get; set; } = string.Empty;

        [JsonPropertyName("last_message_time")]
        public DateTime LastMessageTime { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt
        {
            set
            {
                if (LastMessageTime == default)
                    LastMessageTime = value;
            }
        }

        [JsonPropertyName("is_group")]
        public bool IsGroup { get; set; }
    }
}
