namespace Sup.Models
{
    public class AvatarUploadUrlResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("uploadUrl")]
        public string UploadUrl { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("avatarUrl")]
        public string AvatarUrl { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("objectKey")]
        public string ObjectKey { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("expiresInSeconds")]
        public int ExpiresInSeconds { get; set; }
    }
}
