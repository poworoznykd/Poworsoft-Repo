using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Sharing metadata for a collection (by email) with permission level.
    /// </summary>
    public sealed class CollectionShare : BaseEntity
    {
        /// <summary>
        /// Collection id being shared.
        /// </summary>
        [Indexed]
        public string CollectionId { get; set; } = string.Empty;

        /// <summary>
        /// Email address of the invitee.
        /// </summary>
        [Indexed]
        public string InviteeEmail { get; set; } = string.Empty;

        /// <summary>
        /// Permission (viewer, editor, owner).
        /// </summary>
        [Indexed]
        public string Permission { get; set; } = "viewer";
    }
}
