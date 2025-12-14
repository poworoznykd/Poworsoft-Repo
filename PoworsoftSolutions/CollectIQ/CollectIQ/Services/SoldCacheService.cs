using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CollectIQ.Data;
using CollectIQ.Models;
using SQLite;

namespace CollectIQ.Services
{
    public class SoldCacheService
    {
        private readonly SQLiteAsyncConnection db;
        private static readonly TimeSpan Expiry = TimeSpan.FromHours(24);

        public SoldCacheService(SQLiteAsyncConnection connection)
        {
            db = connection;
            db.CreateTableAsync<SoldListingCache>().Wait();
        }

        public async Task<List<EbayListing>?> TryGetCachedAsync(string query)
        {
            var entry = await db.Table<SoldListingCache>()
                                .Where(x => x.Query == query)
                                .OrderByDescending(x => x.CreatedUtc)
                                .FirstOrDefaultAsync();

            if (entry == null) return null;

            // Expired?
            if ((DateTime.UtcNow - entry.CreatedUtc) > Expiry)
                return null;

            return JsonSerializer.Deserialize<List<EbayListing>>(entry.JsonPayload);
        }

        public async Task SaveAsync(string query, List<EbayListing> sold)
        {
            var json = JsonSerializer.Serialize(sold);

            await db.InsertAsync(new SoldListingCache
            {
                Query = query,
                JsonPayload = json,
                CreatedUtc = DateTime.UtcNow
            });
        }
    }
}
