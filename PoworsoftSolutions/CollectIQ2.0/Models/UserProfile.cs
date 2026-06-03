/******************************************************************************
 *
 * FILE          : UserProfile.cs
 * PROJECT       : CollectIQ
 * PROGRAMMER    : Darryl Poworoznyk
 * FIRST VERSION : 2026-06-02
 *
 * DESCRIPTION:
 *
 * This file defines the user profile model for CollectIQ.
 *
 * UserProfile represents the user-facing identity information for a CollectIQ
 * account. It intentionally does not directly own subscription or reward data.
 * Those concerns are represented by separate long-term models:
 *
 * - UserSubscription
 * - SubscriptionPlan
 * - RewardAccount
 * - RewardTransaction
 * - UserSession
 *
 * This keeps the model clean and makes it easier to support future API,
 * marketplace, subscription, and sharing features.
 *
 * CHANGE LOG:
 *
 * Date         Programmer              Description
 * ----------   --------------------    ---------------------------------------
 * 2026-06-02   Darryl Poworoznyk       Rebuilt profile model for long-term
 *                                      CollectIQ identity architecture.
 *
 *****************************************************************************/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a CollectIQ user profile.
    /// </summary>
    [Table("UserProfile")]
    public sealed class UserProfile : BaseModel
    {
        #region Constructor

        /******************************************************************************
         *
         * METHOD      : UserProfile
         *
         * DESCRIPTION :
         *
         * Initializes a new CollectIQ user profile with safe defaults.
         *
         *****************************************************************************/
        public UserProfile()
        {
            Email = string.Empty;

            DisplayName = string.Empty;

            ProviderUserId = string.Empty;

            PasswordHash = string.Empty;

            Salt = string.Empty;

            Role = UserRoles.Regular;

            ProfileImagePath = string.Empty;

            IsEmailVerified = false;

            IsActive = true;

            LastLoginUtc = null;

            LastSyncUtc = null;
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets or sets the email address for the user.
        /// </summary>
        [Indexed]
        public string Email
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the display name shown inside the application.
        /// </summary>
        public string DisplayName
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the external authentication provider user identifier.
        /// </summary>
        /// <remarks>
        /// This can later store values from Google, Apple, Microsoft, or the
        /// CollectIQ API identity provider.
        /// </remarks>
        public string ProviderUserId
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the hashed password value for local/development login.
        /// </summary>
        /// <remarks>
        /// For production, password validation should happen through the API.
        /// This field can still support local test accounts or temporary offline
        /// development accounts.
        /// </remarks>
        public string PasswordHash
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the password salt.
        /// </summary>
        public string Salt
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the user's primary role.
        /// </summary>
        public string Role
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the user's local profile image path.
        /// </summary>
        public string ProfileImagePath
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets whether the user's email has been verified.
        /// </summary>
        public bool IsEmailVerified
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets whether the user account is currently active.
        /// </summary>
        public bool IsActive
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the last successful login time in UTC.
        /// </summary>
        public DateTime? LastLoginUtc
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the last successful profile synchronization time in UTC.
        /// </summary>
        public DateTime? LastSyncUtc
        {
            get;
            set;
        }

        #endregion
    }
}