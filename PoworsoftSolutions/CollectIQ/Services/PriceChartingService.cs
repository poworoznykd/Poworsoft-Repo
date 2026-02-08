//
//  FILE            : PriceChartingService.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2026-02-08
//  DESCRIPTION     :
//      PriceCharting API client.
//      - Reads API token from "secure.json" in AppDataDirectory (key: PriceChartingToken).
//      - Uses /api/products to get a short list, scores the results, then calls /api/product by id.
//      - IMPORTANT: Keep models in CollectIQ.Models only (no duplicate model classes in Services).
//

using CollectIQ.Models;
using Microsoft.Maui.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace CollectIQ.Services
{
    public sealed class PriceChartingService
    {
        private const string ProductEndpoint = "https://www.pricecharting.com/api/product";
        private const string ProductsEndpoint = "https://www.pricecharting.com/api/products";
        private readonly HttpClient httpClient;

        public PriceChartingService(HttpClient client)
        {
            httpClient = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>
        /// Returns a best-match product by doing:
        ///  1) /api/products?q=...  (top 20 candidates)
        ///  2) Score candidates against the query
        ///  3) /api/product?id=... (full pricing for best candidate)
        /// Returns null if no match is found or token is missing.
        /// </summary>
        public async Task<PriceChartingProduct?> GetBestMatchAsync(string query)
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

            List<PriceChartingProductStub> candidates = await SearchProductsAsync(token, query).ConfigureAwait(false);
            if (candidates.Count == 0)
            {
                // Fallback to the single best match endpoint (q=... on /api/product)
                return await GetProductByQueryAsync(token, query).ConfigureAwait(false);
            }

            PriceChartingProductStub? best = PickBestCandidate(query, candidates);

            if (best == null || string.IsNullOrWhiteSpace(best.Id))
            {
                return await GetProductByQueryAsync(token, query).ConfigureAwait(false);
            }

            return await GetProductByIdAsync(token, best.Id).ConfigureAwait(false);
        }

        // -------------------------------------------------------
        // Token loading (secure.json)
        // -------------------------------------------------------
        private static async Task<string?> GetTokenAsync()
        {
            try
            {
                string securePath = Path.Combine(FileSystem.AppDataDirectory, "secure.json");
                if (!File.Exists(securePath))
                {
                    return null;
                }

                string json = await File.ReadAllTextAsync(securePath).ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("PriceChartingToken", out JsonElement tokenEl) &&
                    tokenEl.ValueKind == JsonValueKind.String)
                {
                    string? token = tokenEl.GetString();
                    return string.IsNullOrWhiteSpace(token) ? null : token.Trim();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        // -------------------------------------------------------
        // Search + fetch
        // -------------------------------------------------------
        private async Task<List<PriceChartingProductStub>> SearchProductsAsync(string token, string query)
        {
            try
            {
                string url =
                    $"{ProductsEndpoint}?t={Uri.EscapeDataString(token)}&q={Uri.EscapeDataString(query)}";

                string response = await httpClient.GetStringAsync(url).ConfigureAwait(false);

                using JsonDocument doc = JsonDocument.Parse(response);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return new List<PriceChartingProductStub>();
                }

                // If status != success, stop.
                if (!doc.RootElement.TryGetProperty("status", out JsonElement statusEl) ||
                    statusEl.ValueKind != JsonValueKind.String ||
                    !string.Equals(statusEl.GetString(), "success", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<PriceChartingProductStub>();
                }

                if (!doc.RootElement.TryGetProperty("products", out JsonElement productsEl) ||
                    productsEl.ValueKind != JsonValueKind.Array)
                {
                    return new List<PriceChartingProductStub>();
                }

                List<PriceChartingProductStub> list = new List<PriceChartingProductStub>();

                foreach (JsonElement item in productsEl.EnumerateArray())
                {
                    try
                    {
                        PriceChartingProductStub? stub = JsonSerializer.Deserialize<PriceChartingProductStub>(item.GetRawText());
                        if (stub != null && !string.IsNullOrWhiteSpace(stub.Id))
                        {
                            list.Add(stub);
                        }
                    }
                    catch
                    {
                        // ignore bad item
                    }
                }

                return list;
            }
            catch
            {
                return new List<PriceChartingProductStub>();
            }
        }

        private async Task<PriceChartingProduct?> GetProductByIdAsync(string token, string id)
        {
            try
            {
                string url =
                    $"{ProductEndpoint}?t={Uri.EscapeDataString(token)}&id={Uri.EscapeDataString(id)}";

                string response = await httpClient.GetStringAsync(url).ConfigureAwait(false);
                return PriceChartingProduct.FromJson(response);
            }
            catch
            {
                return null;
            }
        }

        private async Task<PriceChartingProduct?> GetProductByQueryAsync(string token, string query)
        {
            try
            {
                // /api/product?q=... returns best single match
                string url =
                    $"{ProductEndpoint}?t={Uri.EscapeDataString(token)}&q={Uri.EscapeDataString(query)}";

                string response = await httpClient.GetStringAsync(url).ConfigureAwait(false);
                return PriceChartingProduct.FromJson(response);
            }
            catch
            {
                return null;
            }
        }

        // -------------------------------------------------------
        // Matching / scoring
        // -------------------------------------------------------
        private static PriceChartingProductStub? PickBestCandidate(string query, List<PriceChartingProductStub> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }

            string q = NormalizeForMatch(query);
            HashSet<string> qTokens = Tokenize(q);

            int bestScore = int.MinValue;
            PriceChartingProductStub? best = null;

            foreach (PriceChartingProductStub c in candidates)
            {
                string name = (c.ProductName ?? string.Empty) + " " + (c.ConsoleName ?? string.Empty);
                string n = NormalizeForMatch(name);
                HashSet<string> nTokens = Tokenize(n);

                int score = 0;

                // Token overlap score
                foreach (string t in qTokens)
                {
                    if (nTokens.Contains(t))
                    {
                        score += 3;
                    }
                }

                // Small bonus if query contains a set number like "#4" and candidate does too
                if (q.Contains("#") && n.Contains("#"))
                {
                    score += 3;
                }

                // Prefer exact substring match
                if (!string.IsNullOrWhiteSpace(c.ProductName) &&
                    q.Contains(NormalizeForMatch(c.ProductName)))
                {
                    score += 5;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }

            return best;
        }

        private static string NormalizeForMatch(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string t = text.ToLowerInvariant();

            // Remove common grading words/noise
            string[] noise =
            {
                "psa", "bgs", "cgc", "sgc", "gem", "mint", "graded", "grade",
                "rookie", "rc", "auto", "autograph", "refractor", "holo", "foil",
                "lot", "card", "cards", "n/a"
            };

            foreach (string n in noise)
            {
                t = t.Replace(n, " ");
            }

            // normalize punctuation
            char[] bad = { '|', ',', '.', ':', ';', '(', ')', '[', ']', '{', '}', '-', '_', '/', '\\', '"', '\'' };
            foreach (char ch in bad)
            {
                t = t.Replace(ch, ' ');
            }

            while (t.Contains("  "))
            {
                t = t.Replace("  ", " ");
            }

            return t.Trim();
        }

        private static HashSet<string> Tokenize(string text)
        {
            HashSet<string> set = new HashSet<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return set;
            }

            string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (string p in parts)
            {
                if (p.Length >= 2)
                {
                    set.Add(p);
                }
            }

            return set;
        }
    }
}
