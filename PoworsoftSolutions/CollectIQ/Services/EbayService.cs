/*
* FILE: EbayService.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-18
* DESCRIPTION:
*     Enhanced eBay Browse API client with sanitized query,
*     brand correction, category targeting, and multi-listing
*     results for CollectIQ card recognition workflow.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using CollectIQ.Interfaces;
using CollectIQ.Models;
using CollectIQ.Utilities;
using Newtonsoft.Json.Linq;

namespace CollectIQ.Services
{
    public class EbayService : IEbayService
    {
        private readonly HttpClient _httpClient;

        // === Paste your Production OAuth Application Token below ===
        private const string AccessToken = "v^1.1#i^1#f^0#I^3#p^1#r^0#t^H4sIAAAAAAAA/+VYW2wUVRju9oJUpIASQEBYhopQ2NkzM93t7tCuLF0KC6XddheEKtazM2fK2LkxZ5Z2eTBrAzWQqIQQHwRJow/6YCQkRm6RByIxEBMNXrBRBA3GEkgkSgQ0UM9MS9lWAkiX2MR9mZ3//Oc/3/+d/3LmgMyo4rLOZZ1Xxroeyu/KgEy+y8WMAcWjiuaXFORPLcoDWQqurkxpprCjoKcSQ1Ux+EaEDV3DyN2uKhrmHWEVlTI1XodYxrwGVYR5S+Dj4ZW1PEsD3jB1Sxd0hXJHI1UUZEUuCRk/4w/4AgGOIVLtps2EXkUJFSAIgmRQgn6O8ZNhjFMoqmELalYVxQLW52GAhw0mQDkPWB5wdJALNlHu1cjEsq4RFRpQIQct78w1s6DeGSnEGJkWMUKFouGaeH04GllSl6j0ZtkK9dMQt6CVwoPfqnURuVdDJYXuvAx2tPl4ShAQxpQ31LfCYKN8+CaY+4DvMJ0URNYHGIREJCTLGSEnVNbopgqtO+OwJbLokRxVHmmWbKXvxihhI/kiEqz+tzpiIhpx24+GFFRkSUZmFbVkcXhtOBajQhFommklpntE5w8WPLHGiIcJIpETUUD0BH2iGBBEoX+hPmv9NA9ZqVrXRNkmDbvrdGsxIqjRYG443pfFDVGq1+rNsGTZiLL1/AMcck32pvbtYspar9n7ilRChNt5vfsODMy2LFNOpiw0YGHogEMRySrDkEVq6KATi/3h046rqPWWZfBeb1tbG93G0brZ4mUBYLxrVtbGhfVIhZSta+e6oy/ffYJHdlwREJmJZd5KGwRLO4lVAkBroUI+jg0Atp/3wbBCQ6X/EGT57B2cEbnKEBiUAkhkGPIsFwNSTopNqD9IvTYOlIRpjwrNVmQZChSQRyBxllKRKYs855NYLiAhj+gPSp7yoCR5kj7R72EkhABCyaQQDPyfEuVeQz2OBBNZuYn1XMX5M5vC9d6U4t9Uw0YjQjvThFbgBgmEl6+oUGo31qyNtjZJ4UQgJsbaqu41G27rfLUiE2YSZP2cEGDnes5IWKZjC4nDci8u6AaK6YospEfWBnOmGIOmlY4jRSGCYTkZNoxojmp1rtz7l2Xi/vzOYY/6b/rTbb3CdsiOLK/s+ZgYgIZM2x2IFnTVq9u5DsnxwxY3O6jdt1ccrOQlMtKwBESTviQmodBKmwiKuqakh8WbTE6+I4o14mcfCbLYd2SlHSZovFEgHmM9RTjAdL19gkvorUgj/dAydUVB5mpm2PVAVVMWTCpopBWGHCSIDEdYs2Yq/EwFA3wsNyy/BKcVN4+0kuaU8sIO1wsPupw3IqioI8t3w9TFlGCfUR/AJ4d38P1HKM/5MR2uo6DDdSTf5QKV4ElmNpg1qmBVYcEjU7FsIVqGEo3lFo1815uIbkVpA8pm/mN5J77urptxePl7W89Nzmwp9e7IK8m6fulaB6YMXMAUFzBjsm5jwPRbI0XMuMljWR8D2CAoByzgmsDsW6OFzKTCiRHzMLujc0b3Nqn0k8jSQ/nS7td+AGMHlFyuojwSK3nPH/BPTz+XsNbs+GLChXNvqJnocbDr8ZJrOxe17zra3bP1xox57587+XPNmY978UsfmWteReev/XH2nUsn/tq5v7cxcYmauXPb23vmC53fWLHizuMHx9c+PCeyeRx3Cc2MLP1d7CmsaD99csKkU1s27vpcgu2vuI59NnnOngnhKyVo4U+7z964PgsGfxOvnukZfWraRMV1THgr0P3pLysXGNMysctt25d/e2P05bILTzQf3LKq8FG/Op7e/d3FD8veFU6f2n5xlm/D3N6FV2ceut6g7X1dY8G885Vz33x638t7DwSrcevVdX8+W6Y2N3xw5FCm/KnOC7/+qHy5/6uFU9CGfV3Hv28JL9jcWzq1by//BhqVgfsYEwAA";

        public EbayService() : this(new HttpClient()) { }

        public EbayService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", AccessToken);
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        //// === Helper: sanitize OCR text ===
        //private static string SanitizeForEbay(string text)
        //{
        //    if (string.IsNullOrWhiteSpace(text))
        //        return string.Empty;

        //    string cleaned = System.Text.RegularExpressions.Regex.Replace(
        //        text, @"(www\.|\.com|©|inc\.|company|rights reserved|code[\w\d]+)",
        //        "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        //    cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();

        //    cleaned = cleaned.Replace("XeTOPPS", "Topps", StringComparison.OrdinalIgnoreCase)
        //                     .Replace("TOPPS", "Topps", StringComparison.OrdinalIgnoreCase)
        //                     .Replace("PANNI", "Panini", StringComparison.OrdinalIgnoreCase)
        //                     .Replace("CHROME", "Chrome", StringComparison.OrdinalIgnoreCase)
        //                     .Replace("ROOKE", "Rookie", StringComparison.OrdinalIgnoreCase)
        //                     .Replace("RC", "Rookie", StringComparison.OrdinalIgnoreCase);

        //    var tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        //    if (tokens.Length > 6)
        //        cleaned = string.Join(" ", tokens[..6]);

        //    return cleaned;
        //}

        /// <summary>
        /// Gets a list of live eBay listings for a sanitized query.
        /// </summary>
        /// <summary>
        /// Gets a list of live eBay listings for a sanitized query.
        /// </summary>
        public async Task<List<EbayListing>> SearchListingsAsync(string query, int limit = 10)
        {
            var results = new List<EbayListing>();
            if (string.IsNullOrWhiteSpace(query))
                return results;

            string sanitized = await OCRUtility.SanitizeForEbay(query);
            string url = $"https://api.ebay.com/buy/browse/v1/item_summary/search?q={Uri.EscapeDataString(sanitized)}&limit={limit}";

            using HttpRequestMessage req = new(HttpMethod.Get, url);
            req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccessToken);

            System.Diagnostics.Debug.WriteLine($"[eBay URL] {url}");
            System.Diagnostics.Debug.WriteLine($"[Auth Token Length] {req.Headers.Authorization.Parameter?.Length}");

            try
            {
                using HttpResponseMessage resp = await _httpClient.SendAsync(req);

                if (!resp.IsSuccessStatusCode)
                {
                    string err = await resp.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"[eBay API Error]: {resp.StatusCode} - {err}");
                    return results;
                }

                string json = await resp.Content.ReadAsStringAsync();
                var root = Newtonsoft.Json.Linq.JObject.Parse(json);
                var items = root.SelectToken("itemSummaries");
                if (items == null)
                    return results;

                foreach (var item in items)
                {
                    string title = item.Value<string>("title") ?? string.Empty;
                    string imageUrl = item.SelectToken("image.imageUrl")?.ToString() ?? string.Empty;
                    string currency = item.SelectToken("price.currency")?.ToString() ?? "USD";
                    string priceStr = item.SelectToken("price.value")?.ToString() ?? string.Empty;

                    decimal? price = null;
                    if (decimal.TryParse(priceStr, System.Globalization.NumberStyles.Any,
                                         System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                        price = parsed;

                    string webUrl = item.Value<string>("itemWebUrl") ?? string.Empty;

                    results.Add(new EbayListing
                    {
                        Title = title,
                        ImageUrl = imageUrl,
                        Price = price,
                        Currency = currency,
                        ListingId = item.Value<string>("itemId") ?? string.Empty,
                        Url = webUrl
                    });
                }

                return results;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[eBay EXCEPTION]: {ex.Message}");
                return results; // always return, even on failure
            }
        }



        /// <summary>
        /// Retains existing single best match method for backward compatibility.
        /// </summary>
        public async Task<EbayListing?> GetBestMatchAsync(string query)
        {
            var list = await SearchListingsAsync(query, 1);
            return list.Count > 0 ? list[0] : null;
        }
    }
}
