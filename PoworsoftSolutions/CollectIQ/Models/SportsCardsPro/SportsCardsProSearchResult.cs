using System.Text.Json.Serialization;

namespace CollectIQ.Models.SportsCardsPro
{
    // One item inside the /api/products "products" array.
    public class SportsCardsProSearchResult
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("product-name")]
        public string ProductName { get; set; }

        [JsonPropertyName("console-name")]
        public string ConsoleName { get; set; }

        [JsonPropertyName("genre")]
        public string Genre { get; set; }
    }

}