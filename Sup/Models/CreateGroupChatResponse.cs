using System.Text.Json.Serialization;

namespace Sup.Models
{
    public class CreateGroupChatResponse
    {
        [JsonPropertyName("chat_id")]
        public ChatInfoDto? ChatId { get; set; }
    }
}
