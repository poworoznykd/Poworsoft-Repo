using System;
using SQLite;

namespace CollectIQ.Data
{
    public class SoldListingCache
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// Keywords used for the sold search (parsed title)
        public string Query { get; set; } = string.Empty;

        /// JSON stored list of EbayListing objects
        public string JsonPayload { get; set; } = string.Empty;

        /// When this cache entry was created
        public DateTime CreatedUtc { get; set; }
    }
}
