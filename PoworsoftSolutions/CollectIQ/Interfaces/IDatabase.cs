//
//  FILE            : IDatabase.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-19
//  UPDATED         : 2026-06-08
//  DESCRIPTION     :
//      Defines local database operations used by CollectIQ. This interface
//      keeps the current app working while introducing account, profile,
//      collection, and migration support for the long-term architecture.
//

using CollectIQ.Models;
using SQLite;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Abstract interface for local database operations.
    /// </summary>
    public interface IDatabase
    {
        Task InitializeAsync();
        Task<SQLiteAsyncConnection> GetConnectionAsync();
        string GetDatabasePath();

        // Generic CRUD
        Task UpsertAsync<T>(T entity) where T : BaseModel, new();
        Task DeleteAsync<T>(string id) where T : BaseModel, new();

        // Legacy user profile operations
        Task<UserProfile?> GetUserProfileByEmailAsync(string email);
        Task<UserProfile?> GetUserProfileByAccountIdAsync(string userAccountId);
        Task StorePasswordHashAsync(string email, string passwordHash);
        Task<string?> GetPasswordHashAsync(string email);
        Task UpsertUserProfileAsync(UserProfile profile);
        Task<UserProfile?> GetUserProfileAsync();
        Task<bool> UserExistsAsync(string email);

        // Account and credential operations
        Task<UserAccount?> GetUserAccountByEmailAsync(string email);
        Task<UserAccount> UpsertUserAccountAsync(UserAccount account);
        Task<UserCredential?> GetLocalCredentialAsync(string userAccountId);
        Task UpsertUserCredentialAsync(UserCredential credential);
        Task RecordLoginHistoryAsync(LoginHistory history);

        // Collection operations
        Task<CardCollection> GetOrCreateDefaultCollectionAsync(string userAccountId);
        Task<List<CardCollection>> GetCollectionsForUserAsync(string userAccountId);
        Task<int> RecoverOwnedDataForAccountAsync(string legacyUserAccountId, string canonicalUserAccountId);
        Task UpsertCollectionAsync(CardCollection collection);
        Task DeleteCollectionAsync(string collectionId);

        // Card collection operations
        Task<int> AddCardAsync(Card card);
        Task<int> UpdateCardAsync(Card card);
        Task<int> DeleteCardAsync(string cardId);
        Task<List<Card>> GetAllCardsAsync();
    }
}
