using CollectIQ.Models.Inspection.Geometry;
using ImageSharpRgbaImage = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;

namespace CollectIQ.Interfaces
{
    public interface ICardGeometryService
    {
        CardGeometryResult DetectCard(ImageSharpRgbaImage source);

        /// <summary>
        /// Locates the same card in a later capture using normalized corners
        /// from a neutral reference image as a strong geometric prior.
        /// </summary>
        CardGeometryResult DetectCardNearPrior(
            ImageSharpRgbaImage source,
            IReadOnlyList<CardPoint> normalizedPriorCorners);
    }
}
