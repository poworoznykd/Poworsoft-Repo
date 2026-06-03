/******************************************************************************
 *
 * FILE          : RewardTransaction.cs
 * PROJECT       : CollectIQ
 * PROGRAMMER    : Darryl Poworoznyk
 * FIRST VERSION : 2026-06-02
 *
 * DESCRIPTION:
 *
 * This file defines the reward transaction model for CollectIQ.
 *
 * RewardTransaction records individual reward point changes such as earning
 * points, spending points, refunds, bonuses, and administrative adjustments.
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
    /// Represents a reward point transaction.
    /// </summary>
    [Table("RewardTransaction")]
    public sealed class RewardTransaction : BaseModel
    {
        #region Constructor

        /******************************************************************************
         *
         * METHOD      : RewardTransaction
         *
         * DESCRIPTION :
         *
         * Initializes a new reward transaction with safe defaults.
         *
         *****************************************************************************/
        public RewardTransaction()
        {
            RewardAccountId = string.Empty;

            TransactionType = string.Empty;

            Description = string.Empty;

            ReferenceType = string.Empty;

            ReferenceId = string.Empty;

            Points = 0;
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets or sets the related reward account identifier.
        /// </summary>
        [Indexed]
        public string RewardAccountId
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the transaction type.
        /// </summary>
        public string TransactionType
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the number of points changed by this transaction.
        /// </summary>
        public int Points
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the transaction description.
        /// </summary>
        public string Description
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the reference entity type.
        /// </summary>
        public string ReferenceType
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the reference entity identifier.
        /// </summary>
        public string ReferenceId
        {
            get;
            set;
        }

        #endregion
    }
}