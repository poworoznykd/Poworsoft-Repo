//
//  FILE            : EbayService.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-11-03
//  DESCRIPTION     :
//      REST-based eBay Browse API service for CollectIQ.
//      Loads credentials from Resources/Raw/secure.json,
//      automatically refreshes OAuth2 access tokens,
//      and retrieves live item summaries for card recognition.
//
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CollectIQ.Interfaces;
using CollectIQ.Models;
using Microsoft.Maui.Storage;

namespace CollectIQ.Services
{
    public class EbayService : IEbayService
    {
        private readonly HttpClient _httpClient;
        private SecureConfig? _config;
        private bool _initialized;

        public EbayService(HttpClient client)
        {
            _httpClient = client;
        }

        // -------------------------------------------------------
        //  Initialization: load secure.json from app package
        // -------------------------------------------------------
        public async Task InitializeAsync()
        {
            if (_initialized)
                return;

            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync("secure.json");
                using var reader = new StreamReader(stream);
                string json = await reader.ReadToEndAsync();

                _config = JsonSerializer.Deserialize<SecureConfig>(json)
                    ?? throw new InvalidOperationException("secure.json parse error");

                _initialized = true;
                System.Diagnostics.Debug.WriteLine("[eBay INIT] Credentials loaded.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[eBay INIT ERROR] {ex}");
                throw;
            }
        }

