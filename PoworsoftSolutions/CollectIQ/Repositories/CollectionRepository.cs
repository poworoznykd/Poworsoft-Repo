/*
* FILE            : CollectionRepository.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Repository for user-owned card collections.
*/

using CollectIQ.Interfaces;
using CollectIQ.Models;

namespace CollectIQ.Repositories
{
    /// <summary>
    /// Provides repository methods for card collections.
    /// </summary>
    public sealed class CollectionRepository : ICollectionRepository
    {
        private readonly IDatabase database;

        /// <summary>
        /// Initializes a new instance of the CollectionRepository class.
        /// </summary>
        /// <param name="database">The local database abstraction.</param>
        public CollectionRepository(IDatabase database)
        {
            this.database = database;
        }

        /// <summary>
        /// Gets or creates the default collection for a user.
        /// </summary>
        /// <param name="userAccountId">The user account ID.</param>
        /// <returns>The default collection.</returns>
        public Task<CardCollection> GetOrCreateDefaultCollectionAsync(string userAccountId)
        {
            return database.GetOrCreateDefaultCollectionAsync(userAccountId);
        }

        /// <summary>
        /// Gets all collections available to a user.
        /// </summary>
        /// <param name="userAccountId">The user account ID.</param>
        /// <returns>The user's collections.</returns>
        public Task<List<CardCollection>> GetCollectionsForUserAsync(string userAccountId)
        {
            return database.GetCollectionsForUserAsync(userAccountId);
        }

        /// <summary>
        /// Saves a collection.
        /// </summary>
        /// <param name="collection">The collection to save.</param>
        public Task SaveAsync(CardCollection collection)
        {
            return database.UpsertCollectionAsync(collection);
        }

        /// <summary>
        /// Deletes a collection.
        /// </summary>
        /// <param name="collectionId">The collection ID.</param>
        public Task DeleteAsync(string collectionId)
        {
            return database.DeleteCollectionAsync(collectionId);
        }
    }
}
