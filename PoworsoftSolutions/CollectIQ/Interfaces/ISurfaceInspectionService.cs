using CollectIQ.Models.Inspection;
using CollectIQ.Models.Inspection.Geometry;

namespace CollectIQ.Interfaces
{
    public interface ISurfaceInspectionService
    {
        Task<SurfaceInspectionResult> AnalyzeAsync(
            string neutralReferencePath,
            IReadOnlyDictionary<SurfaceLightDirection, string> captures,
            IReadOnlyList<CardPoint>? manualReferenceCorners = null,
            IProgress<SurfaceInspectionProgress>? progress = null,
            CancellationToken cancellationToken = default);

        Task<SurfaceInspectionResult> AnalyzeSinglePhotoAsync(
            string imagePath,
            CancellationToken cancellationToken = default);

        Task<SurfaceInspectionResult> AnalyzeTiltSweepAsync(
            IReadOnlyList<string> captures,
            CancellationToken cancellationToken = default);
    }
}
