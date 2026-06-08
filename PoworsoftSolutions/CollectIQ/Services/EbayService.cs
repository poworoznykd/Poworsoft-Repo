//
//  FILE            : EbayService.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-11-03
//  UPDATED         : 2025-11-18
//  DESCRIPTION     :
//      REST-based eBay Browse API service for CollectIQ.
//      Loads credentials from Resources/Raw/secure.json,
//      automatically refreshes OAuth2 access tokens,
//      and retrieves live item summaries for both
//      text-based and image-based card recognition.
//      Supports filters for sold vs active listings and
//      optional lastSoldDate ranges for comps analysis.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// <summary>
    /// Provides methods for querying the eBay Browse API using either
    /// manual text queries or the search_by_image endpoint.
    /// </summary>
    public class EbayService : IEbayService
    {
        private readonly HttpClient httpClient;
        private static readonly HttpClient http = new HttpClient();
        private SecureConfig? secureConfig;
        private bool isInitialized;

        /// <summary>
        /// Initializes a new instance of the <see cref="EbayService"/> class.
        /// </summary>
        /// <param name="client">An HTTP client used for outbound requests to eBay.</param>
        public EbayService(HttpClient client)
        {
            httpClient = client;
        }

        #region Initialization

        /// <summary>
        /// Loads the secure configuration (client ID, secret, etc.) from
        /// the packaged secure.json file on first use.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (isInitialized)
            {
                return;
            }

            try
            {
                using Stream stream = await FileSystem.OpenAppPackageFileAsync("secure.json");
                using StreamReader reader = new StreamReader(stream);
                string json = await reader.ReadToEndAsync();

                secureConfig = JsonSerializer.Deserialize<SecureConfig>(json)
                    ?? throw new InvalidOperationException("secure.json parse error");

                isInitialized = true;
                System.Diagnostics.Debug.WriteLine("[eBay INIT] Credentials loaded.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[eBay INIT ERROR] {ex}");
                throw;
            }
        }

        /// <summary>
        /// Requests a new OAuth2 application access token using client credentials.
        /// </summary>
        /// <returns>The bearer token string, or null if the request fails.</returns>
        private async Task<string?> RefreshAccessTokenAsync()
        {
            try
            {
                if (!isInitialized)
                    await InitializeAsync();

                if (secureConfig == null)
                    throw new InvalidOperationException("Config not loaded.");

                string clientId = secureConfig.EBAY_CLIENT_ID?.Trim() ?? "";
                string clientSecret = secureConfig.EBAY_CLIENT_SECRET?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
                    throw new InvalidOperationException("ClientId/ClientSecret missing.");

                string credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")
                );

                Debug.WriteLine($"[eBay CONFIG] clientIdLen={clientId.Length}, clientSecretLen={clientSecret.Length}");
                Debug.WriteLine($"[eBay CONFIG] tokenUrl=https://api.ebay.com/identity/v1/oauth2/token");
                Debug.WriteLine($"[eBay CONFIG] basicHeaderLen={credentials.Length}");


                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.ebay.com/identity/v1/oauth2/token");
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                request.Headers.Accept.ParseAdd("application/json");

                request.Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                    new KeyValuePair<string, string>("scope", "https://api.ebay.com/oauth/api_scope")
                });

                var response = await httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                Debug.WriteLine("[eBay TOKEN] Token received successfully.");
                if (!response.IsSuccessStatusCode)
                    return null;

                using JsonDocument doc = JsonDocument.Parse(content);
                return doc.RootElement.TryGetProperty("access_token", out var tokenElement)
                    ? tokenElement.GetString()
                    : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[eBay TOKEN ERROR] {ex}");
                return null;
            }
        }


        #endregion

        #region Helper Methods

        /// <summary>
        /// Performs minimal cleanup of the user-entered query string.
        /// OCR is no longer used; this simply trims whitespace.
        /// </summary>
        /// <param name="query">The raw user query.</param>
        /// <returns>A trimmed query string safe to send to eBay.</returns>
        private string CleanQueryForEbay(string query)
        {
            return query?.Trim() ?? string.Empty;
        }

        /// <summary>
        /// Builds the lastSoldDate filter fragment for the given number of days.
        /// If days is less than or equal to zero, an empty string is returned.
        /// </summary>
        /// <param name="days">The number of days to look back from now.</param>
        /// <returns>
        /// A lastSoldDate filter string such as
        /// "lastSoldDate:[2025-08-20T00:00:00Z..2025-11-18T23:59:59Z]",
        /// or an empty string if the range is not applied.
        /// </returns>
        private static string BuildLastSoldDateFilter(int days)
        {
            if (days <= 0)
            {
                return string.Empty;
            }

            DateTime utcNow = DateTime.UtcNow;
            DateTime startDate = utcNow.AddDays(-days);

            string startString = startDate.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            string endString = utcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

            return $"lastSoldDate:[{startString}..{endString}]";
        }

        private static string BuildFilterString(string listingTypeFilter, int daysRange)
        {
            DateTime endDate = DateTime.UtcNow;
            DateTime startDate = endDate.AddDays(-daysRange);

            string start = startDate.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            string end = endDate.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

            switch (listingTypeFilter?.Trim().ToLowerInvariant())
            {
                case "sold":
                    // Only sold items within the given range
                    return $"lastSoldDate:[{start}..{end}]";

                case "active":
                    // Only currently active listings (fixed price or auction)
                    // IMPORTANT: do NOT quote the enum values
                    return "buyingOptions:{FIXED_PRICE|AUCTION}";

                case "both":
                case "sold and active":
                    // Combine sold and active filters
                    return $"(lastSoldDate:[{start}..{end}],buyingOptions:{{FIXED_PRICE|AUCTION}})";

                default:
                    // Default fallback: sold items within range
                    return $"lastSoldDate:[{start}..{end}]";
            }
        }


        private async Task<string?> GetAccessTokenAsync()
        {
            if (!isInitialized)
            {
                await InitializeAsync();
            }

            if (secureConfig == null)
            {
                throw new InvalidOperationException("Config not loaded.");
            }

            string creds = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{secureConfig.EBAY_CLIENT_ID}:{secureConfig.EBAY_CLIENT_SECRET}")
            );

            var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://api.ebay.com/identity/v1/oauth2/token"
            );

            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", creds);

            // Only client_credentials works for YOUR app
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("scope", "https://api.ebay.com/oauth/api_scope")
            });

            var response = await http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            try
            {
                using var doc = JsonDocument.Parse(body);

                if (doc.RootElement.TryGetProperty("access_token", out var tokenElem))
                    return tokenElem.GetString();

                Console.WriteLine("Access token missing in response:");
                Console.WriteLine(body);
                return null;
            }
            catch
            {
                Console.WriteLine("Token parse error:");
                Console.WriteLine(body);
                return null;
            }
        }

        #endregion

        #region Text Search

        public async Task<List<EbayListing>> SearchActiveAndSoldAsync(string query, int limit = 20)
        {
            var active = await SearchListingsAsync(query, limit, "active", 90);
            var sold = await SearchSoldAsync(query, limit);

            var combined = new List<EbayListing>();
            combined.AddRange(active);
            combined.AddRange(sold);

            // Remove duplicates based on ListingId
            var unique = combined
                .GroupBy(x => x.ListingId)
                .Select(g => g.First())
                .ToList();

            // Sort Sold → Active, then by price
            unique = unique
                .OrderBy(x => x.Status == "Active" ? 1 : 0)
                .ThenBy(x => x.Price)
                .ToList();

            return unique;
        }

        /// <summary>
        /// Retrieves SOLD listings using the eBay Marketplace Insights API.
        /// Uses the OAuth application access token instead of the legacy Finding API.
        /// </summary>
        /// <param name="query">Search keywords (typically a card title).</param>
        /// <param name="limit">Maximum number of sold records to return.</param>
        /// <param name="daysRange">Look-back window in days for lastSoldDate.</param>
        public static async Task<List<EbayListing>> SearchSoldAsync(
            string query,
            int limit = 20,
            int daysRange = 90)
        {
            List<EbayListing> results = new List<EbayListing>();
            string url =
               "https://api.ebay.com/buy/browse/v1/item_summary/search" +
               $"?q={Uri.EscapeDataString(query)}" +
               "&limit=200" +
               "&sort=-itemEndDate" +
               "&fieldgroups=EXTENDED";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            string? token = await new EbayService(http).GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                Console.WriteLine("Access token unavailable.");
                return results;
            }
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-EBAY-C-MARKETPLACE-ID", "EBAY_US");

            var response = await http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("itemSummaries", out var items))
            {
                Console.WriteLine("No items found.");
                return results;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("itemEndDate", out var endDate))
                {
                    string title = item.GetPropertyOrDefault("title", string.Empty);
                    string imageUrl = item.GetPropertyOrDefault("image", "imageUrl");
                    string currency = item.GetNestedProperty("price", "currency") ?? "USD";
                    string priceString = item.GetNestedProperty("price", "value") ?? "0";
                    string status = "Active";
                    if (item.TryGetProperty("itemEndDate", out JsonElement endDateElement) &&
                        endDateElement.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrEmpty(endDateElement.GetString()))
                    {
                        status = "Sold";
                    }

                    decimal price = 0;
                    decimal.TryParse(priceString, NumberStyles.Any, CultureInfo.InvariantCulture, out price);

                    string urlWeb = item.GetPropertyOrDefault("itemWebUrl", string.Empty);
                    string id = item.GetPropertyOrDefault("itemId", string.Empty);

                    results.Add(new EbayListing
                    {
                        Title = title,
                        ImageUrl = imageUrl,
                        Currency = currency,
                        Price = price,
                        Url = urlWeb,
                        ListingId = id,
                        Status = status
                    });
                }
            }
            return results;
        }

        /// <summary>
        /// Searches eBay listings using the text-based Browse API endpoint with
        /// explicit listing filter options.
        /// </summary>
        /// <param name="query">The user-entered search query.</param>
        /// <param name="limit">Maximum number of records to return.</param>
        /// <param name="listingTypeFilter">"sold" or "active".</param>
        /// <param name="daysRange">
        /// Number of days for lastSoldDate when listingTypeFilter is "sold".
        /// Ignored for "active".
        /// </param>
        /// <returns>A list of eBay listings.</returns>
        public async Task<List<EbayListing>> SearchListingsAsync(
            string query,
            int limit,
            string listingTypeFilter,
            int daysRange)
        {
            List<EbayListing> results = new List<EbayListing>();

            if (string.IsNullOrWhiteSpace(query))
            {
                return results;
            }

            await InitializeAsync();
            string? token = await RefreshAccessTokenAsync();

            if (string.IsNullOrEmpty(token))
            {
                System.Diagnostics.Debug.WriteLine("[eBay SEARCH] Token unavailable.");
                return results;
            }

            string safeQuery = CleanQueryForEbay(query);
            System.Diagnostics.Debug.WriteLine($"[eBay CLEAN QUERY] {safeQuery}");

            string filterValue = BuildFilterString(listingTypeFilter, daysRange);
            string encodedFilter = Uri.EscapeDataString(filterValue);

            string url =
                "https://api.ebay.com/buy/browse/v1/item_summary/search" +
                $"?q={Uri.EscapeDataString(safeQuery)}" +
                $"&limit={limit}" +
                $"&fieldgroups=EXTENDED" +
                $"&filter={encodedFilter}";

            System.Diagnostics.Debug.WriteLine($"[eBay SEARCH URL] {url}");

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            try
            {
                HttpResponseMessage response = await httpClient.SendAsync(request);
                string json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[eBay API ERROR] {response.StatusCode}: {json}");
                    return results;
                }

                using JsonDocument doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("itemSummaries", out JsonElement items))
                {
                    if (listingTypeFilter == "sold")
                    {
                        System.Diagnostics.Debug.WriteLine("[eBay SEARCH] Sold returned 0 — falling back to active.");
                        return await SearchListingsAsync(query, limit, "active", daysRange);
                    }
                    System.Diagnostics.Debug.WriteLine("[eBay SEARCH] No itemSummaries found.");
                    return results;
                }

                foreach (JsonElement item in items.EnumerateArray())
                {
                    string title = item.GetPropertyOrDefault("title", string.Empty);
                    string imageUrl = item.GetPropertyOrDefault("image", "imageUrl");
                    string currency = item.GetNestedProperty("price", "currency") ?? "USD";
                    string priceString = item.GetNestedProperty("price", "value") ?? "0";
                    string status = "Active";
                    if (item.TryGetProperty("itemEndDate", out JsonElement endDateElement) &&
                        endDateElement.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrEmpty(endDateElement.GetString()))
                    {
                        status = "Sold";
                    }

                    decimal price = 0;
                    decimal.TryParse(priceString, NumberStyles.Any, CultureInfo.InvariantCulture, out price);

                    string urlWeb = item.GetPropertyOrDefault("itemWebUrl", string.Empty);
                    string id = item.GetPropertyOrDefault("itemId", string.Empty);

                    results.Add(new EbayListing
                    {
                        Title = title,
                        ImageUrl = imageUrl,
                        Currency = currency,
                        Price = price,
                        Url = urlWeb,
                        ListingId = id,
                        Status = status
                    });
                }

                if (results.Count == 0 && listingTypeFilter == "sold")
                {
                    System.Diagnostics.Debug.WriteLine("[eBay SEARCH] Sold returned 0 — falling back to active.");
                    return await SearchListingsAsync(query, limit, "active", daysRange);
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

        #endregion

        #region Image Search

        /// <summary>
        /// Uses the eBay search_by_image endpoint to find listings that
        /// visually match the provided card image.
        /// </summary>
        /// <param name="base64Image">The base64-encoded image of the card.</param>
        /// <param name="limit">Maximum number of records to return.</param>
        /// <param name="listingTypeFilter">"sold", "active", or "both".</param>
        /// <param name="daysRange">
        /// Number of days for lastSoldDate when listingTypeFilter is "sold" or "both".
        /// Ignored for "active".
        /// </param>
        /// <returns>A list of eBay listings matching the image and filters.</returns>
        public async Task<List<EbayListing>> SearchByImageAsync(
            string base64Image,
            int limit,
            string listingTypeFilter,
            int daysRange)
        {
            List<EbayListing> results = new List<EbayListing>();

            // ----------------------------------------------------------
            // INPUT VALIDATION
            // ----------------------------------------------------------
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

            // ----------------------------------------------------------
            // FILTER STRING CONSTRUCTION
            // ----------------------------------------------------------
            string filterValue = BuildFilterString(listingTypeFilter, daysRange);
            string encodedFilter = Uri.EscapeDataString(filterValue);

            string url =
                $"https://api.ebay.com/buy/browse/v1/item_summary/search_by_image?limit={limit}";

            if (!string.IsNullOrWhiteSpace(encodedFilter))
            {
                url += $"&filter={encodedFilter}";
            }

            System.Diagnostics.Debug.WriteLine($"[eBay IMAGE URL] {url}");

            var payload = new { image = base64Image };
            string jsonBody = JsonSerializer.Serialize(payload);

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("X-EBAY-C-MARKETPLACE-ID", "EBAY_US");
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            try
            {
                HttpResponseMessage response = await httpClient.SendAsync(request);
                string json = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"[eBay IMAGE STATUS] {(int)response.StatusCode} {response.ReasonPhrase}");
                System.Diagnostics.Debug.WriteLine($"[eBay IMAGE BODY] {json}");

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[eBay IMAGE ERROR] {response.StatusCode}: {json}");
                    return results;
                }

                // ----------------------------------------------------------
                // PARSE RESULTS
                // ----------------------------------------------------------
                using JsonDocument doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("itemSummaries", out JsonElement items))
                {
                    System.Diagnostics.Debug.WriteLine("[eBay IMAGE] No itemSummaries found.");
                    return results;
                }

                foreach (JsonElement item in items.EnumerateArray())
                {
                    string title = item.GetPropertyOrDefault("title", string.Empty);
                    string imageUrl = item.GetNestedProperty("image", "imageUrl") ?? string.Empty;
                    string currency = item.GetNestedProperty("price", "currency") ?? "USD";
                    string priceString = item.GetNestedProperty("price", "value") ?? "0";
                    decimal price = Convert.ToDecimal(priceString, CultureInfo.InvariantCulture);

                    string urlWeb = item.GetPropertyOrDefault("itemWebUrl", string.Empty);
                    string id = item.GetPropertyOrDefault("itemId", string.Empty);
                    string status = "Active";
                    if (item.TryGetProperty("itemEndDate", out JsonElement endDateElement) &&
                        endDateElement.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrEmpty(endDateElement.GetString()))
                    {
                        status = "Sold";
                    }

                    results.Add(new EbayListing
                    {
                        Title = title,
                        ImageUrl = imageUrl,
                        Currency = currency,
                        Price = price,
                        Url = urlWeb,
                        ListingId = id,
                        Status = status
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

        #endregion

        #region Convenience

        /// <summary>
        /// Convenience method that returns only the first result for a given
        /// text-based query.
        /// </summary>
        /// <param name="query">The search query.</param>
        /// <returns>The best match listing, or null if none are found.</returns>
        public async Task<EbayListing?> GetBestMatchAsync(string query)
        {
            List<EbayListing> list = await SearchListingsAsync(query, 1, "active", 90);
            return list.Count > 0 ? list[0] : null;
        }

        #endregion
    }

    /// <summary>
    /// JSON helper extensions for safely reading properties from eBay responses.
    /// </summary>
    internal static class JsonExtensions
    {
        /// <summary>
        /// Attempts to retrieve a nested property under the specified object
        /// key and nested key, falling back to an empty string if the path
        /// does not exist.
        /// </summary>
        public static string GetPropertyOrDefault(this JsonElement element, string obj, string nested)
        {
            try
            {
                if (!element.TryGetProperty(obj, out JsonElement subElement))
                {
                    return string.Empty;
                }

                if (subElement.ValueKind == JsonValueKind.String)
                {
                    return subElement.GetString() ?? string.Empty;
                }

                if (subElement.ValueKind == JsonValueKind.Object &&
                    subElement.TryGetProperty(nested, out JsonElement valueElement))
                {
                    return valueElement.GetString() ?? string.Empty;
                }

                if (subElement.ValueKind == JsonValueKind.Array &&
                    subElement.GetArrayLength() > 0)
                {
                    JsonElement first = subElement[0];
                    if (first.ValueKind == JsonValueKind.Object &&
                        first.TryGetProperty(nested, out JsonElement nestedElement))
                    {
                        return nestedElement.GetString() ?? string.Empty;
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[JSON EXT ERROR] {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Attempts to retrieve a simple child property from a given parent key.
        /// </summary>
        public static string? GetNestedProperty(this JsonElement element, string parent, string child)
        {
            if (element.TryGetProperty(parent, out JsonElement parentElement) &&
                parentElement.TryGetProperty(child, out JsonElement childElement))
            {
                return childElement.GetString();
            }

            return null;
        }
    }

    /// <summary>
    /// Configuration model mapping directly to secure.json.
    /// </summary>
    public class SecureConfig
    {
        public string EBAY_CLIENT_ID { get; set; } = string.Empty;
        public string EBAY_CLIENT_SECRET { get; set; } = string.Empty;
        public string EBAY_REFRESH_TOKEN { get; set; } = string.Empty;

        public string SPORTS_CARDSPRO_TOKEN { get; set; } = string.Empty;
    }
}
