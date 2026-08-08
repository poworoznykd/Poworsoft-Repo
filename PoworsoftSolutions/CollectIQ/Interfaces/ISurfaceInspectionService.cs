using CollectIQ.Models.Inspection;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Processes a set of directional card photographs into surface-inspection
    /// images. The first implementation is intentionally local/on-device.
    /// </summary>
    public interface ISurfaceInspectionService
    {
        Task<SurfaceInspectionResult> AnalyzeAsync(
            IReadOnlyDictionary<SurfaceLightDirection, string> captures,
            CancellationToken cancellationToken = default);
    }
}
