/******************************************************************************
 *
 * FILE          : Collection.cs
 * PROJECT       : CollectIQ
 * PROGRAMMER    : Darryl Poworoznyk
 * FIRST VERSION : 2026-06-02
 *
 * DESCRIPTION:
 *
 * Represents a user-created card collection.
 *
 * Collections allow users to organize cards, share groups of cards with other
 * users, and eventually list collections for sale.
 *
 *****************************************************************************/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a card collection.
    /// </summary>
    [Table("Collection")]
    public class Collection
    {
        /// <summary>
        /// Gets or sets the local SQLite collection identifier.
        /// </summary>
        [PrimaryKey, AutoIncrement]
        public int LocalId { get; set; }

        /// <summary>
        /// Gets or sets the server-side collection identifier.
        /// </summary>
        public string ServerId { get; set; }

        /// <summary>
        /// Gets or sets the server-side owner user identifier.
        /// </summary>
        public string OwnerServerUserId { get; set; }

        /// <summary>
        /// Gets or sets the collection name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the collection description.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets whether this is the user's default collection.
        /// </summary>
        public bool IsDefault { get; set; }

        /// <summary>
        /// Gets or sets whether the collection is shared.
        /// </summary>
        public bool IsShared { get; set; }

        /// <summary>
        /// Gets or sets whether this collection is listed for sale.
        /// </summary>
        public bool IsForSale { get; set; }

        /// <summary>
        /// Gets or sets the current synchronization status.
        /// </summary>
        public string SyncStatus { get; set; }

        /// <summary>
        /// Gets or sets the date this collection was last synchronized.
        /// </summary>
        public string LastSyncedAt { get; set; }

        /// <summary>
        /// Gets or sets whether this collection has been deleted locally.
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// Gets or sets the record creation date.
        /// </summary>
        public string CreatedAt { get; set; }

        /// <summary>
        /// Gets or sets the record update date.
        /// </summary>
        public string UpdatedAt { get; set; }
    }
}