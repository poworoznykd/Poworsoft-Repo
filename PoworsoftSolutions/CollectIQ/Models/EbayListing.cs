/*
* FILE: EbayListing.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-18
* DESCRIPTION:
*     Represents a single eBay listing returned by the eBay Browse API.
*     Used for displaying title, image, price, and item URL.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents an eBay listing summary.
    /// </summary>
    public sealed class EbayListing
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>
        /// eBay item ID.
        /// </summary>
        public string ListingId { get; set; } = string.Empty;

        /// <summary>
        /// Listing title text.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Item price (parsed to decimal where possible).
        /// </summary>
        public decimal? Price { get; set; }

        /// <summary>
        /// Currency code (e.g., USD, CAD).
        /// </summary>
        public string Currency { get; set; } = "USD";

        /// <summary>
        /// URL of the listing's main image.
        /// </summary>
        public string ImageUrl { get; set; } = string.Empty;

        /// <summary>
        /// Direct browser link to the eBay listing.
        /// </summary>
        public string Url { get; set; } = string.Empty;
    }
}
