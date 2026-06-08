/*
* FILE: SqliteDatabase.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-25
* UPDATED: 2026-02-21
* DESCRIPTION:
*     Provides a concrete SQLite data access implementation for CollectIQ.
*     Implements the IDatabase interface for CRUD operations on user profiles,
*     authentication, and card collection management.
*
*     UPDATE (2026-02-21):
*     - Added defensive schema migration for Card enum persistence.
*       We now store Card.Sport as an INT column (SportValue) to prevent
*       SQLite-net from Enum.Parse(""), which was breaking collection loads.
*     - Migration adds SportValue column if missing.
*     - GetAllCardsAsync is hardened to log and fail gracefully.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CollectIQ.Interfaces;
using CollectIQ.Models;
using SQLite;

namespace CollectIQ.Services
{
    /// <summary>
    /// SQLite-backed implementation of IDatabase for user authentication
    /// and card collection persistence.
    /// </summary>
    public sealed class SqliteDatabase : IDatabase
    {
        private SQLiteAsyncConnection? connection;

        // ============================================================
        //  INITIALIZATION
        // ============================================================

        /// <summary>
        /// Initializes the database connection and creates tables if they do not exist.
        /// Also performs lightweight schema migrations needed for newer models.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (connection != null)
            {
                return;
            }

            string dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "collectiq.db3");

            connection = new SQLiteAsyncConnection(dbPath);

            await connection.CreateTableAsync<UserProfile>();
            await connection.CreateTableAsync<Card>();

            // Keep schema compatible with older app installs.
            await EnsureCardSchemaAsync();
        }

        /// <summary>
        /// Ensures Card table has the columns required by the current Card model.
        /// This is intentionally "small" migration logic (no full migration framework).
        /// </summary>
        private async Task EnsureCardSchemaAsync()
        {
            await InitializeAsync();

            try
            {
                // We only need to detect whether SportValue exists.
                // If it doesn't, inserts/updates will fail even if reads happen to work.
                List<TableInfoRow> cols = await connection!
                    .QueryAsync<TableInfoRow>("PRAGMA table_info('Card');");

                bool hasSportValue = cols.Any(c =>
                    string.Equals(c.name, "SportValue", StringComparison.OrdinalIgnoreCase));

                if (!hasSportValue)
                {
                    Debug.WriteLine("[SqliteDatabase] Migrating Card table: adding SportValue (INTEGER DEFAULT 0)");

                    // Add the column.
                    await connection.ExecuteAsync(
                        "ALTER TABLE Card ADD COLUMN SportValue INTEGER NOT NULL DEFAULT 0;");

                    // If an older Sport column exists, it was typically stored as TEXT.
                    // We don't attempt to map old strings to enum ints here.
                    // Leaving SportValue at 0 (Unknown) is safer than risking parse errors.
                }
            }
            catch (Exception ex)
            {
                // If this fails, we still want the app to run.
                Debug.WriteLine("[SqliteDatabase] EnsureCardSchemaAsync failed: " + ex.Message);
            }
        }

        private sealed class TableInfoRow
        {
            public int cid { get; set; }
            public string name { get; set; } = string.Empty;
            public string type { get; set; } = string.Empty;
            public int notnull { get; set; }
            public string dflt_value { get; set; } = string.Empty;
            public int pk { get; set; }
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
            return await connection!.Table<UserProfile>().FirstOrDefaultAsync();
        }

        /// <summary>
        /// Retrieves a user profile by email.
        /// </summary>
        public async Task<UserProfile?> GetUserProfileByEmailAsync(string email)
        {
            await InitializeAsync();

            return await connection!.Table<UserProfile>()
                .Where(u => u.Email == email)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Inserts or updates a user profile record.
        /// </summary>
        public async Task UpsertUserProfileAsync(UserProfile profile)
        {
            await InitializeAsync();
            await connection!.InsertOrReplaceAsync(profile);
        }

        /// <summary>
        /// Stores a hashed password for the given email.
        /// NOTE: This currently reuses DisplayName as a temporary storage location.
        /// </summary>
        public async Task StorePasswordHashAsync(string email, string passwordHash)
        {
            await InitializeAsync();

            UserProfile? existing = await GetUserProfileByEmailAsync(email);

            if (existing == null)
            {
                existing = new UserProfile { Email = email };
                await connection!.InsertAsync(existing);
            }

            existing.DisplayName = passwordHash;
            await connection!.UpdateAsync(existing);
        }

        /// <summary>
        /// Retrieves the stored password hash for the specified user.
        /// </summary>
        public async Task<string?> GetPasswordHashAsync(string email)
        {
            await InitializeAsync();
            UserProfile? profile = await GetUserProfileByEmailAsync(email);
            return profile?.DisplayName;
        }

        // ============================================================
        //  GENERIC CRUD METHODS
        // ============================================================

        /// <summary>
        /// Inserts or replaces a generic entity (used for any BaseModel-derived model).
        /// </summary>
        public async Task UpsertAsync<T>(T entity) where T : BaseModel, new()
        {
            await InitializeAsync();
            await connection!.InsertOrReplaceAsync(entity);
        }

        /// <summary>
        /// Deletes a record by ID.
        /// </summary>
        public async Task DeleteAsync<T>(string id) where T : BaseModel, new()
        {
            await InitializeAsync();
            await connection!.DeleteAsync<T>(id);
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
            return await connection!.InsertAsync(card);
        }

        /// <summary>
        /// Retrieves all cards from the collection.
        /// </summary>
        public async Task<List<Card>> GetAllCardsAsync()
        {
            await InitializeAsync();

            try
            {
                // Fast path.
                return await connection!.Table<Card>().ToListAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[SqliteDatabase] GetAllCardsAsync FAILED: " + ex);

                // Defensive fallback: return empty instead of crashing UI.
                // If we ever need a recovery path, we can read raw rows and salvage them.
                return new List<Card>();
            }
        }

        /// <summary>
        /// Deletes a card record from the collection.
        /// </summary>
        public async Task<int> DeleteCardAsync(string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId))
            {
                return 0;
            }

            await InitializeAsync();

            try
            {
                // CardImage table might not exist in early builds; ignore failures.
                try
                {
                    await connection!.ExecuteAsync("DELETE FROM CardImage WHERE CardId = ?", cardId);
                }
                catch (Exception imageEx)
                {
                    Debug.WriteLine("[SqliteDatabase] DeleteCardAsync: CardImage cleanup skipped: " + imageEx.Message);
                }

                return await connection!.ExecuteAsync("DELETE FROM Card WHERE Id = ?", cardId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[SqliteDatabase] DeleteCardAsync failed: " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Updates an existing card record in the SQLite collection.
        /// </summary>
        public async Task<int> UpdateCardAsync(Card card)
        {
            await InitializeAsync();

            if (card == null)
            {
                throw new ArgumentNullException(nameof(card), "Cannot update a null card entity.");
            }

            if (string.IsNullOrWhiteSpace(card.Id))
            {
                throw new ArgumentException("Card must have a valid ID before updating.", nameof(card));
            }

            try
            {
                return await connection!.UpdateAsync(card);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[SqliteDatabase] UpdateCardAsync failed: " + ex.Message);
                return 0;
            }
        }

        public Task<bool> UserExistsAsync(string email)
        {
            throw new NotImplementedException();
        }
    }
}
