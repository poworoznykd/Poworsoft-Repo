/*
* FILE            : UserAccount.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Represents the authentication/account identity for a CollectIQ user.
*     Profile, credential, role, subscription, and collection records should
*     reference this account instead of storing account data directly on UI models.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a local or future cloud-backed CollectIQ user account.
    /// </summary>
    public sealed class UserAccount : BaseModel
    {
        [Indexed(Unique = true)]
        public string EmailNormalized { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string AccountStatus { get; set; } = AccountStatuses.Active;

        public bool IsEmailVerified { get; set; }

        public bool IsGuest { get; set; }

        public DateTime? LastLoginUtc { get; set; }
    }

    /// <summary>
    /// Central account status names used by the local database and future API.
    /// </summary>
    public static class AccountStatuses
    {
        public const string Active = "Active";
        public const string Locked = "Locked";
        public const string Disabled = "Disabled";
        public const string PendingVerification = "PendingVerification";
    }
}
