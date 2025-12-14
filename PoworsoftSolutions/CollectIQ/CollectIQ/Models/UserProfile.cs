using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a locally stored user profile or synced cloud identity.
    /// </summary>
    public sealed class UserProfile : BaseEntity
    {
        [Indexed]
        public string? ProviderUserId { get; set; }

        [Indexed(Unique = true)]
        public string? Email { get; set; }

        public string? GuestId { get; set; }
        public string? DisplayName { get; set; }

        // --- Local authentication fields ---
        public string? PasswordHash { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
