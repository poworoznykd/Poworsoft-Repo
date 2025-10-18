//
//  FILE            : EbayListing.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : <Your Name>
//  FIRST VERSION   : 2025-10-18
//  DESCRIPTION     :
//      DTO representing a simplified eBay listing used by the Add Card flow.
//

using System;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a simplified listing result from eBay searches.
    /// </summary>
    public class EbayListing
    {
        /// <summary>Listing title.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Primary image URL for the listing.</summary>
        public string ImageUrl { get; set; } = string.Empty;

        /// <summary>Observed price (if available).</summary>
        public decimal? Price { get; set; }

        /// <summary>Currency code (e.g., USD).</summary>
        public string Currency { get; set; } = "USD";

        /// <summary>The listing id (for potential deep link or future enrichment).</summary>
        public string ListingId { get; set; } = string.Empty;
    }
}
