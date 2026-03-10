using System.Text.Json.Serialization;

namespace Sup.Models
{
    public class FriendshipDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;
    }
}
