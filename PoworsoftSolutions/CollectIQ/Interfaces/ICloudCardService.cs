using CollectIQ.Models;
using CollectIQ.Models.Cloud;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Provides controlled card cloud operations while the production sync layer is introduced.
    /// </summary>
    public interface ICloudCardService
    {
        Task<CloudCardSyncResult> UpsertAndVerifyCardsAsync(
            CardCollection collection,
            IReadOnlyCollection<Card> cards);
    }
}
