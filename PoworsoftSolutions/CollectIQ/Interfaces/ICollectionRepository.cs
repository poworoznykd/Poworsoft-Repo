/*
* FILE            : ICollectionRepository.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Defines repository operations for user-owned card collections.
*/

using CollectIQ.Models;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Defines card collection repository operations.
    /// </summary>
    public interface ICollectionRepository
    {
        Task<CardCollection> GetOrCreateDefaultCollectionAsync(string userAccountId);
        Task<List<CardCollection>> GetCollectionsForUserAsync(string userAccountId);
        Task SaveAsync(CardCollection collection);
        Task DeleteAsync(string collectionId);
    }
}
