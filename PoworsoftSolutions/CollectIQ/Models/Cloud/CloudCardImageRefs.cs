namespace CollectIQ.Models.Cloud
{
    /// <summary>
    /// Stores authenticated Supabase Storage object paths for a card.
    /// The paths are kept in the existing cards *_image_url columns.
    /// </summary>
    public sealed class CloudCardImageRefs
    {
        public string Front { get; set; } = string.Empty;
        public string FrontThumbnail { get; set; } = string.Empty;
        public string Back { get; set; } = string.Empty;
        public string FrontOverlay { get; set; } = string.Empty;
        public string BackOverlay { get; set; } = string.Empty;
    }
}
