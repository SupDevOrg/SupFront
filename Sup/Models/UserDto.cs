using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sup.Models
{
    public class UserDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public int Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("phone")]
        public string Phone { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("emailVerification")]
        public bool EmailVerification { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("avatarURL")]
        public string AvatarUrl { get; set; } = string.Empty;
    }
}
