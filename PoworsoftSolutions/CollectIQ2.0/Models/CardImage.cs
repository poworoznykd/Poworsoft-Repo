/******************************************************************************
 *
 * FILE          : CardImage.cs
 * PROJECT       : CollectIQ
 * PROGRAMMER    : Darryl Poworoznyk
 * FIRST VERSION : 2026-06-02
 *
 * DESCRIPTION:
 *
 * Represents an image associated with a card.
 *
 *****************************************************************************/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a card image.
    /// </summary>
    [Table("CardImage")]
    public class CardImage
    {
        /// <summary>
        /// Gets or sets the local SQLite image identifier.
        /// </summary>
        [PrimaryKey, AutoIncrement]
        public int LocalId { get; set; }

        /// <summary>
        /// Gets or sets the server-side image identifier.
        /// </summary>
        public string ServerId { get; set; }

        /// <summary>
        /// Gets or sets the related local card identifier.
        /// </summary>
        public int CardLocalId { get; set; }

        /// <summary>
        /// Gets or sets the image path.
        /// </summary>
        public string ImagePath { get; set; }

        /// <summary>
        /// Gets or sets the image type, such as Front or Back.
        /// </summary>
        public string ImageType { get; set; }

        /// <summary>
        /// Gets or sets the current synchronization status.
        /// </summary>
        public string SyncStatus { get; set; }

        /// <summary>
        /// Gets or sets the last synchronized date.
        /// </summary>
        public string LastSyncedAt { get; set; }

        /// <summary>
        /// Gets or sets whether this image has been deleted locally.
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