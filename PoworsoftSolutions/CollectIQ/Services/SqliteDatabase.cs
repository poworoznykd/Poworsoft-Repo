//
//  FILE            : SqliteDatabase.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-20
//  DESCRIPTION     :
//      Implements IDatabase for SQLite-based persistence.
//      Handles all CRUD operations for authentication, users,
//      collections, and cards following SET coding standards.
//
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SQLite;
using CollectIQ.Interfaces;
using CollectIQ.Models;

namespace CollectIQ.Services
{
    /// <summary>
    /// Provides SQLite-based persistent storage for CollectIQ.
    /// </summary>
    public sealed class SqliteDatabase : IDatabase
    {
        private SQLiteAsyncConnection? _connection;
        private const string DatabaseFilename = "collectiq.db3";

        /// <summary>
        /// Ensures database connection and creates required tables.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_connection != null)
                return;

            string dbPath = Path.Combine(FileSystem.AppDataDirectory, DatabaseFilename);
            _connection = new SQLiteAsyncConnection(dbPath);

            await _connection.CreateTableAsync<UserProfile>();
            await _connection.CreateTableAsync<Collection>();
            await _connection.CreateTableAsync<Card>();
            await _connection.CreateTableAsync<CollectionShare>();
            await _connection.CreateTableAsync<CardImage>();
            await _connection.CreateTableAsync<PriceSnapshot>();
            await _connection.CreateTableAsync<PasswordRecord>();
        }

        /// <summary>
        /// Gets the current SQLite connection.
        /// </summary>
        private SQLiteAsyncConnection Conn
        {
            get
            {
                if (_connection == null)
                    throw new InvalidOperationException("Database not initialized.");
                return _connection;
            }
        }

        // -----------------------------
        // Generic CRUD
        // -----------------------------
        public async Task UpsertAsync<T>(T entity) where T : BaseEntity, new()
        {
            await Conn.InsertOrReplaceAsync(entity);
        }

        public async Task DeleteAsync<T>(string id) where T : BaseEntity, new()
        {
            await Conn.DeleteAsync<T>(id);
        }

        // -----------------------------
        // Authentication / User Profile
        // -----------------------------
        public async Task UpsertUserProfileAsync(UserProfile profile)
        {
            await Conn.InsertOrReplaceAsync(profile);
        }

        public async Task<UserProfile?> GetUserProfileAsync()
        {
            return await Conn.Table<UserProfile>().FirstOrDefaultAsync();
        }

        public async Task<UserProfile?> GetUserProfileByEmailAsync(string email)
        {
            return await Conn.Table<UserProfile>().Where(u => u.Email == email).FirstOrDefaultAsync();
        }

        public async Task StorePasswordHashAsync(string email, string passwordHash)
        {
            var record = new PasswordRecord { Email = email, PasswordHash = passwordHash };
            await Conn.InsertOrReplaceAsync(record);
        }

        public async Task<string?> GetPasswordHashAsync(string email)
        {
            var record = await Conn.Table<PasswordRecord>().Where(p => p.Email == email).FirstOrDefaultAsync();
            return record?.PasswordHash;
        }

        // -----------------------------
        // Collections and Cards
        // -----------------------------
        public async Task CreateCollectionAsync(Collection collection)
        {
            await Conn.InsertAsync(collection);
        }

        public async Task<IReadOnlyList<Collection>> GetCollectionsAsync(string ownerUserId)
        {
            return await Conn.Table<Collection>().Where(c => c.OwnerUserId == ownerUserId).ToListAsync();
        }

        public async Task AddCardAsync(Card card)
        {
            await Conn.InsertAsync(card);
        }

        public async Task<IReadOnlyList<Card>> GetCardsByCollectionAsync(string collectionId)
        {
            return await Conn.Table<Card>().Where(c => c.CollectionId == collectionId).ToListAsync();
        }

        public async Task AddPriceSnapshotAsync(PriceSnapshot snapshot)
        {
            await Conn.InsertAsync(snapshot);
        }

        public async Task AddCardImageAsync(CardImage image)
        {
            await Conn.InsertAsync(image);
        }

        public async Task AddShareAsync(CollectionShare share)
        {
            await Conn.InsertAsync(share);
        }

        public async Task<IReadOnlyList<CollectionShare>> GetSharesAsync(string collectionId)
        {
            return await Conn.Table<CollectionShare>().Where(s => s.CollectionId == collectionId).ToListAsync();
        }
    }

    /// <summary>
    /// Internal table used for storing email/password hash pairs.
    /// </summary>
    public sealed class PasswordRecord
    {
        [PrimaryKey, Indexed]
        public string Email { get; set; } = string.Empty;

        public string PasswordHash { get; set; } = string.Empty;
    }
}
