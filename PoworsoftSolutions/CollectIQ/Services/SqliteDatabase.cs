/*
* FILE            : SqliteDatabase.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2025-10-25
* UPDATED         : 2026-06-08
* DESCRIPTION     :
*     Provides the local SQLite data layer for CollectIQ. This version keeps the
*     existing Card/UserProfile workflow working while adding a cleaner database
*     foundation for accounts, credentials, roles, collections, sharing,
*     marketplace, rewards, synchronization, audit history, and migrations.
*/

using System.Diagnostics;
using CollectIQ.Interfaces;
using CollectIQ.Models;
using SQLite;

namespace CollectIQ.Services
{
    /// <summary>
    /// SQLite-backed implementation of IDatabase for CollectIQ.
    /// </summary>
    public sealed class SqliteDatabase : IDatabase
    {
        private const int CurrentDatabaseVersion = 1;
        private const string InitialMigrationName = "20260608_InitialCollectIQFoundation";
        private SQLiteAsyncConnection? connection;
        private bool isInitialized;
        private readonly SemaphoreSlim initializeLock = new SemaphoreSlim(1, 1);

        #region Initialization

        /// <summary>
        /// Initializes the SQLite database and applies required schema setup.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (isInitialized)
            {
                return;
            }

            await initializeLock.WaitAsync();

            try
            {
                if (isInitialized)
                {
                    return;
                }

                string dbPath = GetDatabasePath();

                Debug.WriteLine($"[CollectIQ DB] Path: {dbPath}");

                connection = new SQLiteAsyncConnection(dbPath);

                await CreateTablesAsync();
                await EnsureCardSchemaAsync();
                await SeedRolesAndPlansAsync();
                await RecordInitialMigrationAsync();

                isInitialized = true;
            }
            finally
            {
                initializeLock.Release();
            }
        }

        /// <summary>
        /// Gets the initialized SQLite connection.
        /// </summary>
        /// <returns>The SQLite connection.</returns>
        public async Task<SQLiteAsyncConnection> GetConnectionAsync()
        {
            await InitializeAsync();
            return connection!;
        }

