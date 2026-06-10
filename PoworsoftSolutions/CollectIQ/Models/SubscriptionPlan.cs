/*
* FILE            : SubscriptionPlan.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Stores subscription plan metadata for future premium CollectIQ features.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a subscription plan offered by CollectIQ.
    /// </summary>
    public sealed class SubscriptionPlan : BaseModel
    {
        [Indexed(Unique = true)]
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public decimal PriceMonthly { get; set; }

        public decimal PriceYearly { get; set; }

        public int MaxCollections { get; set; }

        public int MaxCards { get; set; }

        public bool AllowsMarketplace { get; set; }

        public bool AllowsSharing { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
