/*
* FILE: SqliteDatabase.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-25
* DESCRIPTION:
*     Provides a concrete SQLite data access implementation for CollectIQ.
*     Implements the IDatabase interface for CRUD operations on user profiles,
*     authentication, and card collection management.
*/

using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using SQLite;
using CollectIQ.Interfaces;
using CollectIQ.Models;

namespace CollectIQ.Services
{
    /// <summary>
    /// SQLite-backed implementation of IDatabase for user authentication
    /// and card collection persistence.
    /// </summary>
    public sealed class SqliteDatabase : IDatabase
    {
        private SQLiteAsyncConnection? _connection;

        /// <summary>
        /// Initializes the database connection and creates tables if they do not exist.
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
            await _connection.CreateTableAsync<Card>();
            await _connection.CreateTableAsync<CardImage>();
        }

        // ============================================================
        //  USER PROFILE METHODS
        // ============================================================

        /// <summary>
        /// Retrieves the current user profile.
        /// </summary>
        public async Task<UserProfile?> GetUserProfileAsync()
        {
            await InitializeAsync();
            return await _connection!.Table<UserProfile>().FirstOrDefaultAsync();
        }

        /// <summary>
        /// Retrieves a user profile by email.
        /// </summary>
        public async Task<UserProfile?> GetUserProfileByEmailAsync(string email)
        {
            await InitializeAsync();
            return await _connection!.Table<UserProfile>()
                .Where(u => u.Email == email)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Inserts or updates a user profile record.
        /// </summary>
        public async Task UpsertUserProfileAsync(UserProfile profile)
        {
            await InitializeAsync();
            await _connection!.InsertOrReplaceAsync(profile);
        }

        /// <summary>
        /// Stores a hashed password for the given email.
        /// </summary>
        public async Task StorePasswordHashAsync(string email, string passwordHash)
        {
            await InitializeAsync();
            var existing = await GetUserProfileByEmailAsync(email);

            if (existing == null)
            {
                existing = new UserProfile { Email = email };
                await _connection!.InsertAsync(existing);
            }

            existing.DisplayName = passwordHash; // Temporary field reuse for password hash
            await _connection!.UpdateAsync(existing);
        }

        /// <summary>
        /// Retrieves the stored password hash for the specified user.
        /// </summary>
        public async Task<string?> GetPasswordHashAsync(string email)
        {
            await InitializeAsync();
            var profile = await GetUserProfileByEmailAsync(email);
            return profile?.DisplayName;
        }

        // ============================================================
        //  GENERIC CRUD METHODS
        // ============================================================

        /// <summary>
        /// Inserts or replaces a generic entity (used for any BaseEntity-derived model).
        /// </summary>
        public async Task UpsertAsync<T>(T entity) where T : BaseEntity, new()
        {
            await InitializeAsync();
            await _connection!.InsertOrReplaceAsync(entity);
        }

        /// <summary>
        /// Deletes a record by ID.
        /// </summary>
        public async Task DeleteAsync<T>(string id) where T : BaseEntity, new()
        {
            await InitializeAsync();
            await _connection!.DeleteAsync<T>(id);
        }

        // ============================================================
        //  CARD COLLECTION METHODS
        // ============================================================

        /// <summary>
        /// Inserts a card record into the collection.
        /// </summary>
        public async Task<int> AddCardAsync(Card card)
        {
            await InitializeAsync();
            return await _connection!.InsertAsync(card);
        }

        /// <summary>
        /// Inserts a card image associated with a specific card.
        /// </summary>
        public async Task<int> AddCardImageAsync(CardImage image)
        {
            await InitializeAsync();
            return await _connection!.InsertAsync(image);
        }

        /// <summary>
        /// Retrieves all cards from the collection.
        /// </summary>
        public async Task<List<Card>> GetAllCardsAsync()
        {
            await InitializeAsync();
            return await _connection!.Table<Card>().ToListAsync();
        }
    }
}
