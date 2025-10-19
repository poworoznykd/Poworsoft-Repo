//
//  FILE            : SqliteDatabase.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-19
//  DESCRIPTION     :
//      Implements the local SQLite data access layer for user profiles
//      and authentication using SQLite.NET-PCL.
//
using System;
using System.IO;
using System.Threading.Tasks;
using SQLite;
using CollectIQ.Interfaces;
using CollectIQ.Models;

namespace CollectIQ.Services
{
    /// <summary>
    /// Concrete SQLite database implementation for CollectIQ.
    /// </summary>
    public sealed class SqliteDatabase : IDatabase
    {
        private SQLiteAsyncConnection? _connection;

        /// <summary>
        /// Initializes database tables if not already created.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_connection != null)
                return;

            string dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "collectiq.db3");

            _connection = new SQLiteAsyncConnection(dbPath);
            await _connection.CreateTableAsync<UserProfile>();
        }

        public async Task UpsertAsync<T>(T entity) where T : BaseEntity, new()
        {
            await InitializeAsync();
            await _connection!.InsertOrReplaceAsync(entity);
        }

        public async Task DeleteAsync<T>(string id) where T : BaseEntity, new()
        {
            await InitializeAsync();
            await _connection!.DeleteAsync<T>(id);
        }

        public async Task<UserProfile?> GetUserProfileByEmailAsync(string email)
        {
            await InitializeAsync();
            return await _connection!.Table<UserProfile>()
                .Where(u => u.Email == email)
                .FirstOrDefaultAsync();
        }

        public async Task StorePasswordHashAsync(string email, string passwordHash)
        {
            await InitializeAsync();
            var existing = await GetUserProfileByEmailAsync(email);
            if (existing == null)
            {
                existing = new UserProfile { Email = email };
                await _connection!.InsertAsync(existing);
            }

            existing.DisplayName = passwordHash; // TEMP store hashed pw in DisplayName for simplicity
            await _connection!.UpdateAsync(existing);
        }

        public async Task<string?> GetPasswordHashAsync(string email)
        {
            await InitializeAsync();
            var profile = await GetUserProfileByEmailAsync(email);
            return profile?.DisplayName;
        }

        public async Task UpsertUserProfileAsync(UserProfile profile)
        {
            await InitializeAsync();
            await _connection!.InsertOrReplaceAsync(profile);
        }

        public async Task<UserProfile?> GetUserProfileAsync()
        {
            await InitializeAsync();
            return await _connection!.Table<UserProfile>().FirstOrDefaultAsync();
        }
    }
}
