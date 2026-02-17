using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a locally stored user profile or synced cloud identity.
    /// </summary>
    public sealed class UserProfile : BaseModel
    {
        // --- Authorization Role ---
        public string Role { get; set; } = "Admin";

        // --- External or provider ID (optional) ---
        [Indexed]
        public string? ProviderUserId { get; set; }

        // --- Email for local or cloud login ---
        [Indexed(Unique = true)]
        public string? Email { get; set; }

        // --- Display username ---
        public string? DisplayName { get; set; }

        // --- Local authentication fields ---
        public string? PasswordHash { get; set; }
        public string Salt { get; set; }
    }
}
