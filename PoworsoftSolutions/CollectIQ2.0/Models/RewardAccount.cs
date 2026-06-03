/******************************************************************************
 *
 * FILE          : RewardAccount.cs
 * PROJECT       : CollectIQ
 * PROGRAMMER    : Darryl Poworoznyk
 * FIRST VERSION : 2026-06-02
 *
 * DESCRIPTION:
 *
 * This file defines the reward account model for CollectIQ.
 *
 * RewardAccount tracks the user's current reward point balance and lifetime
 * totals. Individual earning/spending events are stored in RewardTransaction.
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
    /// Represents a user's reward account.
    /// </summary>
    [Table("RewardAccount")]
    public sealed class RewardAccount : BaseModel
    {
        #region Constructor

        /******************************************************************************
         *
         * METHOD      : RewardAccount
         *
         * DESCRIPTION :
         *
         * Initializes a new reward account with safe defaults.
         *
         *****************************************************************************/
        public RewardAccount()
        {
            UserProfileId = string.Empty;

            PointsBalance = 0;

            LifetimePointsEarned = 0;

            LifetimePointsSpent = 0;
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
        /// Gets or sets the user's current reward point balance.
        /// </summary>
        public int PointsBalance
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the lifetime number of points earned.
        /// </summary>
        public int LifetimePointsEarned
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the lifetime number of points spent.
        /// </summary>
        public int LifetimePointsSpent
        {
            get;
            set;
        }

        #endregion
    }
}