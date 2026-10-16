using CollectIQ.Models;
using CollectIQ.Models.Cloud;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Provides controlled cloud collection operations used while the sync layer is being introduced.
    /// </summary>
    public interface ICloudCollectionService
    {
        Task<CloudCollectionSyncResult> UpsertAndVerifyCollectionAsync(CardCollection collection);
    }
}
