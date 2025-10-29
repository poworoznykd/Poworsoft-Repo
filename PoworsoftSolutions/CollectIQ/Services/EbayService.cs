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
        private const string AccessToken = "v^1.1#i^1#r^0#f^0#p^1#I^3#t^H4sIAAAAAAAA/+VYe2wURRjv9SVNKYaAgA3idaGKxd2b3evd7S3cJQdtw0EfB1deNYD7mG2X7u1eduZoj4g0iMSqxYQggcRIE4yPKBIxmkDVPwhRQqIRiC8SoqAgYgxarZjQEGe3pVwrAaRHbOL9s7fffPPN7/vN95gd0FFYVLF14dbLJa57crs7QEeuy8UWg6LCgjkT8nJLC3JAhoKru2NWR/7mvAvzkJjQk8JSiJKmgaC7PaEbSHCEISplGYIpIg0JhpiASMCyEI/U1QocA4SkZWJTNnXKHa0KUarM+RS5Ug34ApCV/T4iNa7ZbDRDlOJTeD7I+b2KLPJyJU/GEUrBqIGwaOAQxQHOR7OA5oKNwC+AgMB6GcDxTZR7ObSQZhpEhQFU2IErOHOtDKw3hyoiBC1MjFDhaKQm3hCJVlXXN87zZNgKD/IQxyJOoeFvC0wFupeLegrefBnkaAvxlCxDhChPeGCF4UaFyDUwdwDfoRqqlVJAkf2q5A8GJB5khcoa00qI+OY4bImm0KqjKkADazh9K0YJG9I6KOPBt3piIlrlth9LUqKuqRq0QlT1/MiqSCxGhatEy0rrMZNWnD9IpmNLq2g2CBWvAnmFDvoUhZcVeXChAWuDNI9YaYFpKJpNGnLXm3g+JKjhcG58gi+DG6LUYDRYERXbiDL1+CEOuSZ7Uwd2MYVbDHtfYYIQ4XZeb70DQ7MxtjQpheGQhZEDDkUhSkwmNYUaOejE4mD4tKMQ1YJxUvB42tramDYvY1rNHg4A1rOyrjYut8CESBFdO9cH9LVbT6A1xxUZkplIE3A6SbC0k1glAIxmKuzzcjzgBnkfDis8UvoPQYbPnuEZka0MkVhFlSqDpA6pPpHnYTYyJDwYpB4bB5TENJ0QrVaIk7ooQ1omcZZKQEtTBK9P5by8CmnFH1TpyqCq0pJP8dOsCiGAUJLkIP9/SpTbDfU4lC2IsxLrWYvzFRsiDZ6U7t9Qw0Wr5Ha2CS5GS1QQWbQ4oNeur1kVbW1SI418TIm1hW43G27o/AJdI8w0kvWzQYCd69kjYaGJMFRG5V5cNpMwZuqanB5bG+y1lJho4XQc6joRjMrJSDIZzU6tzpp7/7JM3Jnf2etR/1F/uqFXyA7ZseWVPR8RA2JSY+wOxMhmwmPnuimS44ctXuugdt9QcYSSh8hIw5IhQ/qSIolyK2NBUTENPT0q3jRy8h1TrBE/B0jQlIEjK+MwwaD1MvEYmSnCAWIa7BNco9kKDdIPsWXqOrSWs6OuB4lECouSDsdaYchCgmjiGGvWbMDPBlgeeL2j8kt2WvHasVbS7FKev9kl3vVyvhSKemJs+Z60TCUl22fUu/DJ4Rl+ARLOcX7sZtdhsNn1Ua7LBeaBcnYmKCvMW5afN74UaRgymqgySGs2yHe9BZlWmE6KmpU7KefYF9/Uz+hZ9HrnD1M7np7l2Z4zIeP+pXs1mDZ0A1OUxxZnXMeA6ddHCth7p5ZwPhZwQeAHAdbbBGZeH81np+RPPpHumXn8wpt9tWXnzhz87fmD4y5+UAtKhpRcroIcEiw5DxhXDl/Z/2fF8cT46vMvf8yPP1lSvPdE56mLDc+dOrqybfLnF6U1h1750f/H0c6Xdod7rNOlzd1NV3v33LdzzzLT+2xF194tV/bvmb3r7W2P9k9If1n8WO97fd2/960vK5hbf6BCOrSm+vIL88uO7evJ36XPXr1j0/d1vfXGNHpcT8WliZX4u85JE8+6Ahs7Lu2wXv16yu7Odwof/OS8a92urse3H3it/+euOUfMLbVf1b3Y3P6UGC0vY44/tKXgkbk1XWciZ/vu5+uKO/tnvLWulH/2cvGv707H20Jlm84vOfnZE/1nW3LOjbu6b2L5T9/uNAo/TT9zZPLpJx8+XF7018aDKz4MLzzwy+L32aLeN9YO7OXfGozGchkTAAA=";
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
