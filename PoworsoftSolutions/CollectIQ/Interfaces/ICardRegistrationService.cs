using CollectIQ.Models.Inspection;
using CollectIQ.Models.Inspection.Geometry;
using CollectIQ.Models.Inspection.Registration;

namespace CollectIQ.Interfaces
{
    public interface ICardRegistrationService
    {

        Task<RegisteredCardFrame> RegisterSingleFrameAsync(
            string imagePath,
            string key,
            string outputDirectory,
            IReadOnlyList<CardPoint>? manualCorners = null,
            IProgress<SurfaceInspectionProgress>? progress = null,
            CancellationToken cancellationToken = default);
        Task<CardRegistrationResult> RegisterAsync(
            IReadOnlyDictionary<string, string> captures,
            string referenceKey,
            string outputDirectory,
            IReadOnlyList<CardPoint>? manualReferenceCorners = null,
            IReadOnlyDictionary<string, IReadOnlyList<CardPoint>>? manualFrameCorners = null,
            bool stopAfterGeometry = false,
            IProgress<SurfaceInspectionProgress>? progress = null,
            CancellationToken cancellationToken = default);
    }
}
