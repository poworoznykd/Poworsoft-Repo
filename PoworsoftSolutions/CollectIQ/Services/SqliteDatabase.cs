using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CollectIQ.Interfaces;
using CollectIQ.Models;
using SQLite;

namespace CollectIQ.Services
{
    /// <summary>
    /// SQLite-net implementation of the IDatabase interface.
    /// </summary>
    public sealed class SqliteDatabase : IDatabase
    {
        private const string DbFileName = "collectiq.db3";
        private SQLiteAsyncConnection? _conn;

        /// <summary>
        /// Creates the SQLite connection, tables, and indices if not present.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_conn != null) return;

            var dbPath = Path.Combine(FileSystem.AppDataDirectory, DbFileName);
            _conn = new SQLiteAsyncConnection(
                dbPath,
                SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.SharedCache
            );

            await _conn.CreateTableAsync<UserProfile>();
            await _conn.CreateTableAsync<Collection>();
            await _conn.CreateTableAsync<CollectionShare>();
            await _conn.CreateTableAsync<Card>();
            await _conn.CreateTableAsync<CardImage>();
            await _conn.CreateTableAsync<PriceSnapshot>();
        }

        /// <summary>
        /// Generic upsert (insert or update) for any entity.
        /// </summary>
        public async Task UpsertAsync<T>(T entity) where T : BaseEntity, new()
        {
            EnsureReady();
            entity.UpdatedUtc = DateTime.UtcNow;

            var existing = await _conn!.FindAsync<T>(entity.Id);
            if (existing == null)
            {
                entity.CreatedUtc = DateTime.UtcNow;
                await _conn.InsertAsync(entity);
            }
            else
            {
                await _conn.UpdateAsync(entity);
            }
        }

        /// <summary>
        /// Soft-deletes an entity by id (sets IsDeleted=true).
        /// </summary>
        public async Task DeleteAsync<T>(string id) where T : BaseEntity, new()
        {
            EnsureReady();
            var item = await _conn!.FindAsync<T>(id);
            if (item != null)
            {
                item.IsDeleted = true;
                item.UpdatedUtc = DateTime.UtcNow;
                await _conn.UpdateAsync(item);
            }
        }

        public async Task UpsertUserProfileAsync(UserProfile profile)
        {
            EnsureReady();
            profile.UpdatedUtc = DateTime.UtcNow;

            var existing = await GetUserProfileAsync();
            if (existing != null)
            {
                profile.Id = existing.Id;
                await _conn!.UpdateAsync(profile);
            }
            else
            {
                profile.CreatedUtc = DateTime.UtcNow;
                await _conn!.InsertAsync(profile);
            }
        }

        public async Task<UserProfile?> GetUserProfileAsync()
        {
            EnsureReady();
            return await _conn!.Table<UserProfile>()
                               .OrderByDescending(u => u.UpdatedUtc)
                               .FirstOrDefaultAsync();
        }

        public async Task CreateCollectionAsync(Collection collection)
        {
            EnsureReady();
            collection.CreatedUtc = DateTime.UtcNow;
            collection.UpdatedUtc = DateTime.UtcNow;
            await _conn!.InsertAsync(collection);
        }

        public async Task<IReadOnlyList<Collection>> GetCollectionsAsync(string ownerUserId)
        {
            EnsureReady();
            var list = await _conn!.Table<Collection>()
                                   .Where(c => !c.IsDeleted && c.OwnerUserId == ownerUserId)
                                   .OrderBy(c => c.Name)
                                   .ToListAsync();
            return list;
        }

        public async Task AddCardAsync(Card card)
        {
            EnsureReady();
            card.CreatedUtc = DateTime.UtcNow;
            card.UpdatedUtc = DateTime.UtcNow;
            await _conn!.InsertAsync(card);
        }

        public async Task<IReadOnlyList<Card>> GetCardsByCollectionAsync(string collectionId)
        {
            EnsureReady();
            var list = await _conn!.Table<Card>()
                                   .Where(x => !x.IsDeleted && x.CollectionId == collectionId)
                                   .OrderByDescending(x => x.CreatedUtc)
                                   .ToListAsync();
            return list;
        }

        public async Task AddPriceSnapshotAsync(PriceSnapshot snapshot)
        {
            EnsureReady();
            snapshot.CreatedUtc = DateTime.UtcNow;
            snapshot.UpdatedUtc = DateTime.UtcNow;
            await _conn!.InsertAsync(snapshot);
        }

        public async Task AddCardImageAsync(CardImage image)
        {
            EnsureReady();
            image.CreatedUtc = DateTime.UtcNow;
            image.UpdatedUtc = DateTime.UtcNow;
            await _conn!.InsertAsync(image);
        }

        public async Task AddShareAsync(CollectionShare share)
        {
            EnsureReady();
            share.CreatedUtc = DateTime.UtcNow;
            share.UpdatedUtc = DateTime.UtcNow;
            await _conn!.InsertAsync(share);
        }

        public async Task<IReadOnlyList<CollectionShare>> GetSharesAsync(string collectionId)
        {
            EnsureReady();
            var list = await _conn!.Table<CollectionShare>()
                                   .Where(s => !s.IsDeleted && s.CollectionId == collectionId)
                                   .ToListAsync();
            return list;
        }

        private void EnsureReady()
        {
            if (_conn == null)
                throw new InvalidOperationException("Database not initialized. Call InitializeAsync() at app startup.");
        }
    }
}
