using System.Text.Json.Serialization;

namespace CollectIQ.Models.SportsCardsPro
{
    /// <summary>
    /// Represents the fields returned by SportsCardsPro Prices API (/api/product) and CSV.
    /// Notes:
    /// - Prices are encoded as integer pennies (e.g., $17.32 => 1732).
    /// - Dates are encoded as YYYY-MM-DD strings.
    /// </summary>
    public class SportCardsProPricesSnapshot
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("product-name")]
        public string? ProductName { get; set; }

        [JsonPropertyName("console-name")]
        public string? ConsoleName { get; set; }

        [JsonPropertyName("genre")]
        public string? Genre { get; set; }

        [JsonPropertyName("release-date")]
        public string? ReleaseDate { get; set; }

        [JsonPropertyName("sales-volume")]
        public int? SalesVolume { get; set; }

        // Value fields (pennies)
        [JsonPropertyName("loose-price")]
        public long? LoosePrice { get; set; }

        [JsonPropertyName("new-price")]
        public long? NewPrice { get; set; }

        [JsonPropertyName("graded-price")]
        public long? GradedPrice { get; set; }

        [JsonPropertyName("cib-price")]
        public long? CibPrice { get; set; }

        [JsonPropertyName("bgs-10-price")]
        public long? Bgs10Price { get; set; }

        [JsonPropertyName("manual-only-price")]
        public long? ManualOnlyPrice { get; set; }

        [JsonPropertyName("box-only-price")]
        public long? BoxOnlyPrice { get; set; }

        [JsonPropertyName("condition-17-price")]
        public long? Condition17Price { get; set; }

        [JsonPropertyName("condition-18-price")]
        public long? Condition18Price { get; set; }

        // Retail recommended buy/sell (pennies)
        [JsonPropertyName("retail-loose-buy")]
        public long? RetailLooseBuy { get; set; }

        [JsonPropertyName("retail-loose-sell")]
        public long? RetailLooseSell { get; set; }

        [JsonPropertyName("retail-new-buy")]
        public long? RetailNewBuy { get; set; }

        [JsonPropertyName("retail-new-sell")]
        public long? RetailNewSell { get; set; }

        [JsonPropertyName("retail-cib-buy")]
        public long? RetailCibBuy { get; set; }

        [JsonPropertyName("retail-cib-sell")]
        public long? RetailCibSell { get; set; }

        /// <summary>
        /// True if the API returned "success" (case-insensitive).
        /// </summary>
        [JsonIgnore]
        public bool IsSuccess => string.Equals(Status, "success", System.StringComparison.OrdinalIgnoreCase);
    }
}
