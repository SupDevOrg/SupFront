using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Sup.Models
{
    public class AddUsersToChatRequest
    {
        [JsonPropertyName("user_ids")]
        public List<uint> UserIds { get; set; } = new();

        public static AddUsersToChatRequest FromIds(IEnumerable<uint> userIds)
        {
            return new AddUsersToChatRequest
            {
                UserIds = userIds
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList()
            };
        }
    }
}
