//
//  FILE            : PriceChartingProduct.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2026-02-08
//  DESCRIPTION     :
//      Strongly-typed model for PriceCharting /api/product responses.
//      Notes:
//      - PriceCharting returns prices as integer pennies/cents (e.g., 1732 = $17.32).
//      - JSON keys use hyphenated names (e.g., "product-name"), so we map them explicitly.
//      - Keep this model in CollectIQ.Models ONLY (do NOT duplicate in Services), otherwise you
//        will get "ambiguous reference" compiler errors.
//

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a PriceCharting product response (/api/product).
    /// </summary>
    public sealed class PriceChartingProduct
    {
        // Core identity
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("product-name")]
        public string? ProductName { get; set; }

        [JsonPropertyName("console-name")]
        public string? ConsoleName { get; set; }

        // Prices (USD dollars) - we convert from pennies/cents in FromJson(...)
        // Cards mapping (per PriceCharting key descriptions):
        //   loose-price        = RAW / ungraded
        //   cib-price          = graded 7 / 7.5 (often PSA 7 for cards)
        //   new-price          = graded 8
        //   graded-price       = graded 9
        //   box-only-price     = graded 9.5
        //   manual-only-price  = PSA 10 (cards) / best "10" price field in many guides
        //   bgs-10-price       = BGS 10
        //   condition-17-price = CGC 10
        //   condition-18-price = SGC 10
        public double? LoosePrice { get; set; }
        public double? CibPrice { get; set; }
        public double? NewPrice { get; set; }
        public double? GradedPrice { get; set; }
        public double? BoxOnlyPrice { get; set; }
        public double? ManualOnlyPrice { get; set; }
        public double? Bgs10Price { get; set; }
        public double? Condition17Price { get; set; }
        public double? Condition18Price { get; set; }

        public int? SalesVolume { get; set; }

        /// <summary>
        /// Parses a /api/product JSON response and converts all price fields from pennies to USD dollars.
        /// Returns null if the response is not a success payload or cannot be parsed.
        /// </summary>
        public static PriceChartingProduct? FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);

                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                JsonElement root = doc.RootElement;

                // API returns { "status": "success", ... } or { "status": "error", "error-message": "..." }
                string? status = GetString(root, "status");
                if (!string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                PriceChartingProduct p = new PriceChartingProduct
                {
                    Id = GetString(root, "id"),
                    ProductName = GetString(root, "product-name"),
                    ConsoleName = GetString(root, "console-name"),

                    // Convert cents/pennies -> dollars
                    LoosePrice = GetPriceUsd(root, "loose-price"),
                    CibPrice = GetPriceUsd(root, "cib-price"),
                    NewPrice = GetPriceUsd(root, "new-price"),
                    GradedPrice = GetPriceUsd(root, "graded-price"),
                    BoxOnlyPrice = GetPriceUsd(root, "box-only-price"),
                    ManualOnlyPrice = GetPriceUsd(root, "manual-only-price"),
                    Bgs10Price = GetPriceUsd(root, "bgs-10-price"),
                    Condition17Price = GetPriceUsd(root, "condition-17-price"),
                    Condition18Price = GetPriceUsd(root, "condition-18-price"),

                    SalesVolume = GetInt(root, "sales-volume")
                };

                return p;
            }
            catch
            {
                return null;
            }
        }

        // -----------------------------
        // Helpers
        // -----------------------------

        private static string? GetString(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out JsonElement p) && p.ValueKind == JsonValueKind.String)
            {
                return p.GetString();
            }

            return null;
        }

        private static int? GetInt(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement p))
            {
                return null;
            }

            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int i))
            {
                return i;
            }

            if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out int si))
            {
                return si;
            }

            return null;
        }

        private static double? GetPriceUsd(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement p))
            {
                return null;
            }

            // API returns pennies/cents, so divide by 100
            double cents;

            if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out double n))
            {
                cents = n;
            }
            else if (p.ValueKind == JsonValueKind.String && double.TryParse(p.GetString(), out double s))
            {
                cents = s;
            }
            else
            {
                return null;
            }

            if (cents <= 0)
            {
                return null;
            }

            return cents / 100.0;
        }
    }
}
