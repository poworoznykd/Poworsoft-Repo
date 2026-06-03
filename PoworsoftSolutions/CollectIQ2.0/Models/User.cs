/******************************************************************************
 *
 * FILE          : User.cs
 * PROJECT       : CollectIQ
 * PROGRAMMER    : Darryl Poworoznyk
 * FIRST VERSION : 2026-06-02
 *
 * DESCRIPTION:
 *
 * Represents a CollectIQ user account.
 *
 * This model is used locally for cached user profile information and will also
 * align with the future online API user model.
 *
 *****************************************************************************/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a CollectIQ user.
    /// </summary>
    [Table("User")]
    public class User
    {
        /// <summary>
        /// Gets or sets the local SQLite user identifier.
        /// </summary>
        [PrimaryKey, AutoIncrement]
        public int LocalId { get; set; }

        /// <summary>
        /// Gets or sets the server-side user identifier.
        /// </summary>
        public string ServerId { get; set; }

        /// <summary>
        /// Gets or sets the user's email address.
        /// </summary>
        public string Email { get; set; }

        /// <summary>
        /// Gets or sets the user's display name.
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Gets or sets the user's subscription plan key.
        /// </summary>
        public string SubscriptionPlanKey { get; set; }

        /// <summary>
        /// Gets or sets whether the user is an administrator.
        /// </summary>
        public bool IsAdmin { get; set; }

        /// <summary>
        /// Gets or sets whether the user account is active.
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Gets or sets the record creation date.
        /// </summary>
        public string CreatedAt { get; set; }

        /// <summary>
        /// Gets or sets the record update date.
        /// </summary>
        public string UpdatedAt { get; set; }
    }
}