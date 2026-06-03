/******************************************************************************
 *
 * FILE          : SubscriptionPlan.cs
 * PROJECT       : CollectIQ
 * PROGRAMMER    : Darryl Poworoznyk
 * FIRST VERSION : 2026-06-02
 *
 * DESCRIPTION:
 *
 * This file defines the subscription plan model for CollectIQ.
 *
 * Subscription plans describe what a user is allowed to do based on their
 * account tier. These plans support future paid features such as advanced
 * insights, marketplace selling, and collection sharing.
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
    /// Represents a CollectIQ subscription plan.
    /// </summary>
    [Table("SubscriptionPlan")]
    public sealed class SubscriptionPlan : BaseModel
    {
        #region Constructor

        /******************************************************************************
         *
         * METHOD      : SubscriptionPlan
         *
         * DESCRIPTION :
         *
         * Initializes a new subscription plan with safe defaults.
         *
         *****************************************************************************/
        public SubscriptionPlan()
        {
            PlanKey = string.Empty;

            Name = string.Empty;

            Description = string.Empty;

            MonthlyPrice = 0.00m;

            YearlyPrice = 0.00m;

            MaxCollections = null;

            MaxCards = null;

            CanShareCollections = false;

            CanSellCards = false;

            CanUseAdvancedInsights = false;

            IsActive = true;
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets or sets the unique subscription plan key.
        /// </summary>
        [Unique]
        public string PlanKey
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the subscription plan display name.
        /// </summary>
        public string Name
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the subscription plan description.
        /// </summary>
        public string Description
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the monthly subscription price.
        /// </summary>
        public decimal MonthlyPrice
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the yearly subscription price.
        /// </summary>
        public decimal YearlyPrice
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the maximum number of collections allowed.
        /// </summary>
        public int? MaxCollections
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the maximum number of cards allowed.
        /// </summary>
        public int? MaxCards
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets whether collection sharing is enabled.
        /// </summary>
        public bool CanShareCollections
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets whether marketplace selling is enabled.
        /// </summary>
        public bool CanSellCards
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets whether advanced card insights are enabled.
        /// </summary>
        public bool CanUseAdvancedInsights
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets whether this subscription plan is active.
        /// </summary>
        public bool IsActive
        {
            get;
            set;
        }

        #endregion
    }
}