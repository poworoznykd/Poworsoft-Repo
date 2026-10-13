namespace CollectIQ.Models.Inspection.Geometry
{
    /// <summary>
    /// User-visible progress emitted by the shared physical-card normalization
    /// pipeline. Optional image paths allow the UI to reveal intermediate
    /// checkpoints before the entire inspection finishes.
    /// </summary>
    public sealed class CardNormalizationProgress
    {
        public string Stage { get; init; } = string.Empty;

        public string Message { get; init; } = string.Empty;

        public string? PreviewImagePath { get; init; }

        public string? EdgeImagePath { get; init; }

        public string? RectangleImagePath { get; init; }

        public string? CardLockImagePath { get; init; }

        public string? TrueFormImagePath { get; init; }
    }
}
