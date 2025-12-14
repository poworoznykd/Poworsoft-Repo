// -------------------------------------------------------------------------------------------------
// File: CardInsights.cs
// Description: Holds pricing and market insight data for a single sports card, derived primarily
//              from recent eBay search results.
// -------------------------------------------------------------------------------------------------

using System;

namespace CollectIQ.Models
{
    public class CardInsights
    {
        public double? MinPrice { get; set; }

        public double? MaxPrice { get; set; }

        public double? MedianPrice { get; set; }

        public double? AveragePrice { get; set; }

        // Suggested fair value for the card based on comps
        public double? SuggestedPrice { get; set; }

        // Number of listings used to compute insights
        public int ListingCount { get; set; }

        public string Currency { get; set; } = "USD";

        public DateTime? LastUpdatedUtc { get; set; }

        // Short human-readable description of the market picture
        public string Summary { get; set; } = string.Empty;

        // The exact query that was used to fetch these insights
        public string QueryUsed { get; set; } = string.Empty;

        // 0.0–1.0 indicating how confident we are in the suggested price
        public double? ConfidenceScore { get; set; }
    }
}
