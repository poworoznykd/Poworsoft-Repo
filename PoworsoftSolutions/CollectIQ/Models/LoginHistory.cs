/*
* FILE            : LoginHistory.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Stores login attempts for auditing and future account security screens.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a login attempt for a CollectIQ user account.
    /// </summary>
    public sealed class LoginHistory : BaseModel
    {
        [Indexed]
        public string UserAccountId { get; set; } = string.Empty;

        public string EmailNormalized { get; set; } = string.Empty;

        public string AuthProvider { get; set; } = "Local";

        public bool WasSuccessful { get; set; }

        public string FailureReason { get; set; } = string.Empty;

        public DateTime LoginUtc { get; set; } = DateTime.UtcNow;
    }
}
