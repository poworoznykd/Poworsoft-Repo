using CollectIQ.Models.Inspection.Geometry;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Shared first stage for Centering, Corners, Edges and Surface inspection.
    /// Finds the physical card once and perspective-normalizes it into a fixed
    /// 5:7 inspection coordinate system.
    /// </summary>
    public interface ICardNormalizationService
    {
        Task<CardNormalizationResult> NormalizeAsync(
            string imagePath,
            string outputDirectory,
            int normalizedWidth,
            int normalizedHeight,
            IProgress<CardNormalizationProgress>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates the normalized card from user-positioned outer-card lines.
        /// left/right/top/bottom are normalized 0..1 coordinates in the
        /// visually auto-oriented source image. Automatic rectangle detection
        /// is deliberately skipped.
        /// </summary>
        Task<CardNormalizationResult> NormalizeFromRelativeRectangleAsync(
            string imagePath,
            string outputDirectory,
            double left,
            double right,
            double top,
            double bottom,
            int normalizedWidth,
            int normalizedHeight,
            IProgress<CardNormalizationProgress>? progress = null,
            CancellationToken cancellationToken = default);
    }
}
