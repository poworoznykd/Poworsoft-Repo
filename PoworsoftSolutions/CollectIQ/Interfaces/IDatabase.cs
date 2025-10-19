//
//  FILE            : IDatabase.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-19
//  DESCRIPTION     :
//      Defines the interface for SQLite data access methods used
//      throughout the CollectIQ application.
//
using System.Collections.Generic;
using System.Threading.Tasks;
using CollectIQ.Models;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Abstract interface for all database operations.
    /// </summary>
    public interface IDatabase
    {
        Task InitializeAsync();

        // Generic CRUD
        Task UpsertAsync<T>(T entity) where T : BaseEntity, new();
        Task DeleteAsync<T>(string id) where T : BaseEntity, new();

        // User account operations
        Task<UserProfile?> GetUserProfileByEmailAsync(string email);
        Task StorePasswordHashAsync(string email, string passwordHash);
        Task<string?> GetPasswordHashAsync(string email);
        Task UpsertUserProfileAsync(UserProfile profile);
        Task<UserProfile?> GetUserProfileAsync();
    }
}
