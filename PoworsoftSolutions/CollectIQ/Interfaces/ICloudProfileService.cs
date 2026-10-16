using CollectIQ.Models.Cloud;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Performs small authenticated profile operations against the CollectIQ cloud database.
    /// </summary>
    public interface ICloudProfileService
    {
        Task<CloudConnectionResult> TestConnectionAsync(string? expectedProviderUserId = null);
    }
}
