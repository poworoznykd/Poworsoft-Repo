/*
* FILE            : UserCredential.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Stores local credential information separately from profile data.
*     This prevents password hashes from being mixed with display names,
*     profile settings, or future social-provider identity records.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a password or external provider credential for a user account.
    /// </summary>
    public sealed class UserCredential : BaseModel
    {
        [Indexed]
        public string UserAccountId { get; set; } = string.Empty;

        [Indexed]
        public string AuthProvider { get; set; } = "Local";

        public string? ProviderUserId { get; set; }

        public string? PasswordHash { get; set; }

        public string? PasswordSalt { get; set; }

        public string PasswordAlgorithm { get; set; } = "PBKDF2-SHA256-100000";

        public DateTime? LastChangedUtc { get; set; }
    }
}
