/*
* FILE: EbayService.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-18
* DESCRIPTION:
*     Provides both a live eBay Browse API client for production use
*     and a mock fallback method for development and testing.
*     The live method uses HttpClient to fetch a single best match,
*     while the mock method returns multiple static-style listings
*     for the scanning and recognition demo flow.
*
*     ENDPOINT (Live):
*         https://api.ebay.com/buy/browse/v1/item_summary/search?q={query}
*
*     NOTE:
*         Replace the placeholder token below with your eBay OAuth access token
*         once you register an eBay developer app.
*/

using System;
using System.Collections.Generic;
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
    /// eBay service implementation supporting both live and mock lookups.
    /// </summary>
    public class EbayService : IEbayService
    {
        private readonly HttpClient _httpClient;
        public EbayService() : this(new HttpClient()) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="EbayService"/> class.
        /// </summary>
        /// <param name="httpClient">Shared HttpClient injected from DI container.</param>
        public EbayService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        /// <summary>
        /// Finds the best-matching listing on eBay for the given query (live API).
        /// </summary>
        /// <param name="query">Free-text card description.</param>
        /// <returns>The top matched listing or null if not found.</returns>
        public async Task<EbayListing?> GetBestMatchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;

            // --- OAuth header (for live use only) ---
            // _httpClient.DefaultRequestHeaders.Authorization =
            //     new AuthenticationHeaderValue("Bearer", "<ACCESS_TOKEN>");

            string url = $"https://api.ebay.com/buy/browse/v1/item_summary/search?q={Uri.EscapeDataString(query)}&limit=1";

            using HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using HttpResponseMessage resp = await _httpClient.SendAsync(request);
            if (!resp.IsSuccessStatusCode)
                return null;

            string json = await resp.Content.ReadAsStringAsync();
            JObject root = JObject.Parse(json);
            JToken? item = root.SelectToken("itemSummaries[0]");
            if (item == null)
                return null;

            string title = item.Value<string>("title") ?? string.Empty;
            string imageUrl = item.SelectToken("image.imageUrl")?.ToString() ?? string.Empty;

            // Parse price and currency safely
            string priceStr = item.SelectToken("price.value")?.ToString() ?? string.Empty;
            string currency = item.SelectToken("price.currency")?.ToString() ?? "USD";
            decimal? price = null;
            if (decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed))
                price = parsed;

            return new EbayListing
            {
                Title = title,
                ImageUrl = imageUrl,
                Price = price,
                Currency = currency,
                ListingId = item.Value<string>("itemId") ?? string.Empty
            };
        }

        /// <summary>
        /// Returns a list of mock listings similar to a recognized card.
        /// Used in the scanning demo when live API is not enabled.
        /// </summary>
        /// <param name="card">Recognized card model.</param>
        /// <returns>List of visually similar eBay-style mock listings.</returns>
        public async Task<List<EbayListing>> SearchSimilarListingsAsync(Card card)
        {
            await Task.Delay(400); // simulate API latency

            string player = string.IsNullOrWhiteSpace(card.Player) ? "Player" : card.Player;
            string setName = string.IsNullOrWhiteSpace(card.Set) ? "Set" : card.Set;
            string year = card.Year > 0 ? card.Year.ToString() : "2020";

            return new List<EbayListing>
            {
                new EbayListing
                {
                    Title = $"{year} {player} {setName} - PSA 10 Gem Mint",
                    Price = 249.99m,
                    Currency = "USD",
                    ImageUrl = "https://i.ebayimg.com/images/g/Example1.jpg",
                    ListingId = Guid.NewGuid().ToString()
                },
                new EbayListing
                {
                    Title = $"{year} {player} {setName} - Raw Ungraded",
                    Price = 129.00m,
                    Currency = "USD",
                    ImageUrl = "https://i.ebayimg.com/images/g/Example2.jpg",
                    ListingId = Guid.NewGuid().ToString()
                },
                new EbayListing
                {
                    Title = $"{year} {player} {setName} Refractor Parallel",
                    Price = 320.50m,
                    Currency = "USD",
                    ImageUrl = "https://i.ebayimg.com/images/g/Example3.jpg",
                    ListingId = Guid.NewGuid().ToString()
                }
            };
        }
    }
}
