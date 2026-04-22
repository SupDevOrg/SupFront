using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Sup.Models
{
    public class GetChatMembersResponse
    {
        [JsonPropertyName("chat_id")]
        public uint ChatId { get; set; }

        [JsonPropertyName("members")]
        public List<ChatParticipantDto> Members { get; set; } = new();
    }
}