        /// <summary>
        /// Gets the local SQLite database path used by CollectIQ.
        /// </summary>
        /// <returns>The full local database path.</returns>
        public string GetDatabasePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "collectiq.db3");
        }

        /// <summary>
        /// Creates all local database tables required by CollectIQ.
        /// </summary>
        private async Task CreateTablesAsync()
        {
            await connection!.CreateTableAsync<UserAccount>();
            await connection.CreateTableAsync<UserProfile>();
            await connection.CreateTableAsync<UserCredential>();
            await connection.CreateTableAsync<UserSessionRecord>();
            await connection.CreateTableAsync<Role>();
            await connection.CreateTableAsync<Permission>();
            await connection.CreateTableAsync<UserRoleLink>();
            await connection.CreateTableAsync<RolePermissionLink>();
            await connection.CreateTableAsync<LoginHistory>();

            await connection.CreateTableAsync<CardCollection>();
            await connection.CreateTableAsync<CollectionMember>();
            await connection.CreateTableAsync<CollectionInvite>();
            await connection.CreateTableAsync<Card>();
            await connection.CreateTableAsync<CollectionCard>();
            await connection.CreateTableAsync<CardImage>();
            await connection.CreateTableAsync<CardInsightRecord>();

            await connection.CreateTableAsync<SubscriptionPlan>();
            await connection.CreateTableAsync<UserSubscription>();
            await connection.CreateTableAsync<RewardAccount>();
            await connection.CreateTableAsync<RewardTransaction>();

            await connection.CreateTableAsync<MarketplaceListing>();
            await connection.CreateTableAsync<MarketplaceOffer>();
            await connection.CreateTableAsync<MarketplaceTransaction>();
            await connection.CreateTableAsync<WatchListItem>();
            await connection.CreateTableAsync<FavoriteItem>();

            await connection.CreateTableAsync<SyncQueueItem>();
            await connection.CreateTableAsync<AuditHistory>();
            await connection.CreateTableAsync<SchemaMigrationHistory>();
        }

        /// <summary>
        /// Ensures the legacy Card table contains newer columns required by existing UI.
        /// </summary>
        private async Task EnsureCardSchemaAsync()
        {
            try
            {
                List<TableInfoRow> columns = await connection!
                    .QueryAsync<TableInfoRow>("PRAGMA table_info('Card');");

                await AddColumnIfMissingAsync(columns, "SportValue", "ALTER TABLE Card ADD COLUMN SportValue INTEGER NOT NULL DEFAULT 0;");
                await AddColumnIfMissingAsync(columns, "CollectionId", "ALTER TABLE Card ADD COLUMN CollectionId TEXT NOT NULL DEFAULT ''; ");
                await AddColumnIfMissingAsync(columns, "FrontThumbnailPath", "ALTER TABLE Card ADD COLUMN FrontThumbnailPath TEXT;");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CollectIQ DB] EnsureCardSchemaAsync failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Adds a column when it does not already exist.
        /// </summary>
        /// <param name="columns">The current table columns.</param>
        /// <param name="columnName">The column name.</param>
        /// <param name="sql">The ALTER TABLE SQL statement.</param>
        private async Task AddColumnIfMissingAsync(List<TableInfoRow> columns, string columnName, string sql)
        {
            bool exists = columns.Any(c => string.Equals(c.name, columnName, StringComparison.OrdinalIgnoreCase));

            if (!exists)
            {
                Debug.WriteLine($"[CollectIQ DB] Adding missing Card column: {columnName}");
                await connection!.ExecuteAsync(sql);
            }
        }

        /// <summary>
        /// Seeds default roles and subscription plans needed by the app.
        /// </summary>
        private async Task SeedRolesAndPlansAsync()
        {
            await SeedRoleAsync(UserRoles.Admin, "Full access to local CollectIQ functionality.");
            await SeedRoleAsync(UserRoles.Regular, "Standard signed-in CollectIQ user.");
            await SeedRoleAsync(UserRoles.Guest, "Temporary guest user with limited access.");

            SubscriptionPlan? freePlan = await connection!.Table<SubscriptionPlan>()
                .Where(p => p.Name == "Free")
                .FirstOrDefaultAsync();

            if (freePlan == null)
            {
                freePlan = new SubscriptionPlan
                {
                    Name = "Free",
                    Description = "Default local CollectIQ plan.",
                    MaxCollections = 3,
                    MaxCards = 500,
                    AllowsMarketplace = false,
                    AllowsSharing = true,
                    IsActive = true
                };

                await connection.InsertAsync(freePlan);
            }
        }

        /// <summary>
        /// Seeds a role if it does not already exist.
        /// </summary>
        /// <param name="name">The role name.</param>
        /// <param name="description">The role description.</param>
        private async Task SeedRoleAsync(string name, string description)
        {
            Role? existingRole = await connection!.Table<Role>()
                .Where(r => r.Name == name)
                .FirstOrDefaultAsync();

            if (existingRole != null)
            {
                return;
            }

            await connection.InsertAsync(new Role
            {
                Name = name,
                Description = description
            });
        }

        /// <summary>
        /// Records the initial migration once the foundation tables exist.
        /// </summary>
        private async Task RecordInitialMigrationAsync()
        {
            SchemaMigrationHistory? existing = await connection!.Table<SchemaMigrationHistory>()
                .Where(m => m.MigrationName == InitialMigrationName)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                return;
            }

            await connection.InsertAsync(new SchemaMigrationHistory
            {
                MigrationName = InitialMigrationName,
                DatabaseVersion = CurrentDatabaseVersion,
                AppVersion = "LocalFoundation",
                AppliedUtc = DateTime.UtcNow
            });
        }

        #endregion

        #region Generic CRUD

        /// <summary>
        /// Inserts or replaces a persisted entity.
        /// </summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <param name="entity">The entity to save.</param>
        public async Task UpsertAsync<T>(T entity) where T : BaseModel, new()
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            await InitializeAsync();

            DateTime now = DateTime.UtcNow;

            if (entity.CreatedUtc == default)
            {
                entity.CreatedUtc = now;
            }

            entity.UpdatedUtc = now;

            await connection!.InsertOrReplaceAsync(entity);
        }

        /// <summary>
        /// Deletes a persisted entity by ID.
        /// </summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <param name="id">The entity ID.</param>
        public async Task DeleteAsync<T>(string id) where T : BaseModel, new()
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            await InitializeAsync();
            await connection!.DeleteAsync<T>(id);
        }

        #endregion

        #region User Account and Profile Methods

        /// <summary>
        /// Gets the first local user profile. Legacy helper used by older screens.
        /// </summary>
        /// <returns>The first profile, or null when none exists.</returns>
        public async Task<UserProfile?> GetUserProfileAsync()
        {
            await InitializeAsync();
            return await connection!.Table<UserProfile>().FirstOrDefaultAsync();
        }

        /// <summary>
        /// Gets a user profile by email address.
        /// </summary>
        /// <param name="email">The email address.</param>
        /// <returns>The matching profile, or null.</returns>
        public async Task<UserProfile?> GetUserProfileByEmailAsync(string email)
        {
            await InitializeAsync();

            string normalizedEmail = NormalizeEmail(email);

            return await connection!.Table<UserProfile>()
                .Where(u => u.Email == normalizedEmail)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Inserts or updates a user profile.
        /// </summary>
        /// <param name="profile">The profile to save.</param>
        public async Task UpsertUserProfileAsync(UserProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            await InitializeAsync();

            if (!string.IsNullOrWhiteSpace(profile.Email))
            {
                profile.Email = NormalizeEmail(profile.Email);
            }

            profile.Role = UserRoles.Normalize(profile.Role);
            profile.UpdatedUtc = DateTime.UtcNow;

            await connection!.InsertOrReplaceAsync(profile);
        }

        /// <summary>
        /// Gets a user account by email address.
        /// </summary>
        /// <param name="email">The email address.</param>
        /// <returns>The matching account, or null.</returns>
        public async Task<UserAccount?> GetUserAccountByEmailAsync(string email)
        {
            await InitializeAsync();

            string normalizedEmail = NormalizeEmail(email);

            return await connection!.Table<UserAccount>()
                .Where(u => u.EmailNormalized == normalizedEmail)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Inserts or updates a user account.
        /// </summary>
        /// <param name="account">The account to save.</param>
        /// <returns>The saved account.</returns>
        public async Task<UserAccount> UpsertUserAccountAsync(UserAccount account)
        {
            if (account == null)
            {
                throw new ArgumentNullException(nameof(account));
            }

            await InitializeAsync();

            account.EmailNormalized = NormalizeEmail(account.EmailNormalized);

            if (string.IsNullOrWhiteSpace(account.Email) && !string.IsNullOrWhiteSpace(account.EmailNormalized))
            {
                account.Email = account.EmailNormalized;
            }

            account.UpdatedUtc = DateTime.UtcNow;

            await connection!.InsertOrReplaceAsync(account);
            return account;
        }

        /// <summary>
        /// Gets a local password credential for an account.
        /// </summary>
        /// <param name="userAccountId">The user account ID.</param>
        /// <returns>The local credential, or null.</returns>
        public async Task<UserCredential?> GetLocalCredentialAsync(string userAccountId)
        {
            await InitializeAsync();

            return await connection!.Table<UserCredential>()
                .Where(c => c.UserAccountId == userAccountId && c.AuthProvider == "Local")
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Saves a user credential.
        /// </summary>
        /// <param name="credential">The credential to save.</param>
        public async Task UpsertUserCredentialAsync(UserCredential credential)
        {
            if (credential == null)
            {
                throw new ArgumentNullException(nameof(credential));
            }

            await InitializeAsync();
            credential.UpdatedUtc = DateTime.UtcNow;
            await connection!.InsertOrReplaceAsync(credential);
        }

        /// <summary>
        /// Stores a password hash for backward-compatible callers.
        /// </summary>
        /// <param name="email">The email address.</param>
        /// <param name="passwordHash">The password hash.</param>
        public async Task StorePasswordHashAsync(string email, string passwordHash)
        {
            await InitializeAsync();

            string normalizedEmail = NormalizeEmail(email);

            UserAccount? account = await GetUserAccountByEmailAsync(normalizedEmail);

            if (account == null)
            {
                account = new UserAccount
                {
                    Email = normalizedEmail,
                    EmailNormalized = normalizedEmail,
                    AccountStatus = AccountStatuses.Active
                };

                await UpsertUserAccountAsync(account);
            }

            UserCredential? credential = await GetLocalCredentialAsync(account.Id);

            if (credential == null)
            {
                credential = new UserCredential
                {
                    UserAccountId = account.Id,
                    AuthProvider = "Local"
                };
            }

            credential.PasswordHash = passwordHash;
            credential.PasswordAlgorithm = passwordHash.StartsWith("PBKDF2$", StringComparison.OrdinalIgnoreCase)
                ? "PBKDF2-SHA256-100000"
                : "Legacy-SHA256";
            credential.LastChangedUtc = DateTime.UtcNow;

            await UpsertUserCredentialAsync(credential);

            UserProfile? profile = await GetUserProfileByEmailAsync(normalizedEmail);

            if (profile == null)
            {
                profile = new UserProfile
                {
                    Email = normalizedEmail,
                    UserAccountId = account.Id,
                    DisplayName = normalizedEmail,
                    Role = UserRoles.Regular,
                    CreatedUtc = DateTime.UtcNow
                };
            }

            profile.PasswordHash = passwordHash;
            profile.UserAccountId = account.Id;
            profile.UpdatedUtc = DateTime.UtcNow;

            await UpsertUserProfileAsync(profile);
        }

        /// <summary>
        /// Gets a password hash for backward-compatible callers.
        /// </summary>
        /// <param name="email">The email address.</param>
        /// <returns>The stored hash, or null.</returns>
        public async Task<string?> GetPasswordHashAsync(string email)
        {
            await InitializeAsync();

            UserAccount? account = await GetUserAccountByEmailAsync(email);

            if (account != null)
            {
                UserCredential? credential = await GetLocalCredentialAsync(account.Id);

                if (!string.IsNullOrWhiteSpace(credential?.PasswordHash))
                {
                    return credential.PasswordHash;
                }
            }

            UserProfile? profile = await GetUserProfileByEmailAsync(email);
            return profile?.PasswordHash ?? profile?.DisplayName;
        }

        /// <summary>
        /// Checks whether a user exists by email address.
        /// </summary>
        /// <param name="email">The email address.</param>
        /// <returns>True if the user exists; otherwise false.</returns>
        public async Task<bool> UserExistsAsync(string email)
        {
            await InitializeAsync();
            return await GetUserAccountByEmailAsync(email) != null || await GetUserProfileByEmailAsync(email) != null;
        }

        /// <summary>
        /// Records a login attempt.
        /// </summary>
        /// <param name="history">The login history record.</param>
        public async Task RecordLoginHistoryAsync(LoginHistory history)
        {
            if (history == null)
            {
                return;
            }

            await InitializeAsync();
            await connection!.InsertAsync(history);
        }

        #endregion

        #region Collection Methods

        /// <summary>
        /// Gets or creates the default collection for a user.
        /// </summary>
        /// <param name="userAccountId">The user account ID.</param>
        /// <returns>The user's default collection.</returns>
        public async Task<CardCollection> GetOrCreateDefaultCollectionAsync(string userAccountId)
        {
            await InitializeAsync();

            if (string.IsNullOrWhiteSpace(userAccountId))
            {
                userAccountId = "local";
            }

            CardCollection? collection = await connection!.Table<CardCollection>()
                .Where(c => c.OwnerUserAccountId == userAccountId && c.IsDefault && !c.IsDeleted)
                .FirstOrDefaultAsync();

            if (collection != null)
            {
                return collection;
            }

            collection = new CardCollection
            {
                OwnerUserAccountId = userAccountId,
                Name = "My Collection",
                Description = "Default CollectIQ collection.",
                Visibility = CollectionVisibility.Private,
                IsDefault = true,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };

            await connection.InsertAsync(collection);

            CollectionMember member = new CollectionMember
            {
                CollectionId = collection.Id,
                UserAccountId = userAccountId,
                CollectionRole = "Owner",
                CanView = true,
                CanAddCards = true,
                CanEditCards = true,
                CanDeleteCards = true,
                CanInvite = true
            };

            await connection.InsertAsync(member);

            await AssignUnassignedCardsToCollectionAsync(collection.Id);

            return collection;
        }

        /// <summary>
        /// Gets collections owned by or shared with a user.
        /// </summary>
        /// <param name="userAccountId">The user account ID.</param>
        /// <returns>The collection list.</returns>
        public async Task<List<CardCollection>> GetCollectionsForUserAsync(string userAccountId)
        {
            await InitializeAsync();

            List<CardCollection> owned = await connection!.Table<CardCollection>()
                .Where(c => c.OwnerUserAccountId == userAccountId && !c.IsDeleted)
                .ToListAsync();

            List<CollectionMember> memberships = await connection.Table<CollectionMember>()
                .Where(m => m.UserAccountId == userAccountId && !m.IsDeleted)
                .ToListAsync();

            HashSet<string> knownIds = new HashSet<string>(owned.Select(c => c.Id));

            foreach (CollectionMember membership in memberships)
            {
                if (knownIds.Contains(membership.CollectionId))
                {
                    continue;
                }

                CardCollection? sharedCollection = await connection.Table<CardCollection>()
                    .Where(c => c.Id == membership.CollectionId && !c.IsDeleted)
                    .FirstOrDefaultAsync();

                if (sharedCollection != null)
                {
                    owned.Add(sharedCollection);
                    knownIds.Add(sharedCollection.Id);
                }
            }

            return owned.OrderByDescending(c => c.IsDefault).ThenBy(c => c.Name).ToList();
        }

        /// <summary>
        /// Saves a collection.
        /// </summary>
        /// <param name="collection">The collection to save.</param>
        public async Task UpsertCollectionAsync(CardCollection collection)
        {
            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            await InitializeAsync();
            collection.UpdatedUtc = DateTime.UtcNow;
            await connection!.InsertOrReplaceAsync(collection);
        }

        /// <summary>
        /// Soft deletes a collection.
        /// </summary>
        /// <param name="collectionId">The collection ID.</param>
        public async Task DeleteCollectionAsync(string collectionId)
        {
            if (string.IsNullOrWhiteSpace(collectionId))
            {
                return;
            }

            await InitializeAsync();

            CardCollection? collection = await connection!.Table<CardCollection>()
                .Where(c => c.Id == collectionId)
                .FirstOrDefaultAsync();

            if (collection == null)
            {
                return;
            }

            collection.IsDeleted = true;
            collection.UpdatedUtc = DateTime.UtcNow;
            await connection.UpdateAsync(collection);
        }

        #endregion

        #region Card Methods

        /// <summary>
        /// Inserts a card.
        /// </summary>
        /// <param name="card">The card to insert.</param>
        /// <returns>The SQLite insert result.</returns>
        public async Task<int> AddCardAsync(Card card)
        {
            if (card == null)
            {
                throw new ArgumentNullException(nameof(card));
            }

            await InitializeAsync();

            if (string.IsNullOrWhiteSpace(card.CollectionId) ||
                string.Equals(card.CollectionId, "Default", StringComparison.OrdinalIgnoreCase))
            {
                CardCollection collection = await GetOrCreateDefaultCollectionAsync("local");
                card.CollectionId = collection.Id;
            }

            card.CreatedUtc = card.CreatedUtc == default ? DateTime.UtcNow : card.CreatedUtc;
            card.UpdatedUtc = DateTime.UtcNow;

            return await connection!.InsertAsync(card);
        }

        /// <summary>
        /// Gets all non-deleted cards.
        /// </summary>
        /// <returns>A list of cards.</returns>
        public async Task<List<Card>> GetAllCardsAsync()
        {
            await InitializeAsync();

            try
            {
                return await connection!.Table<Card>()
                    .Where(c => !c.IsDeleted)
                    .OrderByDescending(c => c.CreatedUtc)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CollectIQ DB] GetAllCardsAsync failed: " + ex);
                return new List<Card>();
            }
        }

        /// <summary>
        /// Deletes a card and related local image records.
        /// </summary>
        /// <param name="cardId">The card ID.</param>
        /// <returns>The SQLite delete result.</returns>
        public async Task<int> DeleteCardAsync(string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId))
            {
                return 0;
            }

            await InitializeAsync();

            try
            {
                await connection!.ExecuteAsync("DELETE FROM CardImage WHERE CardId = ?", cardId);
                return await connection.ExecuteAsync("DELETE FROM Card WHERE Id = ?", cardId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CollectIQ DB] DeleteCardAsync failed: " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Updates a card.
        /// </summary>
        /// <param name="card">The card to update.</param>
        /// <returns>The SQLite update result.</returns>
        public async Task<int> UpdateCardAsync(Card card)
        {
            if (card == null)
            {
                throw new ArgumentNullException(nameof(card));
            }

            await InitializeAsync();
            card.UpdatedUtc = DateTime.UtcNow;
            return await connection!.UpdateAsync(card);
        }

        /// <summary>
        /// Assigns existing unassigned cards to the default collection.
        /// </summary>
        /// <param name="collectionId">The collection ID.</param>
        private async Task AssignUnassignedCardsToCollectionAsync(string collectionId)
        {
            try
            {
                List<Card> unassignedCards = await connection!.Table<Card>()
                    .Where(c => c.CollectionId == null || c.CollectionId == string.Empty)
                    .ToListAsync();

                foreach (Card card in unassignedCards)
                {
                    card.CollectionId = collectionId;
                    card.UpdatedUtc = DateTime.UtcNow;
                    await connection.UpdateAsync(card);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CollectIQ DB] AssignUnassignedCardsToCollectionAsync failed: " + ex.Message);
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Normalizes an email address for lookups and unique indexing.
        /// </summary>
        /// <param name="email">The email address.</param>
        /// <returns>The normalized email address.</returns>
        private static string NormalizeEmail(string? email)
        {
            return (email ?? string.Empty).Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Represents the SQLite PRAGMA table_info result.
        /// </summary>
        private sealed class TableInfoRow
        {
            public int cid { get; set; }
            public string name { get; set; } = string.Empty;
            public string type { get; set; } = string.Empty;
            public int notnull { get; set; }
            public string dflt_value { get; set; } = string.Empty;
            public int pk { get; set; }
        }

        #endregion
    }
}
