/*
* FILE            : MarketplaceTransaction.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Stores future marketplace purchase/sale transactions.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a completed or in-progress marketplace transaction.
    /// </summary>
    public sealed class MarketplaceTransaction : BaseModel
    {
        [Indexed]
        public string MarketplaceListingId { get; set; } = string.Empty;

        public string BuyerUserAccountId { get; set; } = string.Empty;

        public string SellerUserAccountId { get; set; } = string.Empty;

        public decimal FinalPrice { get; set; }

        public string Status { get; set; } = "Pending";

        public DateTime? CompletedUtc { get; set; }
    }
}
