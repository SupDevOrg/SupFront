using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Sup.Models
{
    public class MessagesResponse
    {
        [JsonPropertyName("messages")]
        public List<MessageDto> Messages { get; set; } = new();
    }
}