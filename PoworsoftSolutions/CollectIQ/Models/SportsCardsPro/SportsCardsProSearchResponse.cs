using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CollectIQ.Models.SportsCardsPro
{
    public class SportsCardsProSearchResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("products")]
        public List<SportsCardsProSearchResult> Products { get; set; }

        [JsonIgnore]
        public bool IsSuccess => string.Equals(Status, "success", System.StringComparison.OrdinalIgnoreCase);
    }

}