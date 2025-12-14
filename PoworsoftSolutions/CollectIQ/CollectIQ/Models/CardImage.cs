using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Image path(s) for a card (front/back/scans).
    /// </summary>
    public sealed class CardImage : BaseEntity
    {
        /// <summary>
        /// Card id.
        /// </summary>
        [Indexed]
        public string CardId { get; set; } = string.Empty;

        /// <summary>
        /// Local file path or URI.
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Kind (front, back, slab, raw, ebay, etc.).
        /// </summary>
        [Indexed]
        public string Kind { get; set; } = "front";
    }
}
