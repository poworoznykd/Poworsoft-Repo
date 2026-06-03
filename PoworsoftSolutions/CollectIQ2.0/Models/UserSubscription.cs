/******************************************************************************
 *
 * FILE          : UserSubscription.cs
 * PROJECT       : CollectIQ
 * PROGRAMMER    : Darryl Poworoznyk
 * FIRST VERSION : 2026-06-02
 *
 * DESCRIPTION:
 *
 * This file defines the relationship between a user and a subscription plan.
 *
 * UserSubscription records current and historical subscription information,
 * including start/end dates, external billing provider identifiers, and status.
 *
 * CHANGE LOG:
 *
 * Date         Programmer              Description
 * ----------   --------------------    ---------------------------------------
 * 2026-06-02   Darryl Poworoznyk       Initial creation.
 *
 *****************************************************************************/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a user's subscription.
    /// </summary>
    [Table("UserSubscription")]
    public sealed class UserSubscription : BaseModel
    {
        #region Constructor

        /******************************************************************************
         *
         * METHOD      : UserSubscription
         *
         * DESCRIPTION :
         *
         * Initializes a new user subscription with safe defaults.
         *
         *****************************************************************************/
        public UserSubscription()
        {
            UserProfileId = string.Empty;

            SubscriptionPlanId = string.Empty;

            Status = "Active";

            ExternalSubscriptionId = string.Empty;

            ExternalProvider = string.Empty;

            StartUtc = DateTime.UtcNow;

            EndUtc = null;

            CancelledUtc = null;
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets or sets the related user profile identifier.
        /// </summary>
        [Indexed]
        public string UserProfileId
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the related subscription plan identifier.
        /// </summary>
        [Indexed]
        public string SubscriptionPlanId
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the subscription status.
        /// </summary>
        public string Status
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the external billing provider subscription identifier.
        /// </summary>
        public string ExternalSubscriptionId
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the external billing provider name.
        /// </summary>
        public string ExternalProvider
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the subscription start time in UTC.
        /// </summary>
        public DateTime StartUtc
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the subscription end time in UTC.
        /// </summary>
        public DateTime? EndUtc
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the cancellation time in UTC.
        /// </summary>
        public DateTime? CancelledUtc
        {
            get;
            set;
        }

        #endregion
    }
}