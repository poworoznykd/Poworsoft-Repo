/*
* FILE            : UserProfile.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2025-10-25
* UPDATED         : 2026-06-08
* DESCRIPTION     :
*     Represents profile/display information for a CollectIQ user. Account and
*     credential data now live in UserAccount and UserCredential, but legacy
*     properties remain temporarily for backward compatibility with existing UI.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents locally stored user profile information.
    /// </summary>
    public sealed class UserProfile : BaseModel
    {
        [Indexed]
        public string UserAccountId { get; set; } = string.Empty;

        public DateTime? LastLoginUtc { get; set; }

        public string Role { get; set; } = UserRoles.Regular;

        [Indexed]
        public string? ProviderUserId { get; set; }

        [Indexed(Unique = true)]
        public string? Email { get; set; }

        public string? DisplayName { get; set; }

        public string? AvatarImageId { get; set; }

        public string? Bio { get; set; }

        public string? LocationDisplay { get; set; }

        // Legacy compatibility only. New code should use UserCredential.
        public string? PasswordHash { get; set; }

        // Legacy compatibility only. New code should use UserCredential.PasswordSalt.
        public string? Salt { get; set; }
    }
}
