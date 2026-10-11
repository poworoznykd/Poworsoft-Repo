using CollectIQ.Models.Inspection;
using CollectIQ.Models.Inspection.Geometry;
using CollectIQ.Models.Inspection.Registration;

namespace CollectIQ.Interfaces
{
    public interface ICardRegistrationService
    {
        Task<CardRegistrationResult> RegisterAsync(
            IReadOnlyDictionary<string, string> captures,
            string referenceKey,
            string outputDirectory,
            IReadOnlyList<CardPoint>? manualReferenceCorners = null,
            IProgress<SurfaceInspectionProgress>? progress = null,
            CancellationToken cancellationToken = default);
    }
}
