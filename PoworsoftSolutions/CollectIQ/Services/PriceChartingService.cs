/*
 *  CollectIQ - PriceChartingService.cs
 *  ---------------------------------------------------------------------------
 *  Purpose:
 *      Lightweight client for the PriceCharting Prices API used to fetch
 *      current guide values (raw / graded) for CollectIQ Insights.
 *
 *  References:
 *      PriceCharting API Documentation: https://www.pricecharting.com/api-documentation
 *
 *  Notes:
 *      - Prices returned by the API are integer pennies (e.g., 1732 == $17.32). We convert to dollars.
 *      - /api/product?q= returns a single "best match". For noisy card titles that can be wrong, so we:
 *          1) call /api/products?q= to get up to 20 candidates
 *          2) choose the best candidate by simple similarity scoring
 *          3) fetch full detail by id via /api/product?id=
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
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
        private const string BaseUrl = "https://www.pricecharting.com";
        private const string ProductEndpoint = BaseUrl + "/api/product";
        private const string ProductsEndpoint = BaseUrl + "/api/products";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

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
                foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
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

                    // Tokens are typically 40 chars, but don't over-assume.
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
        /// Fetches the best match product for a query. Uses /api/products first to avoid wildly incorrect matches.
        /// </summary>
        public async Task<PriceChartingProduct?> GetBestMatchAsync(
            string query,
            string? desiredGenre = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            string? token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            PriceChartingProductSummary[] candidates =
                await GetCandidatesAsync(token, query, cancellationToken).ConfigureAwait(false);

            // If no candidates, fall back to /api/product?q=
            if (candidates.Length == 0)
            {
                return await GetProductByQueryAsync(token, query, cancellationToken).ConfigureAwait(false);
            }

            string normalizedQuery = NormalizeForMatch(query);

            PriceChartingProductSummary? best = null;
            double bestScore = double.MinValue;

            // 1) If a genre is requested, try filtering first.
            if (!string.IsNullOrWhiteSpace(desiredGenre))
            {
                foreach (PriceChartingProductSummary c in candidates)
                {
                    if (string.IsNullOrWhiteSpace(c.Id))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(c.Genre) &&
                        c.Genre.IndexOf(desiredGenre, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    double score = ScoreCandidate(normalizedQuery, c);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = c;
                    }
                }
            }

            // 2) If nothing matched the requested genre, choose best overall.
            if (best == null)
            {
                foreach (PriceChartingProductSummary c in candidates)
                {
                    if (string.IsNullOrWhiteSpace(c.Id))
                    {
                        continue;
                    }

                    double score = ScoreCandidate(normalizedQuery, c);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = c;
                    }
                }
            }

            if (best == null || string.IsNullOrWhiteSpace(best.Id))
            {
                return await GetProductByQueryAsync(token, query, cancellationToken).ConfigureAwait(false);
            }

            return await GetProductByIdAsync(token, best.Id, cancellationToken).ConfigureAwait(false);
        }

        private static double ScoreCandidate(string normalizedQuery, PriceChartingProductSummary c)
        {
            string candidateText = $"{c.ProductName} {c.ConsoleName} {c.Genre}";
            string normalizedCandidate = NormalizeForMatch(candidateText);

            double score = SimilarityScore(normalizedQuery, normalizedCandidate);

            // Mild bias toward exact product-name containment (common for card numbers).
            if (!string.IsNullOrWhiteSpace(c.ProductName))
            {
                string pn = NormalizeForMatch(c.ProductName);
                if (normalizedQuery.Contains(pn, StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.10;
                }
                if (pn.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.10;
                }
            }

            return score;
        }

        private async Task<PriceChartingProductSummary[]> GetCandidatesAsync(
            string token,
            string query,
            CancellationToken ct)
        {
            // GET https://www.pricecharting.com/api/products?t=<token>&q=<query>
            string url =
                $"{ProductsEndpoint}?t={Uri.EscapeDataString(token)}&q={Uri.EscapeDataString(query)}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using HttpResponseMessage resp = await httpClient.SendAsync(req, ct).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode || string.IsNullOrWhiteSpace(body))
            {
                return Array.Empty<PriceChartingProductSummary>();
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);

                if (!doc.RootElement.TryGetProperty("status", out JsonElement statusEl) ||
                    !string.Equals(statusEl.GetString(), "success", StringComparison.OrdinalIgnoreCase))
                {
                    return Array.Empty<PriceChartingProductSummary>();
                }

                if (!doc.RootElement.TryGetProperty("products", out JsonElement productsEl) ||
                    productsEl.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<PriceChartingProductSummary>();
                }

                var list = new List<PriceChartingProductSummary>();
                foreach (JsonElement el in productsEl.EnumerateArray())
                {
                    // Docs guarantee id/product-name/console-name for prices guide; genre is optional.
                    string? id = GetString(el, "id");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    list.Add(new PriceChartingProductSummary
                    {
                        Id = id,
                        ProductName = GetString(el, "product-name"),
                        ConsoleName = GetString(el, "console-name"),
                        Genre = GetString(el, "genre")
                    });
                }

                return list.ToArray();
            }
            catch
            {
                return Array.Empty<PriceChartingProductSummary>();
            }
        }

        private async Task<PriceChartingProduct?> GetProductByQueryAsync(
            string token,
            string query,
            CancellationToken ct)
        {
            // GET https://www.pricecharting.com/api/product?t=<token>&q=<query>
            string url =
                $"{ProductEndpoint}?t={Uri.EscapeDataString(token)}&q={Uri.EscapeDataString(query)}";

            return await FetchProductAsync(url, ct).ConfigureAwait(false);
        }

        private async Task<PriceChartingProduct?> GetProductByIdAsync(
            string token,
            string id,
            CancellationToken ct)
        {
            // GET https://www.pricecharting.com/api/product?t=<token>&id=<id>
            string url =
                $"{ProductEndpoint}?t={Uri.EscapeDataString(token)}&id={Uri.EscapeDataString(id)}";

            return await FetchProductAsync(url, ct).ConfigureAwait(false);
        }

        private async Task<PriceChartingProduct?> FetchProductAsync(string url, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using HttpResponseMessage resp = await httpClient.SendAsync(req, ct).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode || string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);

                if (!doc.RootElement.TryGetProperty("status", out JsonElement statusEl) ||
                    !string.Equals(statusEl.GetString(), "success", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                // IMPORTANT: Prices API returns pennies as integers (see docs).
                // Convert to dollars here so Insights can treat them as normal currency.
                return PriceChartingProduct.FromJson(doc.RootElement);
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeForMatch(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(s.Length);

            foreach (char ch in s.ToUpperInvariant())
            {
                // Normalize punctuation to spaces so tokenization behaves.
                if (char.IsLetterOrDigit(ch) || ch == '#')
                {
                    sb.Append(ch);
                }
                else
                {
                    sb.Append(' ');
                }
            }

            // Collapse whitespace
            return string.Join(' ',
                sb.ToString()
                  .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        /// <summary>
        /// Simple token-based similarity. Good enough to avoid ridiculous mismatches.
        /// </summary>
        private static double SimilarityScore(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return 0.0;
            }

            var aTokens = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var bTokens = b.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var aSet = new HashSet<string>(aTokens);
            var bSet = new HashSet<string>(bTokens);

            int intersect = aSet.Intersect(bSet).Count();
            int union = aSet.Union(bSet).Count();

            double jaccard = union == 0 ? 0.0 : (double)intersect / union;

            // Bonus for substring containment (helps card numbers like "#4", "1st", etc.)
            double containBonus = (b.Contains(a, StringComparison.OrdinalIgnoreCase) || a.Contains(b, StringComparison.OrdinalIgnoreCase))
                ? 0.10
                : 0.0;

            return jaccard + containBonus;
        }

        private static string? GetString(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String)
            {
                return el.GetString();
            }

            return null;
        }

        private sealed class PriceChartingProductSummary
        {
            public string? Id { get; set; }
            public string? ProductName { get; set; }
            public string? ConsoleName { get; set; }
            public string? Genre { get; set; }
        }
    }

    /// <summary>
    /// Represents a PriceCharting /api/product response (flattened to only what Insights needs).
    /// Prices are returned in dollars (converted from pennies).
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
        public double? CibPrice { get; set; }
        public double? NewPrice { get; set; }
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

                LoosePrice = GetMoneyDollars(root, "loose-price"),
                GradedPrice = GetMoneyDollars(root, "graded-price"),
                ManualOnlyPrice = GetMoneyDollars(root, "manual-only-price"),
                BoxOnlyPrice = GetMoneyDollars(root, "box-only-price"),
                CibPrice = GetMoneyDollars(root, "cib-price"),
                NewPrice = GetMoneyDollars(root, "new-price"),
                Bgs10Price = GetMoneyDollars(root, "bgs-10-price"),
                Condition17Price = GetMoneyDollars(root, "condition-17-price"),
                Condition18Price = GetMoneyDollars(root, "condition-18-price"),

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

        private static double? GetMoneyDollars(JsonElement root, string name)
        {
            // Prices are pennies (int). Example: 1732 == $17.32.
            if (!root.TryGetProperty(name, out JsonElement el))
            {
                return null;
            }

            if (el.ValueKind == JsonValueKind.Number)
            {
                if (el.TryGetInt64(out long pennies))
                {
                    return pennies / 100.0;
                }

                if (el.TryGetDouble(out double penniesDouble))
                {
                    return penniesDouble / 100.0;
                }
            }

            if (el.ValueKind == JsonValueKind.String)
            {
                string s = el.GetString() ?? string.Empty;

                if (long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out long pennies))
                {
                    return pennies / 100.0;
                }

                if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double penniesDouble))
                {
                    return penniesDouble / 100.0;
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
