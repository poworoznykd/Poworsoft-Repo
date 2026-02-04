/*
 *  CollectIQ - PriceChartingService.cs
 *  ---------------------------------------------------------------------------
 *  Purpose:
 *      Minimal client for the PriceCharting API used to fetch price guide values
 *      (raw / graded / PSA10, etc.) to power CollectIQ Insights.
 *
 *  Notes:
 *      - This is a price guide (current values). It is not a sold-comps feed.
 *      - Token is loaded from secure.json in the app package at runtime.
 *      - This file is intentionally dependency-light and safe to call from MAUI.
 */

using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace CollectIQ.Services
{
    /// <summary>
    /// Provides access to PriceCharting price guide data.
    /// </summary>
    public sealed class PriceChartingService
    {
        private const string ProductEndpoint = "https://www.pricecharting.com/api/product";

        private readonly HttpClient httpClient;
        private string? cachedToken;

        public PriceChartingService(HttpClient httpClient)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>
        /// Loads the PriceCharting token from secure.json (app package). This method caches the token.
        /// </summary>
        public async Task<string?> GetTokenAsync(CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(cachedToken))
            {
                return cachedToken;
            }

            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync("secure.json");
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

                // Best effort: search for a value that looks like the token.
                // Common key names: PRICECHARTING_TOKEN, PriceChartingToken, pricecharting_token, etc.
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    string name = prop.Name ?? string.Empty;
                    string value = prop.Value.GetString() ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    bool nameLooksRight =
                        name.Contains("price", StringComparison.OrdinalIgnoreCase) &&
                        name.Contains("chart", StringComparison.OrdinalIgnoreCase);

                    if (nameLooksRight && value.Trim().Length >= 20)
                    {
                        cachedToken = value.Trim();
                        return cachedToken;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Fetches the "best match" product for a query.
        /// </summary>
        public async Task<PriceChartingProduct?> GetBestMatchAsync(string query, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            string? token = await GetTokenAsync(ct);
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            // GET https://www.pricecharting.com/api/product?t=<token>&q=<query>
            string url =
                $"{ProductEndpoint}?t={Uri.EscapeDataString(token)}&q={Uri.EscapeDataString(query)}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);

            using HttpResponseMessage resp = await httpClient.SendAsync(req, ct);
            string body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode || string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);
                return PriceChartingProduct.FromJson(doc.RootElement);
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Represents a PriceCharting product response (flattened to only what we need for Insights).
    /// </summary>
    public sealed class PriceChartingProduct
    {
        public string? Id { get; set; }
        public string? ProductName { get; set; }
        public string? ConsoleName { get; set; }

        public double? LoosePrice { get; set; }
        public double? GradedPrice { get; set; }
        public double? ManualOnlyPrice { get; set; }
        public double? BoxOnlyPrice { get; set; }
        public double? Bgs10Price { get; set; }
        public double? Condition17Price { get; set; }
        public double? Condition18Price { get; set; }

        public int? SalesVolume { get; set; }

        public static PriceChartingProduct FromJson(JsonElement root)
        {
            return new PriceChartingProduct
            {
                Id = GetString(root, "id"),
                ProductName = GetString(root, "product-name"),
                ConsoleName = GetString(root, "console-name"),

                LoosePrice = GetDouble(root, "loose-price"),
                GradedPrice = GetDouble(root, "graded-price"),
                ManualOnlyPrice = GetDouble(root, "manual-only-price"),
                BoxOnlyPrice = GetDouble(root, "box-only-price"),
                Bgs10Price = GetDouble(root, "bgs-10-price"),
                Condition17Price = GetDouble(root, "condition-17-price"),
                Condition18Price = GetDouble(root, "condition-18-price"),

                SalesVolume = GetInt(root, "sales-volume")
            };
        }

        private static string? GetString(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String)
            {
                return el.GetString();
            }
            return null;
        }

        private static double? GetDouble(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement el))
            {
                return null;
            }

            if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out double n))
            {
                return n;
            }

            if (el.ValueKind == JsonValueKind.String)
            {
                string s = el.GetString() ?? string.Empty;
                if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                {
                    return v;
                }
            }

            return null;
        }

        private static int? GetInt(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement el))
            {
                return null;
            }

            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out int n))
            {
                return n;
            }

            if (el.ValueKind == JsonValueKind.String)
            {
                string s = el.GetString() ?? string.Empty;
                if (int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out int v))
                {
                    return v;
                }
            }

            return null;
        }
    }
}
