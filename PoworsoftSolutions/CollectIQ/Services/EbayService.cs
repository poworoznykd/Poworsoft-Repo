/*
* FILE: EbayService.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-28
* DESCRIPTION:
*     Live eBay Browse API client used by CollectIQ to search for cards
*     based on OCR text or manual user input. Supports both single best-match
*     and multi-result searches, returning eBay listing objects.
*
*     ENDPOINT:
*         https://api.ebay.com/buy/browse/v1/item_summary/search?q={query}
*
*     INSTRUCTIONS FOR YOUR TOKEN:
*     ------------------------------------------------------------
*     1. Go to https://developer.ebay.com/signin → create an app.
*     2. Request an OAuth2 token using the Client Credentials flow
*        (scope: https://api.ebay.com/oauth/api_scope).
*     3. Copy your "access_token" value from the JSON response.
*     4. PASTE IT BELOW in the variable named `_accessToken`.
*     ------------------------------------------------------------
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
    public sealed class EbayService : IEbayService
    {
        private readonly HttpClient _httpClient;

        // 👇 THIS IS WHERE YOU PASTE YOUR TOKEN
        // Example: private readonly string _accessToken = "v^1.1#B4CAA....";
        private readonly string _accessToken = "v^1.1#i^1#p^1#r^0#I^3#f^0#t^H4sIAAAAAAAA/+VYfWwURRTv9QNpsPAHWoQoHAuIoLs7u9vbu9u0F48eleOjPbhy0gYh+zHXLt3bXXZ2aY8I1hoqAqL4BQJCg6IEQyIJRhKkRpGImGiiooGoMYYSC0YTA2KIRmfvSmkrAaRHbOL9szdv3rx57/e+Zga0Diue3j6r/WKJ57b8jlbQmu/xMCNA8bCi+0cW5I8rygN9GDwdrZNbC9sKfixHYkozhQUQmYaOoLclpelIyBArCMfSBUNEKhJ0MQWRYMtCPDxvrsBSQDAtwzZkQyO80UgFIfJQhhzHchLPc5Lkw1T9ssxao4JgGdEvBf2SojBQUVgZzyPkwKiObFG38TxgfSQDSDZQy3IC4ASWp3iWrSe8CWgh1dAxCwWIUEZdIbPW6qPrtVUVEYKWjYUQoWi4Kl4TjkZmVteW031khXpwiNui7aD+o0pDgd6EqDnw2tugDLcQd2QZIkTQoewO/YUK4cvK3IT6WaiZJOSCPj9XFpQkf9CXEyirDCsl2tfWw6WoCpnMsApQt1U7fT1EMRrSMijbPaNqLCIa8bqf+Y6oqUkVWhXEzBnhunAsRoQiomWltZhBKpk/SCZjCyIkE4QKp8CAQgZ9ihKQFblno6y0HpgH7FRp6Irqgoa81YY9A2KtYX9sWMHXBxvMVKPXWOGk7WrUyxesBcxlDBlfvevUrBcdu1F3/QpTGAhvZnh9D/Sutm1LlRwb9koYOJGBCPvaNFWFGDiZicWe8GlBFUSjbZsCTTc3N1PNHGVYDTQLAEMvmjc3LjfClEhgXjfXs/zq9ReQasYUGeKVSBXstIl1acGxihXQG4iQj2MDgO3Bvb9aoYHUfxD62Ez3z4hcZYhf8nFlfj8IshyQgnwgFxkS6glS2tUDSmKaTIlWE7RNTZQhKeM4c1LQUhWB8yVZLpCEpMIHk2RZMJkkJZ/CkzhrIYBQkuRg4P+UKDca6nEoW9DOSaznLM4fXhmuoR2NX1nFRiNyC1MP56D5SRCePcevzV1RVRdtqk+GawMxJdZccaPZcFXjKzUVI1OL988FAG6u5w6EWQayoTIo8+KyYcKYoalyemg5mLOUmGjZ6TjUNEwYlJFh04zmplbnzLx/WSZuzu7c9aj/qD9d1SrkhuzQsspdj7AA0VQptwNRspGi3Vw3RHz8cMlLM1p7r8o4gInGNNywZEjhvqRIotxEWVBUDF1LDwo3FZ98hxRq2M4sCKqSPbJSGSQotELGFiPDwRggqsY9wdUaTVDH/dC2DE2DVoIZdD1IpRxblDQ41ApDDhJEFYdYs2b8PMMHWR7wg7JLzrTipUOtpLmlvLDNI97ycr4AilpqaNluWobiyO4Z9RZcOej+DyChvMyPafN8ANo8nfkeDygHU5hJYOKwgoWFBbePQ6oNKVVMUkht0PG93oJUE0ybomrlj847fuJk9fhDs/c8dXpM65rJ9HN5I/u8v3Q8Au7qfYEpLmBG9HmOAXdfmSliRo0pYX0MYAP4AsOxfD2YdGW2kCktvGPxnUsOdp3/bdqXr/y6aHnpm4e/Wzl2GijpZfJ4ivJwsOStf3Vr9N0pxVPm33vfqFnBMv6l0pnavBGrx9NO98s/Lz7dTb+39vvXjp6JfHtgY+ToV97q6PBP3tjMJqaXdm3N34LeP97o/2jPpV2B8icPb/jhrfPrutZuZj//uvP86Pp9dQ8+dLJ99MZjE8b+8s5fR/Zv3xv6Y8fazrVW6zPSxe3k6/t/OrHtYFwiKybumrO747OPF0a7X1yl1h29WLB++LJTRxrKzeKyxc9WOqCp+Nwm/sCFqfe88MAXTx3TPt2bSHzz+/KiCx92JJ5PrapbvW+qtuPpzs5dB94+dGJN18RTO5nUhAR4YpRNGPy0bfxO76Yl60raE+fWddMbHt99avnILX+e2VDy2KVHtbPW2a1ZX/4NhTnrwxkTAAA=";
        public EbayService()
        {
            _httpClient = new HttpClient();
        }

        public EbayService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        // ============================================================
        //  SINGLE BEST MATCH
        // ============================================================

        public async Task<EbayListing?> GetBestMatchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;

            try
            {
                string url = $"https://api.ebay.com/buy/browse/v1/item_summary/search?q={Uri.EscapeDataString(query)}&limit=1";

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _accessToken);
                _httpClient.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));

                var json = await _httpClient.GetStringAsync(url);
                var root = JObject.Parse(json);
                var item = root.SelectToken("itemSummaries[0]");
                if (item == null)
                    return null;

                return new EbayListing
                {
                    Title = item.Value<string>("title") ?? string.Empty,
                    ImageUrl = item.SelectToken("image.imageUrl")?.ToString() ?? string.Empty,
                    Price = decimal.TryParse(item.SelectToken("price.value")?.ToString(),
                        NumberStyles.Any, CultureInfo.InvariantCulture, out var price)
                        ? price : null,
                    Currency = item.SelectToken("price.currency")?.ToString() ?? "USD",
                    ListingId = item.Value<string>("itemId") ?? string.Empty,
                    Url = item.Value<string>("itemWebUrl") ?? string.Empty
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EbayService:GetBestMatchAsync] {ex.Message}");
                return null;
            }
        }

        // ============================================================
        //  MULTI-RESULT SEARCH (used by OCR workflow)
        // ============================================================

        public async Task<List<EbayListing>> SearchListingsAsync(string query, int limit = 10)
        {
            var results = new List<EbayListing>();
            if (string.IsNullOrWhiteSpace(query))
                return results;

            try
            {
                string url = $"https://api.ebay.com/buy/browse/v1/item_summary/search?q={Uri.EscapeDataString(query)}&limit={limit}";

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");


                var json = await _httpClient.GetStringAsync(url);
                var root = JObject.Parse(json);
                var items = root["itemSummaries"];
                if (items == null)
                    return results;

                foreach (var item in items)
                {
                    string title = item.Value<string>("title") ?? "";
                    string imageUrl = item.SelectToken("image.imageUrl")?.ToString() ?? "";
                    string urlWeb = item.Value<string>("itemWebUrl") ?? "";
                    string priceStr = item.SelectToken("price.value")?.ToString() ?? "";
                    string currency = item.SelectToken("price.currency")?.ToString() ?? "USD";

                    decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var priceVal);

                    results.Add(new EbayListing
                    {
                        Title = title,
                        ImageUrl = imageUrl,
                        Price = priceVal,
                        Currency = currency,
                        ListingId = item.Value<string>("itemId") ?? "",
                        Url = urlWeb
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EbayService:SearchListingsAsync] {ex.Message}");
            }

            return results;
        }
    }
}
