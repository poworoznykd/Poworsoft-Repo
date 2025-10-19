using System;
using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Time-series pricing for a card from any source (self, eBay, API).
    /// </summary>
    public sealed class PriceSnapshot : BaseEntity
    {
        /// <summary>
        /// Card id.
        /// </summary>
        [Indexed]
        public string CardId { get; set; } = string.Empty;

        /// <summary>
        /// Price value in the user's currency (decimal for money).
        /// </summary>
        public decimal Price { get; set; }

        /// <summary>
        /// Source label (e.g., "user", "ebay", "scraper").
        /// </summary>
        [Indexed]
        public string Source { get; set; } = "user";

        /// <summary>
        /// When the price was observed (UTC).
        /// </summary>
        [Indexed]
        public DateTime ObservedUtc { get; set; } = DateTime.UtcNow;
    }
}
