using CollectIQ.Models.Inspection;

namespace CollectIQ.Interfaces
{
    public interface ISurfaceInspectionService
    {
        Task<SurfaceInspectionResult> AnalyzeAsync(
            string neutralReferencePath,
            IReadOnlyDictionary<SurfaceLightDirection, string> captures,
            CancellationToken cancellationToken = default);

        Task<SurfaceInspectionResult> AnalyzeSinglePhotoAsync(
            string imagePath,
            CancellationToken cancellationToken = default);

        Task<SurfaceInspectionResult> AnalyzeTiltSweepAsync(
            IReadOnlyList<string> captures,
            CancellationToken cancellationToken = default);
    }
}
