using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Sup.Models
{
    public class PagedResponse<T>
    {
        [JsonPropertyName("content")]
        public List<T> Content { get; set; } = new();

        [JsonPropertyName("totalElements")]
        public int TotalElements { get; set; }

        [JsonPropertyName("totalPages")]
        public int TotalPages { get; set; }

        [JsonPropertyName("size")]
        public int Size { get; set; }

        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonPropertyName("empty")]
        public bool Empty { get; set; }
    }
}
