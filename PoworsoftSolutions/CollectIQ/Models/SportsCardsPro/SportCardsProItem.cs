using System.Collections.Generic;

namespace CollectIQ.Models.SportsCardsPro
{
    /// <summary>
    /// "One object" model for an item page.
    /// - ApiPrices is the reliable, API-backed snapshot from SportsCardsPro Prices API/CSV.
    /// - Page/UX extras are optional and may be unavailable depending on your data source.
    /// </summary>
    public class SportCardsProItem
    {
        // Top-level identity / convenience fields
        public string? ItemPageUrl { get; set; }
        public string? ImageUrl { get; set; }

        // Optional parsing convenience (not required)
        public string? PlayerName { get; set; }
        public string? CardNumber { get; set; }
        public string? ParallelOrVariant { get; set; }
        public string? SetName { get; set; }

        public List<string> Breadcrumbs { get; set; } = new List<string>();
        public int? InListsCount { get; set; }

        // API-backed snapshot (recommended as your "source of truth")
        public SportCardsProPricesSnapshot ApiPrices { get; set; } = new SportCardsProPricesSnapshot();

        // Optional "page extras"
        public SportCardsProChart? Chart { get; set; }
        public List<SportCardsProGradePanel> Grades { get; set; } = new List<SportCardsProGradePanel>();
        public List<SportCardsProSoldListingBucket> SoldListingBuckets { get; set; } = new List<SportCardsProSoldListingBucket>();
        public List<SportCardsProListingPreview> ListingPreviews { get; set; } = new List<SportCardsProListingPreview>();
    }
}
