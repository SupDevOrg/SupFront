using System.Text.Json.Serialization;

namespace Sup.Models
{
    public class ChatParticipantDto
    {
        [JsonPropertyName("id")]
        public uint Id { get; set; }

        [JsonPropertyName("user_id")]
        public uint UserId
        {
            set => Id = value;
        }

        [JsonPropertyName("userId")]
        public uint UserIdCamel
        {
            set => Id = value;
        }

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name
        {
            set
            {
                if (string.IsNullOrWhiteSpace(Username))
                    Username = value ?? string.Empty;
            }
        }

        public string DisplayName => !string.IsNullOrWhiteSpace(Username)
            ? Username
            : $"Пользователь {Id}";
    }
}
