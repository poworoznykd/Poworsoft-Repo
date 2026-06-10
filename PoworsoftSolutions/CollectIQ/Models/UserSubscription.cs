/*
* FILE            : UserSubscription.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Links a user account to a subscription plan for future paid features.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a user's active or historical subscription.
    /// </summary>
    public sealed class UserSubscription : BaseModel
    {
        [Indexed]
        public string UserAccountId { get; set; } = string.Empty;

        [Indexed]
        public string SubscriptionPlanId { get; set; } = string.Empty;

        public string Provider { get; set; } = string.Empty;

        public string ProviderCustomerId { get; set; } = string.Empty;

        public string ProviderSubscriptionId { get; set; } = string.Empty;

        public string Status { get; set; } = "Inactive";

        public DateTime? StartedUtc { get; set; }

        public DateTime? EndsUtc { get; set; }

        public DateTime? CancelledUtc { get; set; }
    }
}
