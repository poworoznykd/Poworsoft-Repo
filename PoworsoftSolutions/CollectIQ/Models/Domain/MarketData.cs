/*
* FILE            : MarketData.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2025-12-09
* DESCRIPTION     :
*     Represents financial and market-related attributes for a collectible card.
*/

namespace CollectIQ.Models.Domain
{
    /// <summary>
    /// Represents financial attributes such as purchase price and estimated value.
    /// </summary>
    public sealed class MarketData
    {
        public decimal? PurchasePrice { get; set; }
        public decimal? EstimatedValue { get; set; }
        public decimal? LastSoldPrice { get; set; }

        public DateTime? LastUpdatedUtc { get; set; }
    }
}
