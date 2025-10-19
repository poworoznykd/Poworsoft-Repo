using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Minimal local snapshot of the signed-in user (or guest placeholder).
    /// </summary>
    public sealed class UserProfile : BaseEntity
    {
        /// <summary>
        /// Provider user id (e.g., Supabase uid). Null for guest.
        /// </summary>
        [Indexed]
        public string? ProviderUserId { get; set; }

        /// <summary>
        /// Email address for the user if available.
        /// </summary>
        [Indexed]
        public string? Email { get; set; }

        /// <summary>
        /// Local guest id (if user started as guest).
        /// </summary>
        [Indexed]
        public string? GuestId { get; set; }

        /// <summary>
        /// Display name (optional).
        /// </summary>
        public string? DisplayName { get; set; }
    }
}
