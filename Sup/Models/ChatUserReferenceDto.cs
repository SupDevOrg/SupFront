using System.Text.Json.Serialization;

namespace Sup.Models
{
    public class ChatUserReferenceDto
    {
        [JsonPropertyName("id")]
        public uint Id { get; set; }
    }
}
