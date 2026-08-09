using CollectIQ.Models.Inspection.Registration;

namespace CollectIQ.Interfaces
{
    public interface ICardRegistrationService
    {
        Task<CardRegistrationResult> RegisterAsync(
            IReadOnlyDictionary<string, string> captures,
            string referenceKey,
            string outputDirectory,
            CancellationToken cancellationToken = default);
    }
}
