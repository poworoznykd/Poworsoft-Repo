using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SportsCardsProWpfBasic
{
    public class SportsCardsProClient
    {
        private readonly string _token;

        // docs show sportscardspro; some users still get endpoints via pricecharting.
        private readonly string[] _bases = new[]
        {
        "https://www.sportscardspro.com",
        "https://www.pricecharting.com"
    };

        private readonly HttpClient _http;

        public SportsCardsProClient(string token)
        {
            _token = token;

            var handler = new HttpClientHandler
            {
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            };

            _http = new HttpClient(handler);
            _http.Timeout = TimeSpan.FromSeconds(30);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CollectIQ-Prototype/1.0 (WPF)");
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/html;q=0.9, */*;q=0.8");
        }

        public async Task<List<SearchItem>> SearchProductsAsync(string query)
        {
            // /api/products?t=...&q=...
            string path = "/api/products?t=" + UrlEncoder.Default.Encode(_token) +
                          "&q=" + UrlEncoder.Default.Encode(query);

            string json = await GetFirstOkJsonAsync(path);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string status = root.TryGetProperty("status", out var st) ? (st.GetString() ?? "") : "";
            if (!status.Equals("success", StringComparison.OrdinalIgnoreCase))
            {
                string msg = root.TryGetProperty("error-message", out var em) ? (em.GetString() ?? "Unknown error") : "Unknown error";
                throw new Exception("API error: " + msg);
            }

            var results = new List<SearchItem>();

            if (root.TryGetProperty("products", out var products) && products.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in products.EnumerateArray())
                {
                    results.Add(new SearchItem
                    {
                        Id = p.TryGetProperty("id", out var idEl) ? (idEl.GetString() ?? "") : "",
                        ProductName = p.TryGetProperty("product-name", out var pn) ? (pn.GetString() ?? "") : "",
                        ConsoleName = p.TryGetProperty("console-name", out var cn) ? (cn.GetString() ?? "") : "",
                        Genre = p.TryGetProperty("genre", out var gn) ? (gn.GetString() ?? "") : ""
                    });
                }
            }

            return results;
        }

        public async Task<string> GetProductDetailsJsonAsync(string id)
        {
            // /api/product?t=...&id=...
            string path = "/api/product?t=" + UrlEncoder.Default.Encode(_token) +
                          "&id=" + UrlEncoder.Default.Encode(id);

            string json = await GetFirstOkJsonAsync(path);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string status = root.TryGetProperty("status", out var st) ? (st.GetString() ?? "") : "";
            if (!status.Equals("success", StringComparison.OrdinalIgnoreCase))
            {
                string msg = root.TryGetProperty("error-message", out var em) ? (em.GetString() ?? "Unknown error") : "Unknown error";
                throw new Exception("API error: " + msg);
            }

            return json;
        }

        // Attempts to find an image by checking common product pages and scraping og:image.
        public async Task<string?> TryGetOgImageUrlAsync(string id, string consoleName, string productName)
        {
            // We don't know the exact URL pattern for cards across every category, so try a few reasonable guesses.
            var candidates = new List<string>();

            // Common guesses:
            candidates.Add($"https://www.sportscardspro.com/product/{Uri.EscapeDataString(id)}");
            candidates.Add($"https://www.sportscardspro.com/game/{Uri.EscapeDataString(id)}");
            candidates.Add($"https://www.pricecharting.com/game/{Uri.EscapeDataString(id)}");

            // Fallback: try a search page then scrape og:image from that page (often exists)
            candidates.Add($"https://www.sportscardspro.com/search-products?q={Uri.EscapeDataString(productName)}");

            foreach (var url in candidates)
            {
                try
                {
                    string html = await _http.GetStringAsync(url);
                    var og = ExtractOgImage(html);
                    if (!string.IsNullOrWhiteSpace(og))
                        return og;
                }
                catch
                {
                    // ignore and try next
                }
            }

            return null;
        }

        private static string? ExtractOgImage(string html)
        {
            // <meta property="og:image" content="...">
            var m = Regex.Match(html, "property=[\"']og:image[\"'][^>]*content=[\"']([^\"']+)[\"']",
                                RegexOptions.IgnoreCase);
            if (m.Success)
                return WebUtility.HtmlDecode(m.Groups[1].Value);

            return null;
        }

        private async Task<string> GetFirstOkJsonAsync(string path)
        {
            Exception? last = null;

            foreach (var b in _bases)
            {
                var url = b + path;

                try
                {
                    using var resp = await _http.GetAsync(url);
                    if (resp.StatusCode == HttpStatusCode.NotFound)
                    {
                        last = new Exception($"404 Not Found at {url}");
                        continue;
                    }

                    resp.EnsureSuccessStatusCode();
                    return await resp.Content.ReadAsStringAsync();
                }
                catch (Exception ex)
                {
                    last = ex;
                }
            }

            throw new Exception("Failed calling API. Last error: " + (last?.Message ?? "unknown"));
        }
    }


}