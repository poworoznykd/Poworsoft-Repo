namespace CollectIQ.Models.Inspection.Geometry
{
    /// <summary>
    /// Result of the common inspection geometry pipeline.
    /// Every inspection should begin from this normalized card instead of
    /// independently detecting/cropping the card.
    /// </summary>
    public sealed class CardNormalizationResult
    {
        public string SourcePreviewPath { get; init; } = string.Empty;

        /// <summary>
        /// Card Lock View: the source preview with the detected physical-card
        /// quadrilateral and corner markers drawn on top.
        /// </summary>
        public string DetectionOverlayPath { get; init; } = string.Empty;

        public string NormalizedImagePath { get; init; } = string.Empty;
        public CardPoint[] SourceCorners { get; init; } = Array.Empty<CardPoint>();
        public double GeometryConfidence { get; init; }
        public int NormalizedWidth { get; init; }
        public int NormalizedHeight { get; init; }

        // Physical card location inside the padded TrueForm image.
        public int NormalizedCardLeft { get; init; }
        public int NormalizedCardRight { get; init; }
        public int NormalizedCardTop { get; init; }
        public int NormalizedCardBottom { get; init; }
    }
}
