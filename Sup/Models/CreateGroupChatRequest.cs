using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Sup.Models
{
    public class CreateGroupChatRequest
    {
        [JsonPropertyName("user_id")]
        public List<ChatUserReferenceDto> UserId { get; set; } = new();

        public static CreateGroupChatRequest FromIds(IEnumerable<uint> userIds)
        {
            return new CreateGroupChatRequest
            {
                UserId = userIds
                    .Where(id => id > 0)
                    .Distinct()
                    .Select(id => new ChatUserReferenceDto { Id = id })
                    .ToList()
            };
        }
    }
}
