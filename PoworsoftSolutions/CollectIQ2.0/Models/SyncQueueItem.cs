/******************************************************************************
 *
 * FILE          : SyncQueueItem.cs
 * PROJECT       : CollectIQ
 * PROGRAMMER    : Darryl Poworoznyk
 * FIRST VERSION : 2026-06-02
 *
 * DESCRIPTION:
 *
 * Represents a pending local change that must be synchronized to the server.
 *
 *****************************************************************************/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a pending sync operation.
    /// </summary>
    [Table("SyncQueue")]
    public class SyncQueueItem
    {
        /// <summary>
        /// Gets or sets the sync queue item identifier.
        /// </summary>
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the entity type, such as Card or Collection.
        /// </summary>
        public string EntityType { get; set; }

        /// <summary>
        /// Gets or sets the local entity identifier.
        /// </summary>
        public int LocalEntityId { get; set; }

        /// <summary>
        /// Gets or sets the operation type, such as Insert, Update, or Delete.
        /// </summary>
        public string Operation { get; set; }

        /// <summary>
        /// Gets or sets the serialized payload for the sync operation.
        /// </summary>
        public string PayloadJson { get; set; }

        /// <summary>
        /// Gets or sets the number of sync attempts.
        /// </summary>
        public int AttemptCount { get; set; }

        /// <summary>
        /// Gets or sets the last sync attempt date.
        /// </summary>
        public string LastAttemptAt { get; set; }

        /// <summary>
        /// Gets or sets the last sync error message.
        /// </summary>
        public string LastError { get; set; }

        /// <summary>
        /// Gets or sets the record creation date.
        /// </summary>
        public string CreatedAt { get; set; }
    }
}