/*
* FILE            : MarketplaceOffer.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Stores future marketplace offers made against CollectIQ listings.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents an offer on a marketplace listing.
    /// </summary>
    public sealed class MarketplaceOffer : BaseModel
    {
        [Indexed]
        public string MarketplaceListingId { get; set; } = string.Empty;

        [Indexed]
        public string BuyerUserAccountId { get; set; } = string.Empty;

        public decimal OfferAmount { get; set; }

        public string Status { get; set; } = "Pending";

        public DateTime? ExpiresUtc { get; set; }
    }
}
