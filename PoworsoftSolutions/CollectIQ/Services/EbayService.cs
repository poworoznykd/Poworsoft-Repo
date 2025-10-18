//
//  FILE            : EbayService.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : <Your Name>
//  FIRST VERSION   : 2025-10-18
//  DESCRIPTION     :
//      Minimal eBay search client. Uses eBay Browse API (or placeholder)
//      to return a single best-matching listing for a given query.
//
//      NOTE:
//      - For production, create an eBay developer app and obtain an OAuth token.
//      - Store the token securely and inject via configuration.
//      - Endpoint: https://api.ebay.com/buy/browse/v1/item_summary/search?q={query}
//

using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using CollectIQ.Interfaces;
using CollectIQ.Models;
using Newtonsoft.Json.Linq;

namespace CollectIQ.Services
{
    /// <summary>
    /// eBay service implementation using HttpClient.
    /// </summary>
    public class EbayService : IEbayService
    {
        private readonly HttpClient _httpClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="EbayService"/> class.
        /// </summary>
        public EbayService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        /// <summary>
        /// Finds the best-matching listing on eBay for the given query.
        /// </summary>
        /// <param name="query">Free-text card description.</param>
        /// <returns>The top matched listing or null.</returns>
        public async Task<EbayListing?> GetBestMatchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            // TODO: Set your OAuth access token here (or inject via configuration).
            // _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "<ACCESS_TOKEN>");

            string url = $"https://api.ebay.com/buy/browse/v1/item_summary/search?q={Uri.EscapeDataString(query)}&limit=1";

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using HttpResponseMessage resp = await _httpClient.SendAsync(request);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await resp.Content.ReadAsStringAsync();
            JObject root = JObject.Parse(json);
            JToken? item = root.SelectToken("itemSummaries[0]");
            if (item == null)
            {
                return null;
            }

            string title = item.Value<string>("title") ?? string.Empty;
            string imageUrl = item.SelectToken("image.imageUrl")?.ToString() ?? string.Empty;

            // Try to parse price
            string priceStr = item.SelectToken("price.value")?.ToString() ?? string.Empty;
            string currency = item.SelectToken("price.currency")?.ToString() ?? "USD";
            decimal? price = null;

            if (decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal p))
            {
                price = p;
            }

            string listingId = item.Value<string>("itemId") ?? string.Empty;

            return new EbayListing
            {
                Title = title,
                ImageUrl = imageUrl,
                Price = price,
                Currency = currency,
                ListingId = listingId
            };
        }
    }
}
