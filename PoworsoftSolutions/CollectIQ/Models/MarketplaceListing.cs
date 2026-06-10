/*
* FILE            : MarketplaceListing.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Stores future CollectIQ marketplace listings created from collection cards.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a listing in the future CollectIQ marketplace.
    /// </summary>
    public sealed class MarketplaceListing : BaseModel
    {
        [Indexed]
        public string CollectionCardId { get; set; } = string.Empty;

        [Indexed]
        public string SellerUserAccountId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public decimal AskingPrice { get; set; }

        public string Currency { get; set; } = "CAD";

        public string Status { get; set; } = "Draft";

        public DateTime? ListedUtc { get; set; }

        public DateTime? SoldUtc { get; set; }
    }
}