        // -------------------------------------------------------
        //  Refresh OAuth2 access token using the stored refresh token
        // -------------------------------------------------------
        private async Task<string?> RefreshAccessTokenAsync()
        {
            try
            {
                if (!_initialized)
                    await InitializeAsync();

                if (_config == null)
                    throw new InvalidOperationException("Config not loaded.");

                string credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{_config.EBAY_CLIENT_ID}:{_config.EBAY_CLIENT_SECRET}")
                );

                var req = new HttpRequestMessage(HttpMethod.Post, "https://api.ebay.com/identity/v1/oauth2/token");
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                req.Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                    new KeyValuePair<string, string>("scope", "https://api.ebay.com/oauth/api_scope")
                });


                var resp = await _httpClient.SendAsync(req);
                string content = await resp.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"[eBay TOKEN] {(int)resp.StatusCode} {resp.ReasonPhrase}");
                System.Diagnostics.Debug.WriteLine($"[eBay TOKEN BODY] {content}");

                if (!resp.IsSuccessStatusCode)
                    return null;

                using var doc = JsonDocument.Parse(content);
                return doc.RootElement.TryGetProperty("access_token", out var tokenEl)
                    ? tokenEl.GetString()
                    : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[eBay TOKEN ERROR] {ex.Message}");
                return null;
            }
        }

        private string CleanQueryForEbay(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "";

            // Lowercase and trim punctuation
            query = Regex.Replace(query, @"[^A-Za-z0-9\s]", " ");
            query = Regex.Replace(query, @"\s{2,}", " ").Trim();

            // Split words
            var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

            // Filter out noise: short or non-vowel words
            tokens = tokens
                .Where(t => t.Length > 2 && Regex.IsMatch(t, "[aeiouAEIOU]"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Keep year + brand + player words only
            var keepers = new List<string>();
            foreach (var t in tokens)
            {
                if (Regex.IsMatch(t, @"^(19|20)\d{2}$") || // year
                    Regex.IsMatch(t, @"(Panini|Donruss|Topps|Prizm|Select|Mosaic)", RegexOptions.IgnoreCase) ||
                    char.IsUpper(t[0])) // likely player name
                {
                    keepers.Add(t);
                }
            }

            // Fallback: if filter too strict, revert to cleaned tokens
            if (keepers.Count < 3)
                keepers = tokens;

            return string.Join(" ", keepers);
        }


        // -------------------------------------------------------
        //  Search listings via eBay Browse API
        // -------------------------------------------------------
        public async Task<List<EbayListing>> SearchListingsAsync(string query, int limit = 10)
        {
            var results = new List<EbayListing>();

            if (string.IsNullOrWhiteSpace(query))
                return results;

            await InitializeAsync();
            string? token = await RefreshAccessTokenAsync();

            if (string.IsNullOrEmpty(token))
            {
                System.Diagnostics.Debug.WriteLine("[eBay SEARCH] Token unavailable.");
                return results;
            }

            // Clean up query before sending to eBay
            string safeQuery = CleanQueryForEbay(query);
            System.Diagnostics.Debug.WriteLine($"[eBay CLEAN QUERY] {safeQuery}");

            // Build final URL with extra filters
            string url = "https://api.ebay.com/buy/browse/v1/item_summary/search" +
                         $"?q={Uri.EscapeDataString(safeQuery)}" +
                         $"&limit={limit}" +
                         $"&fieldgroups=EXTENDED" +
                         $"&filter=priceCurrency:USD";

            System.Diagnostics.Debug.WriteLine($"[eBay SEARCH URL] {url}");


            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            try
            {
                var resp = await _httpClient.SendAsync(req);
                string json = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[eBay API ERROR] {resp.StatusCode}: {json}");
                    return results;
                }

                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("itemSummaries", out var items))
                {
                    System.Diagnostics.Debug.WriteLine("[eBay SEARCH] No itemSummaries found.");
                    return results;
                }

                foreach (var item in items.EnumerateArray())
                {
                    string title = item.GetPropertyOrDefault("title", string.Empty);
                    string imageUrl = item.GetPropertyOrDefault("image", "imageUrl");
                    string currency = item.GetNestedProperty("price", "currency") ?? "USD";
                    string priceStr = item.GetNestedProperty("price", "value") ?? "0";
                    decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price);
                    string urlWeb = item.GetPropertyOrDefault("itemWebUrl", string.Empty);
                    string id = item.GetPropertyOrDefault("itemId", string.Empty);

                    results.Add(new EbayListing
                    {
                        Title = title,
                        ImageUrl = imageUrl,
                        Currency = currency,
                        Price = price,
                        Url = urlWeb,
                        ListingId = id
                    });
                }

                System.Diagnostics.Debug.WriteLine($"[eBay SEARCH] Found {results.Count} listings.");
                return results;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[eBay SEARCH EXCEPTION] {ex.Message}");
                return results;
            }
        }

        // -------------------------------------------------------
        //  Search listings via eBay Browse API - search_by_image
        // -------------------------------------------------------
        public async Task<List<EbayListing>> SearchByImageAsync(string base64Image, int limit = 10)
        {
            var results = new List<EbayListing>();

            if (string.IsNullOrWhiteSpace(base64Image))
            {
                System.Diagnostics.Debug.WriteLine("[eBay IMAGE] Empty base64 image.");
                return results;
            }

            await InitializeAsync();
            string? token = await RefreshAccessTokenAsync();

            if (string.IsNullOrEmpty(token))
            {
                System.Diagnostics.Debug.WriteLine("[eBay IMAGE] Token unavailable.");
                return results;
            }

            // Endpoint per eBay docs – using limit and USD filter, similar to text search
            string url =
                $"https://api.ebay.com/buy/browse/v1/item_summary/search_by_image?limit={limit}&filter=priceCurrency:USD";

            // Build JSON payload: { "image": "<BASE64 STRING>" }
            var payload = new { image = base64Image };
            string jsonBody = JsonSerializer.Serialize(payload);

            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Add("X-EBAY-C-MARKETPLACE-ID", "EBAY_US");
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            try
            {
                var resp = await _httpClient.SendAsync(req);
                string json = await resp.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"[eBay IMAGE STATUS] {(int)resp.StatusCode} {resp.ReasonPhrase}");
                System.Diagnostics.Debug.WriteLine($"[eBay IMAGE BODY] {json}");

                if (!resp.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[eBay IMAGE ERROR] {resp.StatusCode}: {json}");
                    return results;
                }

                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("itemSummaries", out var items))
                {
                    System.Diagnostics.Debug.WriteLine("[eBay IMAGE] No itemSummaries found.");
                    return results;
                }

                foreach (var item in items.EnumerateArray())
                {
                    string title = item.GetPropertyOrDefault("title", string.Empty);
                    string imageUrl = item.GetPropertyOrDefault("image", "imageUrl");
                    string currency = item.GetNestedProperty("price", "currency") ?? "USD";
                    string priceStr = item.GetNestedProperty("price", "value") ?? "0";
                    decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price);
                    string urlWeb = item.GetPropertyOrDefault("itemWebUrl", string.Empty);
                    string id = item.GetPropertyOrDefault("itemId", string.Empty);

                    results.Add(new EbayListing
                    {
                        Title = title,
                        ImageUrl = imageUrl,
                        Currency = currency,
                        Price = price,
                        Url = urlWeb,
                        ListingId = id
                    });
                }

                System.Diagnostics.Debug.WriteLine($"[eBay IMAGE] Found {results.Count} listings.");
                return results;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[eBay IMAGE EXCEPTION] {ex.Message}");
                return results;
            }
        }

        // -------------------------------------------------------
        //  Best Match (first result)
        // -------------------------------------------------------
        public async Task<EbayListing?> GetBestMatchAsync(string query)
        {
            var list = await SearchListingsAsync(query, 1);
            return list.Count > 0 ? list[0] : null;
        }
    }

    // -------------------------------------------------------
    //  JSON helper extensions
    // -------------------------------------------------------
    internal static class JsonExtensions
    {
        public static string GetPropertyOrDefault(this JsonElement el, string obj, string nested)
        {
            try
            {
                if (!el.TryGetProperty(obj, out JsonElement subObj))
                    return "";

                // If the property itself is a string, just return it
                if (subObj.ValueKind == JsonValueKind.String)
                    return subObj.GetString() ?? "";

                // If it’s an object, look inside it
                if (subObj.ValueKind == JsonValueKind.Object && subObj.TryGetProperty(nested, out JsonElement value))
                    return value.GetString() ?? "";

                // If it’s an array, grab the first element’s nested value
                if (subObj.ValueKind == JsonValueKind.Array && subObj.GetArrayLength() > 0)
                {
                    var first = subObj[0];
                    if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty(nested, out JsonElement nestedVal))
                        return nestedVal.GetString() ?? "";
                }

                return "";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[JSON EXT ERROR] {ex.Message}");
                return "";
            }
        }


        public static string? GetNestedProperty(this JsonElement el, string parent, string child)
        {
            if (el.TryGetProperty(parent, out var p) && p.TryGetProperty(child, out var c))
                return c.GetString();
            return null;
        }
    }

    // -------------------------------------------------------
    //  Secure Config model
    // -------------------------------------------------------
    public class SecureConfig
    {
        public string EBAY_CLIENT_ID { get; set; } = string.Empty;
        public string EBAY_CLIENT_SECRET { get; set; } = string.Empty;
        public string EBAY_REFRESH_TOKEN { get; set; } = string.Empty;
    }
}
