using CollectIQ.Models.Inspection;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Processes a neutral geometry reference plus four directional card
    /// photographs into surface-inspection images.
    /// </summary>
    public interface ISurfaceInspectionService
    {
        Task<SurfaceInspectionResult> AnalyzeAsync(
            string neutralReferencePath,
            IReadOnlyDictionary<SurfaceLightDirection, string> captures,
            CancellationToken cancellationToken = default);
    }
}
