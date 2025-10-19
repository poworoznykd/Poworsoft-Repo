//
//  FILE            : IDatabase.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-20
//  DESCRIPTION     :
//      Interface defining persistent storage operations for CollectIQ.
//      Provides methods for authentication, user profiles, collections,
//      cards, and related records following SET coding standards.
//
using System.Collections.Generic;
using System.Threading.Tasks;
using CollectIQ.Models;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Defines persistent storage operations used throughout CollectIQ.
    /// </summary>
    public interface IDatabase
    {
        /// <summary>
        /// Initializes and creates tables as required.
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// Inserts or updates an entity in the database.
        /// </summary>
        Task UpsertAsync<T>(T entity) where T : BaseEntity, new();

        /// <summary>
        /// Deletes an entity by identifier.
        /// </summary>
        Task DeleteAsync<T>(string id) where T : BaseEntity, new();

        // -------------------------------
        //  Authentication / User Profile
        // -------------------------------
        Task UpsertUserProfileAsync(UserProfile profile);
        Task<UserProfile?> GetUserProfileAsync();
        Task<UserProfile?> GetUserProfileByEmailAsync(string email);

        Task StorePasswordHashAsync(string email, string passwordHash);
        Task<string?> GetPasswordHashAsync(string email);

        // -------------------------------
        //  Collections and Cards
        // -------------------------------
        Task CreateCollectionAsync(Collection collection);
        Task<IReadOnlyList<Collection>> GetCollectionsAsync(string ownerUserId);

        Task AddCardAsync(Card card);
        Task<IReadOnlyList<Card>> GetCardsByCollectionAsync(string collectionId);

        Task AddPriceSnapshotAsync(PriceSnapshot snapshot);
        Task AddCardImageAsync(CardImage image);

        Task AddShareAsync(CollectionShare share);
        Task<IReadOnlyList<CollectionShare>> GetSharesAsync(string collectionId);
    }
}
