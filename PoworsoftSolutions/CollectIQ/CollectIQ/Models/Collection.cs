using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// A user-owned collection (e.g., "PC – 90s inserts", "Basketball rookies").
    /// </summary>
    public sealed class Collection : BaseEntity
    {
        /// <summary>
        /// Owner user id (local UserProfile.Id).
        /// </summary>
        [Indexed]
        public string OwnerUserId { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable name of the collection.
        /// </summary>
        [Indexed]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Optional description or notes.
        /// </summary>
        public string? Description { get; set; }
    }
}
