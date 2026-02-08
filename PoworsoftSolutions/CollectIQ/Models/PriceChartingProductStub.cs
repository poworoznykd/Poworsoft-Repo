//
//  FILE            : PriceChartingProductStub.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2026-02-08
//  DESCRIPTION     :
//      Lightweight model for PriceCharting /api/products (search results list).
//      This is intentionally small so we can fetch a list, pick the best match,
//      then fetch /api/product by ID for full pricing.
//

using System.Text.Json.Serialization;

namespace CollectIQ.Models
{
    public sealed class PriceChartingProductStub
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("product-name")]
        public string? ProductName { get; set; }

        [JsonPropertyName("console-name")]
        public string? ConsoleName { get; set; }
    }
}
