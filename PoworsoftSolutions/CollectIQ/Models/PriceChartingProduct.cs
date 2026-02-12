using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a PriceCharting product result (Prices API).
    /// Note: Prices from PriceCharting are returned as pennies (integer cents). We store them as dollars.
    /// </summary>
    public sealed class PriceChartingProduct
    {
        // Cards: CGC 10
        // Comics: Graded 9.4
        [JsonPropertyName("condition-17-price")]
        public int? Condition17Price { get; set; }

        // Cards: SGC 10
        // (per PriceCharting docs)
        [JsonPropertyName("condition-18-price")]
        public int? Condition18Price { get; set; }
        public string Status { get; set; }

        // Identifiers
        public string Id { get; set; }
        public string ProductName { get; set; }
        public string ConsoleName { get; set; }

        public string Upc { get; set; }
        public string Asin { get; set; }
        public string Epid { get; set; }

        // Metadata
        public string Genre { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public int? SalesVolume { get; set; }

        // Common price points (dollars)
        public double? LoosePrice { get; set; }           // Cards: Ungraded
        public double? NewPrice { get; set; }             // Cards: Grade 8/8.5
        public double? CibPrice { get; set; }             // Cards: Grade 7/7.5
        public double? GradedPrice { get; set; }          // Cards: Grade 9
        public double? BoxOnlyPrice { get; set; }         // Cards: Grade 9.5
        public double? ManualOnlyPrice { get; set; }      // Cards: PSA 10
        public double? Bgs10Price { get; set; }           // Cards: BGS 10
        public double? Cgc10Price { get; set; }           // Cards: CGC 10 (condition-17-price)
        public double? Sgc10Price { get; set; }           // Cards: SGC 10 (condition-18-price)

        /// <summary>
        /// Parse a Prices API response into a PriceChartingProduct.
        /// </summary>
        public static PriceChartingProduct FromJson(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            PriceChartingProduct p = new PriceChartingProduct
            {
                Status = GetString(root, "status"),
                Id = GetString(root, "id"),
                ProductName = GetString(root, "product-name"),
                ConsoleName = GetString(root, "console-name"),
                Upc = GetString(root, "upc"),
                Asin = GetString(root, "asin"),
                Epid = GetString(root, "epid"),
                Genre = GetString(root, "genre"),
                ReleaseDate = GetDate(root, "release-date"),
                SalesVolume = GetInt(root, "sales-volume"),

                LoosePrice = GetMoney(root, "loose-price"),
                NewPrice = GetMoney(root, "new-price"),
                CibPrice = GetMoney(root, "cib-price"),
                GradedPrice = GetMoney(root, "graded-price"),
                BoxOnlyPrice = GetMoney(root, "box-only-price"),
                ManualOnlyPrice = GetMoney(root, "manual-only-price"),
                Bgs10Price = GetMoney(root, "bgs-10-price"),
                Cgc10Price = GetMoney(root, "condition-17-price"),
                Sgc10Price = GetMoney(root, "condition-18-price")
            };

            return p;
        }

        private static string GetString(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out JsonElement v))
            {
                return null;
            }

            if (v.ValueKind == JsonValueKind.String)
            {
                return v.GetString();
            }

            // some values are numbers but we want string forms (e.g., id)
            if (v.ValueKind == JsonValueKind.Number)
            {
                return v.ToString();
            }

            return null;
        }

        private static int? GetInt(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out JsonElement v))
            {
                return null;
            }

            try
            {
                if (v.ValueKind == JsonValueKind.Number)
                {
                    return v.GetInt32();
                }

                if (v.ValueKind == JsonValueKind.String &&
                    int.TryParse(v.GetString(), out int i))
                {
                    return i;
                }
            }
            catch
            {
                // swallow and return null
            }

            return null;
        }

        private static DateTime? GetDate(JsonElement root, string key)
        {
            string s = GetString(root, key);
            if (string.IsNullOrWhiteSpace(s))
            {
                return null;
            }

            if (DateTime.TryParse(s, out DateTime d))
            {
                return d.Date;
            }

            return null;
        }

        private static double? GetMoney(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out JsonElement v))
            {
                return null;
            }

            try
            {
                // PriceCharting returns pennies/cents as an integer.
                if (v.ValueKind == JsonValueKind.Number)
                {
                    long cents = v.GetInt64();
                    return cents / 100.0;
                }

                if (v.ValueKind == JsonValueKind.String &&
                    long.TryParse(v.GetString(), out long centsStr))
                {
                    return centsStr / 100.0;
                }
            }
            catch
            {
                // swallow and return null
            }

            return null;
        }
    }
}
