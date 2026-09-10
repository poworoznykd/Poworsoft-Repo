/*
* FILE            : SyncQueueItem.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Stores pending local operations for future cloud synchronization.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a queued synchronization operation.
    /// </summary>
    public sealed class SyncQueueItem : BaseModel
    {
        public string EntityType { get; set; } = string.Empty;

        [Indexed]
        public string EntityId { get; set; } = string.Empty;

        public string Operation { get; set; } = string.Empty;

        public string PayloadJson { get; set; } = string.Empty;

        // Do not use [Indexed] here. Older CollectIQ databases already have
        // SyncQueueItem without this column, and sqlite-net may attempt to create
        // model indexes before migration 5 has added the column. Migration 5
        // creates the compound account/entity index after the schema is upgraded.
        public string UserAccountId { get; set; } = string.Empty;

        [Indexed]
        public string Status { get; set; } = "Pending";

        public int RetryCount { get; set; }

        public DateTime? LastAttemptUtc { get; set; }

        public DateTime? NextAttemptUtc { get; set; }

        public string LastError { get; set; } = string.Empty;

        public DateTime? CompletedUtc { get; set; }
    }
}
