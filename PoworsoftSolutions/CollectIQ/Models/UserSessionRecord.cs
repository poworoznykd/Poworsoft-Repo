/*
* FILE            : UserSessionRecord.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Represents a persisted login session record. This is separate from
*     the in-memory UserSession class used by the current UI.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a local device session for a CollectIQ account.
    /// </summary>
    public sealed class UserSessionRecord : BaseModel
    {
        [Indexed]
        public string UserAccountId { get; set; } = string.Empty;

        public string DeviceId { get; set; } = string.Empty;

        public string DeviceName { get; set; } = string.Empty;

        public string AuthProvider { get; set; } = "Local";

        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        public DateTime? ExpiresUtc { get; set; }

        public DateTime? RevokedUtc { get; set; }
    }
}
