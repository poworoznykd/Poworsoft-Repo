using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CollectIQ.Models;
using CollectIQ.Parsers;

namespace CollectIQ.Services
{
    public class SearchCoordinator
    {
        private readonly EbayService ebay;
        private readonly SoldCacheService cache;

        public SearchCoordinator(EbayService ebayService, SoldCacheService cacheService)
        {
            ebay = ebayService;
            cache = cacheService;
        }

        public async Task<InsightsResult> RunFullSearchAsync(string base64Image)
        {
            // Step 1 — Search by Image (ACTIVE results)
            var active = await ebay.SearchByImageAsync(base64Image, 30, "active", 90);

            if (active.Count == 0)
                return new InsightsResult();

            // Step 2 — Parse metadata from best match
            var best = active.First();
            var metadata = CardMetadataParser.ExtractMetadata(best);


            // Step 3 — Build query for further searches
            string refinedQuery = $"{metadata.Year} {metadata.PlayerName} {metadata.SetName}".Trim();

            // Step 4 — Try cache for SOLD data
            List<EbayListing>? sold = await cache.TryGetCachedAsync(refinedQuery);

            if (sold == null)
            {
                // Not cached — call Finding API
                sold = await ebay.GetSoldListingsFindingApiAsync(refinedQuery);

                // Save to cache
                await cache.SaveAsync(refinedQuery, sold);
            }

            return new InsightsResult
            {
                ActiveListings = active,
                SoldListings = sold,
                Metadata = metadata,
                QueryUsed = refinedQuery
            };
        }
    }

    public class InsightsResult
    {
        public List<EbayListing> ActiveListings { get; set; } = new();
        public List<EbayListing> SoldListings { get; set; } = new();
        public Card Metadata { get; set; } = new();
        public string QueryUsed { get; set; } = "";
    }
}
