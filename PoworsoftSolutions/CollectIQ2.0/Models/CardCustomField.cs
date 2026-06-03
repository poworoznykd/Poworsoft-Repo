/******************************************************************************
 *
 * FILE          : CardCustomField.cs
 * PROJECT       : CollectIQ
 * PROGRAMMER    : Darryl Poworoznyk
 * FIRST VERSION : 2026-06-02
 *
 * DESCRIPTION:
 *
 * Represents a flexible custom field attached to a card.
 *
 *****************************************************************************/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a custom field for a card.
    /// </summary>
    [Table("CardCustomField")]
    public class CardCustomField
    {
        /// <summary>
        /// Gets or sets the local SQLite custom field identifier.
        /// </summary>
        [PrimaryKey, AutoIncrement]
        public int LocalId { get; set; }

        /// <summary>
        /// Gets or sets the server-side custom field identifier.
        /// </summary>
        public string ServerId { get; set; }

        /// <summary>
        /// Gets or sets the related local card identifier.
        /// </summary>
        public int CardLocalId { get; set; }

        /// <summary>
        /// Gets or sets the custom field name.
        /// </summary>
        public string FieldName { get; set; }

        /// <summary>
        /// Gets or sets the custom field value.
        /// </summary>
        public string FieldValue { get; set; }

        /// <summary>
        /// Gets or sets the custom field data type.
        /// </summary>
        public string FieldType { get; set; }

        /// <summary>
        /// Gets or sets the current synchronization status.
        /// </summary>
        public string SyncStatus { get; set; }

        /// <summary>
        /// Gets or sets the last synchronized date.
        /// </summary>
        public string LastSyncedAt { get; set; }

        /// <summary>
        /// Gets or sets whether this custom field has been deleted locally.
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