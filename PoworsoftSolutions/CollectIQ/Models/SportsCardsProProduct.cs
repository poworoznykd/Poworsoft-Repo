using System.Text.Json.Serialization;

namespace CollectIQ.Models.SportsCardsPro
{
    public class SportsCardsProProduct
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
        public string SalesVolumeRaw { get; set; }

        public int SalesVolume
        {
            get
            {
                if (int.TryParse(SalesVolumeRaw, out int value))
                    return value;

                return 0;
            }
        }

        // Prices (pennies)
        [JsonPropertyName("loose-price")]
        public long? LoosePrice { get; set; }

        [JsonPropertyName("new-price")]
        public long? NewPrice { get; set; }

        [JsonPropertyName("cib-price")]
        public long? CibPrice { get; set; }

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

        [JsonPropertyName("gamestop-price")]
        public long? GameStopPrice { get; set; }

        [JsonPropertyName("gamestop-trade-price")]
        public long? GameStopTradePrice { get; set; }

        public decimal? LoosePriceDollars => LoosePrice.HasValue ? LoosePrice.Value / 100m : null;
        public decimal? NewPriceDollars => NewPrice.HasValue ? NewPrice.Value / 100m : null;
        public decimal? CibPriceDollars => CibPrice.HasValue ? CibPrice.Value / 100m : null;
    }
}