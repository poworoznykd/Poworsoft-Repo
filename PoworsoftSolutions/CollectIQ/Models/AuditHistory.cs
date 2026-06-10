/*
* FILE            : AuditHistory.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Stores audit records for user, collection, card, and marketplace changes.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents an auditable change made in CollectIQ.
    /// </summary>
    public sealed class AuditHistory : BaseModel
    {
        [Indexed]
        public string UserAccountId { get; set; } = string.Empty;

        public string EntityType { get; set; } = string.Empty;

        [Indexed]
        public string EntityId { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;

        public string OldValueJson { get; set; } = string.Empty;

        public string NewValueJson { get; set; } = string.Empty;
    }
}
