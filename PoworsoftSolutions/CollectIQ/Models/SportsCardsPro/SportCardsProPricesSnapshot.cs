using System.Text.Json.Serialization;

namespace CollectIQ.Models.SportsCardsPro
{
    // This is the /api/product response.
    // Keep it tight and aligned to real JSON keys.
    public class SportCardsProPricesSnapshot
    {
        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("product-name")]
        public string ProductName { get; set; }

        [JsonPropertyName("console-name")]
        public string ConsoleName { get; set; }

        [JsonPropertyName("genre")]
        public string Genre { get; set; }

        [JsonPropertyName("release-date")]
        public string ReleaseDate { get; set; }

        [JsonPropertyName("sales-volume")]
        [JsonConverter(typeof(NullableIntFromStringOrNumberConverter))]
        public int? SalesVolume { get; set; }

        // These show up as 0 in your JSON sometimes.
        [JsonPropertyName("gamestop-price")]
        public long? GameStopPrice { get; set; }

        [JsonPropertyName("gamestop-trade-price")]
        public long? GameStopTradePrice { get; set; }

        // Prices (pennies)
        [JsonPropertyName("loose-price")]
        public long? LoosePrice { get; set; }

        [JsonPropertyName("new-price")]
        public long? NewPrice { get; set; }

        [JsonPropertyName("cib-price")]
        public long? CibPrice { get; set; }

        // Retail buy/sell (pennies)
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

        // Optional extra keys that sometimes exist for other items.
        [JsonPropertyName("graded-price")]
        public long? GradedPrice { get; set; }

        [JsonPropertyName("box-only-price")]
        public long? BoxOnlyPrice { get; set; }

        [JsonPropertyName("manual-only-price")]
        public long? ManualOnlyPrice { get; set; }

        [JsonPropertyName("bgs-10-price")]
        public long? Bgs10Price { get; set; }


        [JsonPropertyName("condition-9-price")]
        public long? Condition9Price { get; set; }

        [JsonPropertyName("condition-10-price")]
        public long? Condition10Price { get; set; }

        [JsonPropertyName("condition-13-price")]
        public long? Condition13Price { get; set; }

        [JsonPropertyName("condition-14-price")]
        public long? Condition14Price { get; set; }

        [JsonPropertyName("condition-15-price")]
        public long? Condition15Price { get; set; }

        [JsonPropertyName("condition-16-price")]
        public long? Condition16Price { get; set; }

        [JsonPropertyName("condition-17-price")]
        public long? Condition17Price { get; set; }

        [JsonPropertyName("condition-18-price")]
        public long? Condition18Price { get; set; }


        [JsonPropertyName("condition-19-price")]
        public long? Condition19Price { get; set; }

        [JsonPropertyName("condition-20-price")]
        public long? Condition20Price { get; set; }

        [JsonPropertyName("condition-21-price")]
        public long? Condition21Price { get; set; }

        [JsonPropertyName("condition-22-price")]
        public long? Condition22Price { get; set; }

        [JsonIgnore]
        public bool IsSuccess => string.Equals(Status, "success", System.StringComparison.OrdinalIgnoreCase);
    }

}