/*
* FILE            : CardInsightRecord.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Stores calculated or imported pricing insight snapshots for a collection card.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a persisted market/pricing insight snapshot.
    /// </summary>
    public sealed class CardInsightRecord : BaseModel
    {
        [Indexed]
        public string CollectionCardId { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;

        public decimal? MedianPrice { get; set; }

        public decimal? AveragePrice { get; set; }

        public decimal? SuggestedPrice { get; set; }

        public int ListingCount { get; set; }

        public DateTime RetrievedUtc { get; set; } = DateTime.UtcNow;

        public string RawDataJson { get; set; } = string.Empty;
    }
}
