using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CollectIQ.Models;

namespace CollectIQ.Services
{
    public class PriceChartingService
    {
        private readonly HttpClient httpClient;
        private string? cachedToken;

        public PriceChartingService(HttpClient httpClient)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>
        /// Returns the best match product for the query.
        /// Implementation:
        /// 1) Try /api/products (multiple stubs) to pick the best ID by scoring name similarity.
        /// 2) Pull the full /api/product?id=...
        /// 3) Fallback to /api/product?q=... if needed.
        /// </summary>
        public async Task<PriceChartingProduct?> GetBestMatchAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            string? token = await GetTokenAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            // Step 1/2: use products list first (better than trusting the single best match)
            PriceChartingProduct? byProducts = await TryGetBestByProductsAsync(token, query, cancellationToken).ConfigureAwait(false);
            if (byProducts != null)
            {
                return byProducts;
            }

            // Step 3: fallback to /product?q=
            string url = $"https://www.pricecharting.com/api/product?t={Uri.EscapeDataString(token)}&q={Uri.EscapeDataString(query)}";

            using HttpResponseMessage response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return PriceChartingProduct.FromJson(json);
        }

        private async Task<PriceChartingProduct?> TryGetBestByProductsAsync(string token, string query, CancellationToken cancellationToken)
        {
            string url = $"https://www.pricecharting.com/api/products?t={Uri.EscapeDataString(token)}&q={Uri.EscapeDataString(query)}";

            using HttpResponseMessage response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            // products endpoint returns an array of minimal product objects
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                string[] qTokens = Tokenize(query);

                string? bestId = null;
                int bestScore = int.MinValue;

                foreach (JsonElement el in doc.RootElement.EnumerateArray())
                {
                    string id = GetString(el, "id");
                    string name = GetString(el, "product-name");
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    int score = ScoreName(name, qTokens);

                    // If "console-name" exists and contains "sport", bias it a bit.
                    string console = GetString(el, "console-name");
                    if (!string.IsNullOrWhiteSpace(console) && console.IndexOf("sport", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score += 2;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestId = id;
                    }
                }

                if (string.IsNullOrWhiteSpace(bestId))
                {
                    return null;
                }

                // Pull full product by id
                return await GetByIdAsync(token, bestId!, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        private async Task<PriceChartingProduct?> GetByIdAsync(string token, string id, CancellationToken cancellationToken)
        {
            string url = $"https://www.pricecharting.com/api/product?t={Uri.EscapeDataString(token)}&id={Uri.EscapeDataString(id)}";

            using HttpResponseMessage response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return PriceChartingProduct.FromJson(json);
        }

        private async Task<string?> GetTokenAsync()
        {
            if (!string.IsNullOrWhiteSpace(cachedToken))
            {
                return cachedToken;
            }

            // Keep this consistent with your current approach:
            // token is expected in an embedded resource "secure.json" with:
            // { "PriceChartingToken": "..." }
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                string? resourceName = assembly.GetManifestResourceNames()
                                              .FirstOrDefault(n => n.EndsWith("secure.json", StringComparison.OrdinalIgnoreCase));

                if (string.IsNullOrWhiteSpace(resourceName))
                {
                    return null;
                }

                using Stream? stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    return null;
                }

                using StreamReader reader = new StreamReader(stream);
                string json = await reader.ReadToEndAsync().ConfigureAwait(false);

                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("PriceChartingToken", out JsonElement tokenEl))
                {
                    cachedToken = tokenEl.GetString();
                }

                return cachedToken;
            }
            catch
            {
                return null;
            }
        }

        private static string GetString(JsonElement el, string name)
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            if (el.TryGetProperty(name, out JsonElement p) && p.ValueKind == JsonValueKind.String)
            {
                return p.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static string[] Tokenize(string text)
        {
            char[] split = new[] { ' ', '\t', '\r', '\n', '-', '_', '#', '/', '\\', ':', ';', ',', '.', '(', ')', '[', ']', '{', '}', '|', '\"', '\'' };
            return text.ToLowerInvariant()
                       .Split(split, StringSplitOptions.RemoveEmptyEntries)
                       .Where(t => t.Length > 1 && t != "psa" && t != "bgs" && t != "sgc" && t != "cgc" && t != "graded")
                       .ToArray();
        }

        private static int ScoreName(string name, string[] qTokens)
        {
            string lower = name.ToLowerInvariant();
            int score = 0;

            foreach (string t in qTokens)
            {
                if (lower.Contains(t))
                {
                    score += 3;

                    // boost year tokens
                    if (t.Length == 4 && int.TryParse(t, out _))
                    {
                        score += 4;
                    }
                }
            }

            // slight preference for tighter names (less random extra words)
            score -= Math.Min(10, name.Length / 25);

            return score;
        }
    }

    /// <summary>
    /// Represents PriceCharting product response.
    /// IMPORTANT: PriceCharting API price fields are in pennies/cents. We convert to USD dollars here.
    /// </summary>
    public class PriceChartingProduct
    {
        public string? Id { get; set; }
        public string? ProductName { get; set; }

        // Prices (USD)
        public double? LoosePrice { get; set; }        // RAW / ungraded
        public double? CibPrice { get; set; }          // PSA 7 (cards)
        public double? NewPrice { get; set; }          // PSA 8 (cards)
        public double? GradedPrice { get; set; }       // PSA 9 (cards)
        public double? BoxOnlyPrice { get; set; }      // BGS 9.5 (cards)
        public double? ManualOnlyPrice { get; set; }   // PSA 10 (cards)

        public int? SalesVolume { get; set; }

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

                PriceChartingProduct p = new PriceChartingProduct
                {
                    Id = GetString(root, "id"),
                    ProductName = GetString(root, "product-name"),

                    // Convert price fields from pennies to dollars
                    LoosePrice = GetPriceUsd(root, "loose-price"),
                    CibPrice = GetPriceUsd(root, "cib-price"),
                    NewPrice = GetPriceUsd(root, "new-price"),
                    GradedPrice = GetPriceUsd(root, "graded-price"),
                    BoxOnlyPrice = GetPriceUsd(root, "box-only-price"),
                    ManualOnlyPrice = GetPriceUsd(root, "manual-only-price"),

                    SalesVolume = GetInt(root, "sales-volume")
                };

                return p;
            }
            catch
            {
                return null;
            }
        }

        private static string? GetString(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out JsonElement p))
            {
                if (p.ValueKind == JsonValueKind.String)
                {
                    return p.GetString();
                }
            }

            return null;
        }

        private static int? GetInt(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out JsonElement p))
            {
                if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int i))
                {
                    return i;
                }

                if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out int si))
                {
                    return si;
                }
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

            return cents / 100.0;
        }
    }
}
