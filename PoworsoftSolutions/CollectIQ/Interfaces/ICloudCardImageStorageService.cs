using CollectIQ.Models;
using CollectIQ.Models.Cloud;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Uploads and downloads private card images through Supabase Storage.
    /// </summary>
    public interface ICloudCardImageStorageService
    {
        string LastOperationSummary { get; }

        Task<CloudCardImageRefs> PrepareUploadAsync(
            Card card,
            string authenticatedUserId,
            string accessToken);

        Task PopulateLocalImagesFromCloudAsync(
            Card card,
            CloudCardImageRefs cloudRefs,
            string accessToken);
    }
}
