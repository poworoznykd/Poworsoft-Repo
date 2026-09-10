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
using CollectIQ.Services.Session;
using SQLite;

namespace CollectIQ.Services
{
    /// <summary>
    /// SQLite-backed implementation of IDatabase for CollectIQ.
    /// </summary>
    public sealed class SqliteDatabase : IDatabase
    {
        private const int CurrentDatabaseVersion = 5;
        private const string InitialMigrationName = "20260608_InitialCollectIQFoundation";
        private const string DatabaseSafetyMigrationName = "20260823_NonDestructiveDatabaseUpgradeSafety";
        private const string DatabaseIdentityMigrationName = "20260823_DatabaseIdentityAndDiagnostics";
        private const string AuthenticationIntegrityMigrationName = "20260823_AuthenticationIntegrityAndCredentialDurability";
        private const string OfflineFirstPersistenceMigrationName = "20260910_SQLiteOfflineFirstPersistence";
        private const string SyncStatusPending = "Pending";
        private const string SyncStatusFailed = "Failed";
        private const string SyncStatusCompleted = "Completed";
        private const string SyncOperationUpsert = "Upsert";
        private const string SyncOperationDelete = "Delete";
        private const string DurablePreviousBackupFileName = "collectiq-previous-good.db3";
        private const int MaximumAutomaticBackups = 12;
        private const string DurableBackupDirectoryName = "DurableData";
        private const string DurableBackupFileName = "collectiq-last-known-good.db3";
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

            string? safetyBackupDirectory = null;
            string dbPath = GetDatabasePath();
            string initializationStage = "starting";

            try
            {
                if (isInitialized)
                {
                    return;
                }

                initializationStage = "checking durable recovery copy";
                Debug.WriteLine($"[CollectIQ DB] Path: {dbPath}");
                TryRestoreDurableBackupIfPrimaryMissingOrTruncated(dbPath);

                // CRITICAL DATA-SAFETY RULE:
                // If a database already exists, take a byte-for-byte safety snapshot
                // BEFORE any schema creation/migration code is allowed to touch it.
                // We also preserve WAL/SHM sidecars when present so recently committed
                // rows cannot disappear simply because SQLite had not checkpointed yet.
                if (File.Exists(dbPath) && new FileInfo(dbPath).Length > 0)
                {
                    initializationStage = "creating pre-update safety backup";
                    safetyBackupDirectory = await CreateSafetyBackupAsync(
                        dbPath,
                        $"before_schema_v{CurrentDatabaseVersion}");
                }

                initializationStage = "opening SQLite database";
                connection = new SQLiteAsyncConnection(dbPath);

                initializationStage = "configuring SQLite durability";
                await ConfigureSqliteDurabilityAsync();

                initializationStage = "checking SQLite integrity before migration";
                await EnsureDatabaseHealthyAsync("before migration");

                // Migration history must exist before we evaluate versions.
                initializationStage = "ensuring migration history table";
                await connection.CreateTableAsync<SchemaMigrationHistory>();

                initializationStage = "reading installed schema version";
                int existingVersion = await GetAppliedDatabaseVersionAsync();
                Debug.WriteLine($"[CollectIQ DB] Existing schema version: {existingVersion}");

                // sqlite-net's CreateTableAsync is non-destructive for existing tables:
                // it creates missing tables/columns rather than dropping user tables.
                // Because a full safety backup now exists first, even a failed library
                // migration cannot cost the user their collection.
                initializationStage = "ensuring database tables";
                await CreateTablesAsync();

                initializationStage = $"applying migrations from version {existingVersion} to {CurrentDatabaseVersion}";
                await ApplyMigrationsAsync(existingVersion);

                initializationStage = "seeding roles and plans";
                await SeedRolesAndPlansAsync();

                initializationStage = "checking SQLite integrity after migration";
                await EnsureDatabaseHealthyAsync("after migration");

                initializationStage = "recording SQLite user_version";
                await connection.ExecuteAsync($"PRAGMA user_version = {CurrentDatabaseVersion};");

                initializationStage = "pruning old safety backups";
                await PruneOldSafetyBackupsAsync();

                initializationStage = "creating durable post-update snapshot";
                await CreateDurableDataSnapshotAsync();

                initializationStage = "complete";
                isInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CollectIQ DB] Initialization/migration FAILED: " + ex);

                // Never continue with a half-migrated database. Close SQLite first,
                // then restore the exact files captured before the update.
                if (connection != null)
                {
                    try
                    {
                        await connection.CloseAsync();
                    }
                    catch (Exception closeEx)
                    {
                        Debug.WriteLine("[CollectIQ DB] Close before restore failed: " + closeEx.Message);
                    }
                }

                connection = null;
                isInitialized = false;

                if (!string.IsNullOrWhiteSpace(safetyBackupDirectory) && Directory.Exists(safetyBackupDirectory))
                {
                    try
                    {
                        RestoreSafetyBackup(dbPath, safetyBackupDirectory);
                        Debug.WriteLine($"[CollectIQ DB] Restored pre-update database from: {safetyBackupDirectory}");
                    }
                    catch (Exception restoreEx)
                    {
                        Debug.WriteLine("[CollectIQ DB] DATABASE RESTORE FAILED: " + restoreEx);
                        throw new InvalidOperationException(
                            "CollectIQ could not upgrade the local database and the automatic rollback also failed. " +
                            $"The untouched safety backup is still stored at '{safetyBackupDirectory}'. " +
                            "Do not uninstall the app or clear its data.",
                            new AggregateException(ex, restoreEx));
                    }
                }
                // No safety snapshot means there was nothing to restore. Preserve
                // the failed database file for diagnosis; never delete it automatically.

                throw new InvalidOperationException(
                    "CollectIQ could not apply the local database update. " +
                    "Your pre-update database was preserved/restored, so the app stopped instead of risking your collection data. " +
                    $"Stage: {initializationStage}. " +
                    $"Error type: {ex.GetType().Name}. " +
                    $"SQLite error: {ex.Message}",
                    ex);
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
        /// Kept as a compatibility hook for backup/export callers.
        /// CollectIQ no longer forces WAL mode at startup, so no WAL checkpoint
        /// is required for the normal rollback-journal database configuration.
        /// </summary>
        private Task RunWalCheckpointAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Applies the single SQLite connection option CollectIQ requires.
        ///
        /// Keep startup configuration deliberately minimal. In particular, do not
        /// execute row-returning PRAGMA statements through ExecuteAsync because
        /// sqlite-net can surface SQLITE_ROW as the misleading SQLiteException
        /// message "not an error".
        /// </summary>
        private async Task ConfigureSqliteDurabilityAsync()
        {
            if (connection == null)
            {
                throw new InvalidOperationException("SQLite connection is not available.");
            }

            // PRAGMA foreign_keys is connection-scoped. Set it once after opening
            // the connection. There is no need to change journal mode, synchronous
            // mode, or busy_timeout for CollectIQ's local-first persistence model.
            await connection.ExecuteAsync("PRAGMA foreign_keys = ON;");
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
        /// Adds the offline-sync fields to an existing SyncQueueItem table without
        /// deleting, copying, or recreating any queued data.
        /// </summary>
        private async Task EnsureSyncQueueSchemaAsync()
        {
            await connection!.CreateTableAsync<SyncQueueItem>();

            List<TableInfoRow> columns = await connection
                .QueryAsync<TableInfoRow>("PRAGMA table_info('SyncQueueItem');");

            await AddTableColumnIfMissingAsync(
                columns,
                "SyncQueueItem",
                "UserAccountId",
                "ALTER TABLE SyncQueueItem ADD COLUMN UserAccountId TEXT NOT NULL DEFAULT '';");

            await AddTableColumnIfMissingAsync(
                columns,
                "SyncQueueItem",
                "NextAttemptUtc",
                "ALTER TABLE SyncQueueItem ADD COLUMN NextAttemptUtc INTEGER;");

            await AddTableColumnIfMissingAsync(
                columns,
                "SyncQueueItem",
                "LastError",
                "ALTER TABLE SyncQueueItem ADD COLUMN LastError TEXT NOT NULL DEFAULT '';");

            await AddTableColumnIfMissingAsync(
                columns,
                "SyncQueueItem",
                "CompletedUtc",
                "ALTER TABLE SyncQueueItem ADD COLUMN CompletedUtc INTEGER;");
        }

        /// <summary>
        /// Adds one missing column to an existing table. This is intentionally
        /// additive: existing rows remain in place and the table is never rebuilt.
        /// </summary>
        private async Task AddTableColumnIfMissingAsync(
            List<TableInfoRow> columns,
            string tableName,
            string columnName,
            string sql)
        {
            bool exists = columns.Any(column =>
                string.Equals(
                    column.name,
                    columnName,
                    StringComparison.OrdinalIgnoreCase));

            if (exists)
            {
                return;
            }

            Debug.WriteLine(
                $"[CollectIQ DB] Adding missing {tableName} column: {columnName}");

            await connection!.ExecuteAsync(sql);

            // Keep this in-memory schema list current so subsequent checks in the
            // same migration cannot accidentally try to add the same column twice.
            columns.Add(new TableInfoRow { name = columnName });
        }

        /// <summary>
        /// Ensures the legacy Card table contains newer columns required by existing UI.
        /// </summary>
        private async Task EnsureCardSchemaAsync()
        {
            List<TableInfoRow> columns = await connection!
                .QueryAsync<TableInfoRow>("PRAGMA table_info('Card');");

            await AddColumnIfMissingAsync(columns, "SportValue", "ALTER TABLE Card ADD COLUMN SportValue INTEGER NOT NULL DEFAULT 0;");
            await AddColumnIfMissingAsync(columns, "CollectionId", "ALTER TABLE Card ADD COLUMN CollectionId TEXT NOT NULL DEFAULT ''; ");
            await AddColumnIfMissingAsync(columns, "FrontThumbnailPath", "ALTER TABLE Card ADD COLUMN FrontThumbnailPath TEXT;");
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
        /// Applies every schema migration newer than the installed database.
        /// Future schema changes belong here. Never drop/recreate a user table
        /// to make a model change "work". Add a numbered migration instead.
        /// </summary>
        private async Task ApplyMigrationsAsync(int existingVersion)
        {
            if (existingVersion < 1)
            {
                await RecordMigrationAsync(
                    1,
                    InitialMigrationName,
                    "Existing CollectIQ tables adopted as migration baseline.");
                existingVersion = 1;
            }

            if (existingVersion < 2)
            {
                await RunMigrationTransactionAsync(
                    2,
                    DatabaseSafetyMigrationName,
                    async () =>
                    {
                        await EnsureCardSchemaAsync();
                    });
                existingVersion = 2;
            }

            if (existingVersion < 3)
            {
                await RunMigrationTransactionAsync(
                    3,
                    DatabaseIdentityMigrationName,
                    async () =>
                    {
                        await EnsureDatabaseIdentityAsync();
                    });
                existingVersion = 3;
            }

            if (existingVersion < 4)
            {
                await RunMigrationTransactionAsync(
                    4,
                    AuthenticationIntegrityMigrationName,
                    async () =>
                    {
                        await EnsureAuthenticationIntegrityAsync();
                    });
                existingVersion = 4;
            }

            if (existingVersion < 5)
            {
                await RunMigrationTransactionAsync(
                    5,
                    OfflineFirstPersistenceMigrationName,
                    async () =>
                    {
                        // SyncQueueItem already exists in older CollectIQ databases.
                        // sqlite-net CreateTableAsync() creates missing tables but does
                        // NOT add new properties to an existing table. Upgrade it
                        // explicitly and non-destructively before creating indexes.
                        await EnsureSyncQueueSchemaAsync();

                        await connection!.ExecuteAsync(
                            "CREATE INDEX IF NOT EXISTS IX_SyncQueue_Status_NextAttempt " +
                            "ON SyncQueueItem(Status, NextAttemptUtc);");

                        await connection.ExecuteAsync(
                            "CREATE INDEX IF NOT EXISTS IX_SyncQueue_Account_Entity " +
                            "ON SyncQueueItem(UserAccountId, EntityType, EntityId);");
                    });

                existingVersion = 5;
            }

            // Keep these checks idempotent on every launch. They never drop or
            // recreate authentication tables. If an older build left a profile
            // and credential out of sync, CollectIQ repairs the linkage in place.
            await EnsureDatabaseIdentityAsync();
            await EnsureAuthenticationIntegrityAsync();
        }

        /// <summary>
        /// Creates a permanent identity inside this exact SQLite database file.
        /// The value is written once and never intentionally changed. Developer
        /// diagnostics can therefore prove whether the app is still using the
        /// same database after an update.
        /// </summary>
        private async Task EnsureDatabaseIdentityAsync()
        {
            await connection!.ExecuteAsync(
                "CREATE TABLE IF NOT EXISTS CollectIQDatabaseMetadata (" +
                "MetadataKey TEXT PRIMARY KEY NOT NULL, " +
                "MetadataValue TEXT NOT NULL);");

            string? databaseId = await connection.ExecuteScalarAsync<string?>(
                "SELECT MetadataValue FROM CollectIQDatabaseMetadata WHERE MetadataKey = 'DatabaseInstanceId' LIMIT 1;");

            if (string.IsNullOrWhiteSpace(databaseId))
            {
                databaseId = Guid.NewGuid().ToString("D");
                await connection.ExecuteAsync(
                    "INSERT OR REPLACE INTO CollectIQDatabaseMetadata (MetadataKey, MetadataValue) VALUES (?, ?);",
                    "DatabaseInstanceId",
                    databaseId);
                await connection.ExecuteAsync(
                    "INSERT OR REPLACE INTO CollectIQDatabaseMetadata (MetadataKey, MetadataValue) VALUES (?, ?);",
                    "CreatedUtc",
                    DateTime.UtcNow.ToString("O"));
            }

            await connection.ExecuteAsync(
                "INSERT OR REPLACE INTO CollectIQDatabaseMetadata (MetadataKey, MetadataValue) VALUES (?, ?);",
                "LastOpenedUtc",
                DateTime.UtcNow.ToString("O"));
            await connection.ExecuteAsync(
                "INSERT OR REPLACE INTO CollectIQDatabaseMetadata (MetadataKey, MetadataValue) VALUES (?, ?);",
                "SchemaVersion",
                CurrentDatabaseVersion.ToString());
        }

        /// <summary>
        /// Repairs account/profile/credential linkage without deleting, replacing,
        /// or recreating existing authentication rows. This is intentionally
        /// idempotent and also runs on normal startup so old local accounts remain
        /// usable after application updates.
        /// </summary>
        private async Task EnsureAuthenticationIntegrityAsync()
        {
            List<UserAccount> accounts = await connection!.Table<UserAccount>().ToListAsync();
            List<UserProfile> profiles = await connection.Table<UserProfile>().ToListAsync();
            List<UserCredential> credentials = await connection.Table<UserCredential>().ToListAsync();

            foreach (UserAccount account in accounts)
            {
                string canonicalEmail = NormalizeEmail(
                    !string.IsNullOrWhiteSpace(account.EmailNormalized)
                        ? account.EmailNormalized
                        : account.Email);

                bool accountChanged = false;
                if (!string.IsNullOrWhiteSpace(canonicalEmail) && account.EmailNormalized != canonicalEmail)
                {
                    account.EmailNormalized = canonicalEmail;
                    accountChanged = true;
                }

                if (string.IsNullOrWhiteSpace(account.Email) && !string.IsNullOrWhiteSpace(canonicalEmail))
                {
                    account.Email = canonicalEmail;
                    accountChanged = true;
                }

                if (accountChanged)
                {
                    account.UpdatedUtc = DateTime.UtcNow;
                    await connection.UpdateAsync(account);
                }

                UserProfile? profile = profiles.FirstOrDefault(item => item.UserAccountId == account.Id);
                if (profile == null && !string.IsNullOrWhiteSpace(canonicalEmail))
                {
                    profile = profiles.FirstOrDefault(item =>
                        NormalizeEmail(item.Email) == canonicalEmail);

                    if (profile != null)
                    {
                        profile.UserAccountId = account.Id;
                        profile.UpdatedUtc = DateTime.UtcNow;
                        await connection.UpdateAsync(profile);
                    }
                }

                if (profile == null)
                {
                    profile = new UserProfile
                    {
                        UserAccountId = account.Id,
                        Email = canonicalEmail,
                        DisplayName = account.IsGuest ? "Guest" : (!string.IsNullOrWhiteSpace(account.Email) ? account.Email : canonicalEmail),
                        Role = account.IsGuest ? UserRoles.Guest : UserRoles.Regular,
                        CreatedUtc = DateTime.UtcNow,
                        UpdatedUtc = DateTime.UtcNow
                    };

                    await connection.InsertAsync(profile);
                    profiles.Add(profile);
                }

                UserCredential? localCredential = credentials.FirstOrDefault(item =>
                    item.UserAccountId == account.Id &&
                    string.Equals(item.AuthProvider, "Local", StringComparison.OrdinalIgnoreCase));

                // Older CollectIQ versions stored the hash on UserProfile. If the
                // credential row is absent, restore it from that surviving hash.
                if (localCredential == null && !account.IsGuest && !string.IsNullOrWhiteSpace(profile.PasswordHash))
                {
                    localCredential = new UserCredential
                    {
                        UserAccountId = account.Id,
                        AuthProvider = "Local",
                        PasswordHash = profile.PasswordHash,
                        PasswordAlgorithm = profile.PasswordHash.StartsWith("PBKDF2$", StringComparison.OrdinalIgnoreCase)
                            ? "PBKDF2-SHA256-100000"
                            : "Legacy-SHA256",
                        LastChangedUtc = DateTime.UtcNow,
                        CreatedUtc = DateTime.UtcNow,
                        UpdatedUtc = DateTime.UtcNow
                    };

                    await connection.InsertAsync(localCredential);
                    credentials.Add(localCredential);
                }
                else if (localCredential != null && string.IsNullOrWhiteSpace(localCredential.PasswordHash) && !string.IsNullOrWhiteSpace(profile.PasswordHash))
                {
                    localCredential.PasswordHash = profile.PasswordHash;
                    localCredential.PasswordAlgorithm = profile.PasswordHash.StartsWith("PBKDF2$", StringComparison.OrdinalIgnoreCase)
                        ? "PBKDF2-SHA256-100000"
                        : "Legacy-SHA256";
                    localCredential.LastChangedUtc = DateTime.UtcNow;
                    localCredential.UpdatedUtc = DateTime.UtcNow;
                    await connection.UpdateAsync(localCredential);
                }

                // Keep the legacy hash as a temporary local recovery copy while
                // CollectIQ still supports offline/local password authentication.
                // The credential table remains the authoritative source.
                if (localCredential != null &&
                    !string.IsNullOrWhiteSpace(localCredential.PasswordHash) &&
                    profile.PasswordHash != localCredential.PasswordHash)
                {
                    profile.PasswordHash = localCredential.PasswordHash;
                    profile.UserAccountId = account.Id;
                    profile.UpdatedUtc = DateTime.UtcNow;
                    await connection.UpdateAsync(profile);
                }
            }

            // Repair orphaned and legacy profiles by attaching them to an existing
            // account with the same normalized email. If the account row itself is
            // missing but the profile still exists, recreate ONLY the missing account
            // shell and relink it instead of letting the user appear to "disappear".
            foreach (UserProfile profile in profiles.Where(item => !string.IsNullOrWhiteSpace(item.Email)))
            {
                string profileEmail = NormalizeEmail(profile.Email);
                if (string.IsNullOrWhiteSpace(profileEmail))
                {
                    continue;
                }

                UserAccount? matchingAccount = accounts.FirstOrDefault(item =>
                    (!string.IsNullOrWhiteSpace(profile.UserAccountId) && item.Id == profile.UserAccountId) ||
                    NormalizeEmail(item.EmailNormalized) == profileEmail ||
                    NormalizeEmail(item.Email) == profileEmail);

                if (matchingAccount == null)
                {
                    matchingAccount = new UserAccount
                    {
                        Email = profileEmail,
                        EmailNormalized = profileEmail,
                        AccountStatus = AccountStatuses.Active,
                        IsGuest = string.Equals(profile.Role, UserRoles.Guest, StringComparison.OrdinalIgnoreCase),
                        LastLoginUtc = profile.LastLoginUtc,
                        CreatedUtc = profile.CreatedUtc == default ? DateTime.UtcNow : profile.CreatedUtc,
                        UpdatedUtc = DateTime.UtcNow
                    };

                    await connection.InsertAsync(matchingAccount);
                    accounts.Add(matchingAccount);
                }

                if (profile.UserAccountId != matchingAccount.Id)
                {
                    profile.UserAccountId = matchingAccount.Id;
                    profile.UpdatedUtc = DateTime.UtcNow;
                    await connection.UpdateAsync(profile);
                }

                if (!matchingAccount.IsGuest && !string.IsNullOrWhiteSpace(profile.PasswordHash))
                {
                    UserCredential? localCredential = credentials.FirstOrDefault(item =>
                        item.UserAccountId == matchingAccount.Id &&
                        string.Equals(item.AuthProvider, "Local", StringComparison.OrdinalIgnoreCase));

                    if (localCredential == null)
                    {
                        localCredential = new UserCredential
                        {
                            UserAccountId = matchingAccount.Id,
                            AuthProvider = "Local",
                            PasswordHash = profile.PasswordHash,
                            PasswordAlgorithm = profile.PasswordHash.StartsWith("PBKDF2$", StringComparison.OrdinalIgnoreCase)
                                ? "PBKDF2-SHA256-100000"
                                : "Legacy-SHA256",
                            LastChangedUtc = DateTime.UtcNow,
                            CreatedUtc = DateTime.UtcNow,
                            UpdatedUtc = DateTime.UtcNow
                        };

                        await connection.InsertAsync(localCredential);
                        credentials.Add(localCredential);
                    }
                }
            }

            await connection.ExecuteAsync(
                "CREATE INDEX IF NOT EXISTS IX_UserProfile_UserAccountId ON UserProfile(UserAccountId);");
            await connection.ExecuteAsync(
                "CREATE INDEX IF NOT EXISTS IX_UserCredential_UserAccount_Provider ON UserCredential(UserAccountId, AuthProvider);");

            await connection.ExecuteAsync(
                "INSERT OR REPLACE INTO CollectIQDatabaseMetadata (MetadataKey, MetadataValue) VALUES (?, ?);",
                "AuthenticationIntegrityLastCheckedUtc",
                DateTime.UtcNow.ToString("O"));
        }

        /// <summary>
        /// Runs one migration atomically and records it only after it succeeds.
        /// </summary>
        private async Task RunMigrationTransactionAsync(
            int version,
            string migrationName,
            Func<Task> migrationAction)
        {
            SchemaMigrationHistory? alreadyApplied = await connection!.Table<SchemaMigrationHistory>()
                .Where(m => m.MigrationName == migrationName)
                .FirstOrDefaultAsync();

            if (alreadyApplied != null)
            {
                return;
            }

            Debug.WriteLine($"[CollectIQ DB] Applying migration {version}: {migrationName}");
            await connection.ExecuteAsync("BEGIN IMMEDIATE;");

            try
            {
                await migrationAction();
                await RecordMigrationAsync(version, migrationName, "Applied successfully.");
                await connection.ExecuteAsync("COMMIT;");
            }
            catch
            {
                try
                {
                    await connection.ExecuteAsync("ROLLBACK;");
                }
                catch
                {
                    // Outer InitializeAsync rollback restores the safety snapshot.
                }

                throw;
            }
        }

        /// <summary>
        /// Records a successfully applied schema version.
        /// </summary>
        private async Task RecordMigrationAsync(int version, string migrationName, string appVersion)
        {
            SchemaMigrationHistory? existing = await connection!.Table<SchemaMigrationHistory>()
                .Where(m => m.MigrationName == migrationName)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                return;
            }

            await connection.InsertAsync(new SchemaMigrationHistory
            {
                MigrationName = migrationName,
                DatabaseVersion = version,
                AppVersion = appVersion,
                AppliedUtc = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Gets the highest successfully recorded local database version.
        /// </summary>
        private async Task<int> GetAppliedDatabaseVersionAsync()
        {
            List<SchemaMigrationHistory> migrations = await connection!.Table<SchemaMigrationHistory>().ToListAsync();
            if (migrations.Count == 0)
            {
                return 0;
            }

            return migrations.Max(m => m.DatabaseVersion);
        }

        /// <summary>
        /// Fails closed when SQLite reports corruption. The caller will restore
        /// the pre-migration safety copy instead of attempting destructive repair.
        /// </summary>
        private async Task EnsureDatabaseHealthyAsync(string stage)
        {
            string result = await connection!.ExecuteScalarAsync<string>("PRAGMA quick_check;");
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"SQLite integrity check failed {stage}. Result: {result}");
            }
        }

        /// <summary>
        /// Creates a versioned safety snapshot before schema work. The main DB,
        /// WAL and SHM files are copied together when present.
        /// </summary>
        private static Task<string> CreateSafetyBackupAsync(string databasePath, string reason)
        {
            string backupRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DatabaseBackups");
            Directory.CreateDirectory(backupRoot);

            string safeReason = new string(reason
                .Where(character => char.IsLetterOrDigit(character) || character == '_' || character == '-')
                .ToArray());
            string backupDirectory = Path.Combine(
                backupRoot,
                $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{safeReason}");
            Directory.CreateDirectory(backupDirectory);

            CopyIfExists(databasePath, Path.Combine(backupDirectory, Path.GetFileName(databasePath)));
            CopyIfExists(databasePath + "-wal", Path.Combine(backupDirectory, Path.GetFileName(databasePath) + "-wal"));
            CopyIfExists(databasePath + "-shm", Path.Combine(backupDirectory, Path.GetFileName(databasePath) + "-shm"));

            Debug.WriteLine($"[CollectIQ DB] Safety backup created: {backupDirectory}");
            return Task.FromResult(backupDirectory);
        }

        /// <summary>
        /// Restores all SQLite files captured in a safety snapshot.
        /// </summary>
        private static void RestoreSafetyBackup(string databasePath, string backupDirectory)
        {
            string dbFileName = Path.GetFileName(databasePath);
            string backupDatabase = Path.Combine(backupDirectory, dbFileName);
            if (!File.Exists(backupDatabase))
            {
                throw new FileNotFoundException("The database safety snapshot is missing its main database file.", backupDatabase);
            }

            TryDeleteFile(databasePath);
            TryDeleteFile(databasePath + "-wal");
            TryDeleteFile(databasePath + "-shm");

            File.Copy(backupDatabase, databasePath, overwrite: true);
            CopyIfExists(Path.Combine(backupDirectory, dbFileName + "-wal"), databasePath + "-wal");
            CopyIfExists(Path.Combine(backupDirectory, dbFileName + "-shm"), databasePath + "-shm");
        }

        private static void CopyIfExists(string source, string destination)
        {
            if (File.Exists(source))
            {
                File.Copy(source, destination, overwrite: true);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CollectIQ DB] Could not delete '{path}': {ex.Message}");
            }
        }

        /// <summary>
        /// Keeps several historical schema-upgrade snapshots without allowing
        /// the backup folder to grow forever.
        /// </summary>
        private static Task PruneOldSafetyBackupsAsync()
        {
            string backupRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DatabaseBackups");

            if (!Directory.Exists(backupRoot))
            {
                return Task.CompletedTask;
            }

            DirectoryInfo[] backups = new DirectoryInfo(backupRoot)
                .GetDirectories()
                .OrderByDescending(directory => directory.CreationTimeUtc)
                .ToArray();

            foreach (DirectoryInfo oldBackup in backups.Skip(MaximumAutomaticBackups))
            {
                try
                {
                    oldBackup.Delete(recursive: true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CollectIQ DB] Could not prune old backup '{oldBackup.FullName}': {ex.Message}");
                }
            }

            return Task.CompletedTask;
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

            string? activeUserAccountId = UserSession.CurrentUser?.UserAccountId;
            if (!string.IsNullOrWhiteSpace(activeUserAccountId))
            {
                UserProfile? current = await connection!.Table<UserProfile>()
                    .Where(item => item.UserAccountId == activeUserAccountId)
                    .FirstOrDefaultAsync();

                if (current != null)
                {
                    return current;
                }
            }

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

            UserProfile? direct = await connection!.Table<UserProfile>()
                .Where(u => u.Email == normalizedEmail)
                .FirstOrDefaultAsync();

            if (direct != null)
            {
                return direct;
            }

            List<UserProfile> profiles = await connection.Table<UserProfile>().ToListAsync();
            return profiles.FirstOrDefault(item =>
                string.Equals(NormalizeEmail(item.Email), normalizedEmail, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets the most recently updated profile linked to one permanent account ID.
        /// </summary>
        public async Task<UserProfile?> GetUserProfileByAccountIdAsync(string userAccountId)
        {
            await InitializeAsync();

            if (string.IsNullOrWhiteSpace(userAccountId))
            {
                return null;
            }

            List<UserProfile> matches = await connection!.Table<UserProfile>()
                .Where(item => item.UserAccountId == userAccountId && !item.IsDeleted)
                .ToListAsync();

            return matches
                .OrderByDescending(item => item.UpdatedUtc)
                .ThenByDescending(item => item.CreatedUtc)
                .FirstOrDefault();
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

            UserProfile? existing = await connection!.Table<UserProfile>()
                .Where(item => item.Id == profile.Id)
                .FirstOrDefaultAsync();

            if (existing == null && !string.IsNullOrWhiteSpace(profile.UserAccountId))
            {
                existing = await connection.Table<UserProfile>()
                    .Where(item => item.UserAccountId == profile.UserAccountId)
                    .FirstOrDefaultAsync();
            }

            if (existing != null)
            {
                profile.Id = existing.Id;
                profile.CreatedUtc = existing.CreatedUtc;

                // Never accidentally erase the local recovery hash because a UI
                // profile save did not populate the legacy PasswordHash property.
                if (string.IsNullOrWhiteSpace(profile.PasswordHash))
                {
                    profile.PasswordHash = existing.PasswordHash;
                }

                await connection.UpdateAsync(profile);
            }
            else
            {
                await connection.InsertAsync(profile);
            }
        
            UserProfile? verifiedProfile = await connection!.Table<UserProfile>()
                .Where(item => item.Id == profile.Id)
                .FirstOrDefaultAsync();

            if (verifiedProfile == null)
            {
                throw new InvalidOperationException(
                    $"User profile {profile.Id} was not persisted to SQLite.");
            }

            await QueueSyncChangeAsync(
                nameof(UserProfile),
                profile.Id,
                SyncOperationUpsert,
                profile.UserAccountId);

            await CreateDurableDataSnapshotAsync();
        }

        /// <summary>
        /// Gets a user account by email address.
        /// </summary>
        /// <param name="email">The email address.</param>
        /// <returns>The matching account, or null.</returns>
        /// <summary>
        /// Gets a user account by its permanent CollectIQ account ID.
        /// </summary>
        public async Task<UserAccount?> GetUserAccountByIdAsync(string userAccountId)
        {
            await InitializeAsync();

            if (string.IsNullOrWhiteSpace(userAccountId))
            {
                return null;
            }

            return await connection!.Table<UserAccount>()
                .Where(item => item.Id == userAccountId && !item.IsDeleted)
                .FirstOrDefaultAsync();
        }

        public async Task<UserAccount?> GetUserAccountByEmailAsync(string email)
        {
            await InitializeAsync();

            string normalizedEmail = NormalizeEmail(email);

            UserAccount? direct = await connection!.Table<UserAccount>()
                .Where(u => u.EmailNormalized == normalizedEmail)
                .FirstOrDefaultAsync();

            if (direct != null)
            {
                return direct;
            }

            List<UserAccount> accounts = await connection.Table<UserAccount>().ToListAsync();
            return accounts.FirstOrDefault(item =>
                string.Equals(NormalizeEmail(item.EmailNormalized), normalizedEmail, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizeEmail(item.Email), normalizedEmail, StringComparison.OrdinalIgnoreCase));
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

            string canonicalEmail = NormalizeEmail(
                !string.IsNullOrWhiteSpace(account.EmailNormalized)
                    ? account.EmailNormalized
                    : account.Email);

            account.EmailNormalized = canonicalEmail;

            if (string.IsNullOrWhiteSpace(account.Email) && !string.IsNullOrWhiteSpace(canonicalEmail))
            {
                account.Email = canonicalEmail;
            }

            account.UpdatedUtc = DateTime.UtcNow;

            UserAccount? existing = await connection!.Table<UserAccount>()
                .Where(item => item.Id == account.Id)
                .FirstOrDefaultAsync();

            if (existing == null && !string.IsNullOrWhiteSpace(account.EmailNormalized))
            {
                existing = await connection.Table<UserAccount>()
                    .Where(item => item.EmailNormalized == account.EmailNormalized)
                    .FirstOrDefaultAsync();
            }

            if (existing != null)
            {
                account.Id = existing.Id;
                account.CreatedUtc = existing.CreatedUtc;
                await connection.UpdateAsync(account);
            }
            else
            {
                await connection.InsertAsync(account);
            }

            UserAccount? verifiedAccount = await connection!.Table<UserAccount>()
                .Where(item => item.Id == account.Id)
                .FirstOrDefaultAsync();

            if (verifiedAccount == null)
            {
                throw new InvalidOperationException(
                    $"User account {account.Id} was not persisted to SQLite.");
            }

            await CreateDurableDataSnapshotAsync();
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

            UserCredential? existing = await connection!.Table<UserCredential>()
                .Where(item => item.Id == credential.Id)
                .FirstOrDefaultAsync();

            if (existing == null && !string.IsNullOrWhiteSpace(credential.UserAccountId))
            {
                existing = await connection.Table<UserCredential>()
                    .Where(item => item.UserAccountId == credential.UserAccountId && item.AuthProvider == credential.AuthProvider)
                    .FirstOrDefaultAsync();
            }

            if (existing != null)
            {
                credential.Id = existing.Id;
                credential.CreatedUtc = existing.CreatedUtc;

                // A partial update must never blank an already stored password.
                if (string.IsNullOrWhiteSpace(credential.PasswordHash) &&
                    string.Equals(credential.AuthProvider, "Local", StringComparison.OrdinalIgnoreCase))
                {
                    credential.PasswordHash = existing.PasswordHash;
                    credential.PasswordSalt = existing.PasswordSalt;
                    credential.PasswordAlgorithm = existing.PasswordAlgorithm;
                    credential.LastChangedUtc = existing.LastChangedUtc;
                }

                await connection.UpdateAsync(credential);
            }
            else
            {
                await connection.InsertAsync(credential);
            }
        
            UserCredential? verifiedCredential = await connection!.Table<UserCredential>()
                .Where(item => item.Id == credential.Id)
                .FirstOrDefaultAsync();

            if (verifiedCredential == null)
            {
                throw new InvalidOperationException(
                    $"Credential {credential.Id} was not persisted to SQLite.");
            }

            // Credentials are never added to the generic data-sync outbox.
            await CreateDurableDataSnapshotAsync();
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
            return profile?.PasswordHash;
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
                userAccountId = UserSession.CurrentUser?.UserAccountId ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(userAccountId))
            {
                throw new InvalidOperationException(
                    "A UserAccount.Id is required before CollectIQ can create a collection.");
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

            CardCollection? verifiedCollection = await connection.Table<CardCollection>()
                .Where(item => item.Id == collection.Id && !item.IsDeleted)
                .FirstOrDefaultAsync();

            if (verifiedCollection == null)
            {
                throw new InvalidOperationException(
                    $"Default collection {collection.Id} was not persisted.");
            }

            await QueueSyncChangeAsync(
                nameof(CardCollection),
                collection.Id,
                SyncOperationUpsert,
                userAccountId);

            await CreateDurableDataSnapshotAsync();
            return verifiedCollection;
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
        /// Reconnects collections that still reference an older account ID to the
        /// authenticated canonical account. Cards are not rewritten or deleted because
        /// they already belong to those collections. Call only after authentication has
        /// proven both IDs represent the same identity.
        /// </summary>
        public async Task<int> RecoverOwnedDataForAccountAsync(
            string legacyUserAccountId,
            string canonicalUserAccountId)
        {
            await InitializeAsync();

            if (string.IsNullOrWhiteSpace(legacyUserAccountId) ||
                string.IsNullOrWhiteSpace(canonicalUserAccountId) ||
                string.Equals(legacyUserAccountId, canonicalUserAccountId, StringComparison.Ordinal))
            {
                return 0;
            }

            int changes = 0;
            List<CardCollection> legacyCollections = await connection!.Table<CardCollection>()
                .Where(item => item.OwnerUserAccountId == legacyUserAccountId && !item.IsDeleted)
                .ToListAsync();

            foreach (CardCollection collection in legacyCollections)
            {
                collection.OwnerUserAccountId = canonicalUserAccountId;
                collection.UpdatedUtc = DateTime.UtcNow;
                changes += await connection.UpdateAsync(collection);

                List<CollectionMember> oldMembers = await connection.Table<CollectionMember>()
                    .Where(item => item.CollectionId == collection.Id &&
                                   item.UserAccountId == legacyUserAccountId &&
                                   !item.IsDeleted)
                    .ToListAsync();

                CollectionMember? canonicalMember = await connection.Table<CollectionMember>()
                    .Where(item => item.CollectionId == collection.Id &&
                                   item.UserAccountId == canonicalUserAccountId &&
                                   !item.IsDeleted)
                    .FirstOrDefaultAsync();

                if (canonicalMember == null && oldMembers.Count > 0)
                {
                    CollectionMember member = oldMembers[0];
                    member.UserAccountId = canonicalUserAccountId;
                    member.CollectionRole = "Owner";
                    member.CanView = true;
                    member.CanAddCards = true;
                    member.CanEditCards = true;
                    member.CanDeleteCards = true;
                    member.CanInvite = true;
                    member.UpdatedUtc = DateTime.UtcNow;
                    changes += await connection.UpdateAsync(member);
                }
            }

            return changes;
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

            if (string.IsNullOrWhiteSpace(collection.OwnerUserAccountId))
            {
                collection.OwnerUserAccountId =
                    UserSession.CurrentUser?.UserAccountId ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(collection.OwnerUserAccountId))
            {
                throw new InvalidOperationException(
                    "A collection must have a permanent owner UserAccount.Id.");
            }

            collection.UpdatedUtc = DateTime.UtcNow;
            await connection!.InsertOrReplaceAsync(collection);

            CardCollection? verifiedCollection = await connection.Table<CardCollection>()
                .Where(item => item.Id == collection.Id)
                .FirstOrDefaultAsync();

            if (verifiedCollection == null)
            {
                throw new InvalidOperationException(
                    $"Collection {collection.Id} was not persisted.");
            }

            await QueueSyncChangeAsync(
                nameof(CardCollection),
                collection.Id,
                SyncOperationUpsert,
                collection.OwnerUserAccountId);

            await CreateDurableDataSnapshotAsync();
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

            await QueueSyncChangeAsync(
                nameof(CardCollection),
                collection.Id,
                SyncOperationDelete,
                collection.OwnerUserAccountId);

            await CreateDurableDataSnapshotAsync();
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

            await EnsureValidCardCollectionAsync(card);

            card.CreatedUtc = card.CreatedUtc == default ? DateTime.UtcNow : card.CreatedUtc;
            card.UpdatedUtc = DateTime.UtcNow;

            int result = await connection!.InsertAsync(card);

            if (result <= 0)
            {
                throw new InvalidOperationException(
                    $"SQLite did not insert card {card.Id}.");
            }

            await QueueSyncChangeAsync(
                nameof(Card),
                card.Id,
                SyncOperationUpsert,
                UserSession.CurrentUser?.UserAccountId);

            await VerifyCardPersistedAndSnapshotAsync(card.Id);
            return result;
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
                List<Card> cards = await connection!.Table<Card>()
                    .Where(c => !c.IsDeleted)
                    .OrderByDescending(c => c.CreatedUtc)
                    .ToListAsync();

                HashSet<string>? visibleCollectionIds = await GetVisibleCollectionIdsForCurrentUserAsync();
                if (visibleCollectionIds == null || visibleCollectionIds.Count == 0)
                {
                    return cards;
                }

                return cards
                    .Where(card => !string.IsNullOrWhiteSpace(card.CollectionId) && visibleCollectionIds.Contains(card.CollectionId))
                    .ToList();
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
                Card? card = await connection!.Table<Card>()
                    .Where(item => item.Id == cardId)
                    .FirstOrDefaultAsync();

                if (card == null)
                {
                    return 0;
                }

                // Offline-first delete. Keep a tombstone locally so the deletion
                // can synchronize later and can still be diagnosed/recovered.
                card.IsDeleted = true;
                card.UpdatedUtc = DateTime.UtcNow;

                int result = await connection.UpdateAsync(card);

                await QueueSyncChangeAsync(
                    nameof(Card),
                    card.Id,
                    SyncOperationDelete,
                    UserSession.CurrentUser?.UserAccountId);

                await CreateDurableDataSnapshotAsync();
                return result;
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

            await EnsureValidCardCollectionAsync(card);

            card.UpdatedUtc = DateTime.UtcNow;
            int result = await connection!.UpdateAsync(card);

            if (result <= 0)
            {
                throw new InvalidOperationException(
                    $"SQLite did not update card {card.Id}.");
            }

            await QueueSyncChangeAsync(
                nameof(Card),
                card.Id,
                SyncOperationUpsert,
                UserSession.CurrentUser?.UserAccountId);

            await VerifyCardPersistedAndSnapshotAsync(card.Id);
            return result;
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

        /// <summary>
        /// Ensures a card points to a real, non-deleted collection before SQLite
        /// INSERT/UPDATE. Search results and older cards can carry stale collection
        /// IDs after account/database repairs. A stale ID must never be trusted just
        /// because it is non-empty.
        /// </summary>
        private async Task EnsureValidCardCollectionAsync(Card card)
        {
            if (card == null)
            {
                throw new ArgumentNullException(nameof(card));
            }

            await InitializeAsync();

            CardCollection? assignedCollection = null;

            if (!string.IsNullOrWhiteSpace(card.CollectionId))
            {
                assignedCollection = await connection!.Table<CardCollection>()
                    .Where(c => c.Id == card.CollectionId && !c.IsDeleted)
                    .FirstOrDefaultAsync();
            }

            if (assignedCollection != null)
            {
                string? activeUserAccountId = UserSession.CurrentUser?.UserAccountId;

                if (string.IsNullOrWhiteSpace(activeUserAccountId))
                {
                    return;
                }

                bool ownsCollection =
                    string.Equals(
                        assignedCollection.OwnerUserAccountId,
                        activeUserAccountId,
                        StringComparison.Ordinal);

                CollectionMember? member = ownsCollection
                    ? null
                    : await connection!.Table<CollectionMember>()
                        .Where(item =>
                            item.CollectionId == assignedCollection.Id &&
                            item.UserAccountId == activeUserAccountId &&
                            item.CanAddCards &&
                            !item.IsDeleted)
                        .FirstOrDefaultAsync();

                if (ownsCollection || member != null)
                {
                    return;
                }

                // Existing ID belongs to a collection this user cannot write to.
                // Treat it as stale for this save and repair below.
                assignedCollection = null;
            }

            string ownerUserAccountId = await GetPreferredOwnerUserAccountIdAsync();

            // A signed-in card should be repaired onto that account's real default
            // collection. If no session exists, the existing legacy "local" behavior
            // remains available for developer/offline tooling.
            CardCollection defaultCollection =
                await GetOrCreateDefaultCollectionAsync(ownerUserAccountId);

            // Verify the collection row exists before assigning it to the card.
            CardCollection? verifiedDefault = await connection!.Table<CardCollection>()
                .Where(c => c.Id == defaultCollection.Id && !c.IsDeleted)
                .FirstOrDefaultAsync();

            if (verifiedDefault == null)
            {
                throw new InvalidOperationException(
                    $"CollectIQ could not create or locate a valid default collection for account '{ownerUserAccountId}'.");
            }

            card.CollectionId = verifiedDefault.Id;

            Debug.WriteLine(
                $"[CollectIQ DB] Repaired stale/missing card collection. " +
                $"Card={card.Id}, Collection={verifiedDefault.Id}, Owner={ownerUserAccountId}");
        }

        /// <summary>
        /// Resolves the preferred owner account for newly created local cards.
        /// When a signed-in user exists, new cards must go into that account's
        /// default collection instead of the shared legacy "local" bucket.
        /// </summary>
        private static Task<string> GetPreferredOwnerUserAccountIdAsync()
        {
            string? activeUserAccountId = UserSession.CurrentUser?.UserAccountId;

            if (string.IsNullOrWhiteSpace(activeUserAccountId))
            {
                throw new InvalidOperationException(
                    "CollectIQ cannot save collection data without an active UserAccount.Id.");
            }

            return Task.FromResult(activeUserAccountId);
        }

        /// <summary>
        /// Gets the collection IDs visible to the current signed-in profile.
        /// If no session is available, null is returned so existing developer
        /// tooling can still inspect every card in the local database.
        /// </summary>
        private async Task<HashSet<string>?> GetVisibleCollectionIdsForCurrentUserAsync()
        {
            string? activeUserAccountId = UserSession.CurrentUser?.UserAccountId;
            if (string.IsNullOrWhiteSpace(activeUserAccountId))
            {
                return null;
            }

            List<CardCollection> collections = await GetCollectionsForUserAsync(activeUserAccountId);
            if (collections.Count == 0)
            {
                CardCollection defaultCollection = await GetOrCreateDefaultCollectionAsync(activeUserAccountId);
                collections.Add(defaultCollection);
            }

            return new HashSet<string>(
                collections
                    .Where(collection => !string.IsNullOrWhiteSpace(collection.Id))
                    .Select(collection => collection.Id),
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Runs a full SQLite integrity check without changing data.
        /// </summary>
        public async Task<bool> ValidateDatabaseAsync()
        {
            await InitializeAsync();

            try
            {
                string result = await connection!.ExecuteScalarAsync<string>(
                    "PRAGMA integrity_check;");

                return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CollectIQ DB] ValidateDatabaseAsync failed: " + ex);
                return false;
            }
        }

        /// <summary>
        /// Creates a checkpointed portable SQLite file for desktop inspection.
        /// </summary>
        public async Task<string> CreatePortableDatabaseExportAsync()
        {
            await InitializeAsync();
            await RunWalCheckpointAsync();

            string directory = Path.Combine(
                FileSystem.CacheDirectory,
                "DatabaseExports");

            Directory.CreateDirectory(directory);

            string exportPath = Path.Combine(
                directory,
                $"collectiq-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db3");

            File.Copy(GetDatabasePath(), exportPath, overwrite: true);
            return exportPath;
        }

        /// <summary>
        /// Creates an explicit migration-style safety backup on demand.
        /// </summary>
        public async Task<string> CreateManualDatabaseBackupAsync(string reason = "manual")
        {
            await InitializeAsync();

            try
            {
                await RunWalCheckpointAsync();
            }
            catch
            {
                // The backup routine also captures WAL/SHM if checkpointing fails.
            }

            return await CreateSafetyBackupAsync(
                GetDatabasePath(),
                string.IsNullOrWhiteSpace(reason) ? "manual" : reason);
        }

        /// <summary>
        /// Returns local changes that are ready for a future cloud transport.
        /// </summary>
        public async Task<List<SyncQueueItem>> GetPendingSyncItemsAsync(int maximumItems = 100)
        {
            await InitializeAsync();

            maximumItems = Math.Clamp(maximumItems, 1, 500);
            DateTime now = DateTime.UtcNow;

            List<SyncQueueItem> rows = await connection!.Table<SyncQueueItem>()
                .Where(item =>
                    !item.IsDeleted &&
                    (item.Status == SyncStatusPending || item.Status == SyncStatusFailed))
                .OrderBy(item => item.CreatedUtc)
                .ToListAsync();

            return rows
                .Where(item => item.NextAttemptUtc == null || item.NextAttemptUtc <= now)
                .Take(maximumItems)
                .ToList();
        }

        public async Task MarkSyncItemSucceededAsync(string syncQueueItemId)
        {
            await InitializeAsync();

            SyncQueueItem? item = await connection!.Table<SyncQueueItem>()
                .Where(row => row.Id == syncQueueItemId)
                .FirstOrDefaultAsync();

            if (item == null)
            {
                return;
            }

            item.Status = SyncStatusCompleted;
            item.CompletedUtc = DateTime.UtcNow;
            item.LastAttemptUtc = DateTime.UtcNow;
            item.NextAttemptUtc = null;
            item.LastError = string.Empty;
            item.UpdatedUtc = DateTime.UtcNow;

            await connection.UpdateAsync(item);
            await CreateDurableDataSnapshotAsync();
        }

        public async Task MarkSyncItemFailedAsync(
            string syncQueueItemId,
            string errorMessage)
        {
            await InitializeAsync();

            SyncQueueItem? item = await connection!.Table<SyncQueueItem>()
                .Where(row => row.Id == syncQueueItemId)
                .FirstOrDefaultAsync();

            if (item == null)
            {
                return;
            }

            item.RetryCount++;
            item.Status = SyncStatusFailed;
            item.LastAttemptUtc = DateTime.UtcNow;
            item.LastError = (errorMessage ?? string.Empty).Trim();

            double minutes = Math.Min(
                Math.Pow(2, Math.Min(item.RetryCount, 8)),
                240);

            item.NextAttemptUtc = DateTime.UtcNow.AddMinutes(minutes);
            item.UpdatedUtc = DateTime.UtcNow;

            await connection.UpdateAsync(item);
            await CreateDurableDataSnapshotAsync();
        }

        /// <summary>
        /// Coalesces unsynchronized changes for the same entity. Only identity and
        /// operation are queued; credentials/password hashes are never serialized.
        /// </summary>
        private async Task QueueSyncChangeAsync(
            string entityType,
            string entityId,
            string operation,
            string? userAccountId)
        {
            if (connection == null ||
                string.IsNullOrWhiteSpace(entityType) ||
                string.IsNullOrWhiteSpace(entityId) ||
                string.IsNullOrWhiteSpace(userAccountId))
            {
                return;
            }

            try
            {
                SyncQueueItem? pending = await connection.Table<SyncQueueItem>()
                    .Where(item =>
                        item.EntityType == entityType &&
                        item.EntityId == entityId &&
                        item.UserAccountId == userAccountId &&
                        !item.IsDeleted &&
                        item.Status != SyncStatusCompleted)
                    .OrderByDescending(item => item.UpdatedUtc)
                    .FirstOrDefaultAsync();

                if (pending == null)
                {
                    pending = new SyncQueueItem
                    {
                        UserAccountId = userAccountId,
                        EntityType = entityType,
                        EntityId = entityId,
                        Operation = operation,
                        PayloadJson = string.Empty,
                        Status = SyncStatusPending,
                        RetryCount = 0,
                        CreatedUtc = DateTime.UtcNow,
                        UpdatedUtc = DateTime.UtcNow
                    };

                    await connection.InsertAsync(pending);
                    return;
                }

                pending.Operation = operation;
                pending.Status = SyncStatusPending;
                pending.RetryCount = 0;
                pending.LastAttemptUtc = null;
                pending.NextAttemptUtc = null;
                pending.LastError = string.Empty;
                pending.CompletedUtc = null;
                pending.UpdatedUtc = DateTime.UtcNow;

                await connection.UpdateAsync(pending);
            }
            catch (Exception ex)
            {
                // The local write has already succeeded. Never undo a local save
                // because the future-cloud outbox had a temporary problem.
                Debug.WriteLine(
                    $"[CollectIQ DB] Sync queue write failed for {entityType}/{entityId}: {ex}");
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Normalizes an email address for lookups and unique indexing.
        /// </summary>
        /// <param name="email">The email address.</param>
        /// <returns>The normalized email address.</returns>
        private string GetDurableBackupPath()
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DurableBackupDirectoryName);
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, DurableBackupFileName);
        }

        private void TryRestoreDurableBackupIfPrimaryMissingOrTruncated(string dbPath)
        {
            try
            {
                bool badPrimary =
                    !File.Exists(dbPath) ||
                    new FileInfo(dbPath).Length < 4096;

                if (!badPrimary)
                {
                    return;
                }

                string latest = GetDurableBackupPath();
                string previous = Path.Combine(
                    Path.GetDirectoryName(latest)!,
                    DurablePreviousBackupFileName);

                string? candidate =
                    File.Exists(latest) && new FileInfo(latest).Length >= 4096
                        ? latest
                        : File.Exists(previous) && new FileInfo(previous).Length >= 4096
                            ? previous
                            : null;

                if (candidate == null)
                {
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
                File.Copy(candidate, dbPath, overwrite: true);

                Debug.WriteLine(
                    $"[CollectIQ DB] Restored durable database from {candidate}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CollectIQ DB] Durable restore failed: " + ex);
            }
        }

        private async Task VerifyCardPersistedAndSnapshotAsync(string cardId)
        {
            Card? card = await connection!.Table<Card>().Where(x => x.Id == cardId && !x.IsDeleted).FirstOrDefaultAsync();
            if (card == null) throw new InvalidOperationException($"Card {cardId} was not persisted to SQLite.");
            if (string.IsNullOrWhiteSpace(card.CollectionId)) throw new InvalidOperationException($"Card {cardId} has no collection after save.");
            CardCollection? collection = await connection.Table<CardCollection>().Where(x => x.Id == card.CollectionId && !x.IsDeleted).FirstOrDefaultAsync();
            if (collection == null) throw new InvalidOperationException($"Saved card {cardId} points to a missing collection.");
            await CreateDurableDataSnapshotAsync();
        }

        private async Task CreateDurableDataSnapshotAsync()
        {
            try
            {
                if (connection == null)
                {
                    return;
                }

                try
                {
                    await RunWalCheckpointAsync();
                }
                catch
                {
                    // A sidecar-aware migration backup still exists as another layer.
                }

                string source = GetDatabasePath();
                if (!File.Exists(source) || new FileInfo(source).Length < 4096)
                {
                    return;
                }

                string latest = GetDurableBackupPath();
                string previous = Path.Combine(
                    Path.GetDirectoryName(latest)!,
                    DurablePreviousBackupFileName);
                string temp = latest + ".tmp";

                File.Copy(source, temp, overwrite: true);

                SQLiteAsyncConnection verifier = new SQLiteAsyncConnection(temp);
                string result;

                try
                {
                    result = await verifier.ExecuteScalarAsync<string>(
                        "PRAGMA quick_check;");
                }
                finally
                {
                    await verifier.CloseAsync();
                }

                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteFile(temp);
                    throw new InvalidOperationException(
                        "New durable SQLite snapshot failed quick_check.");
                }

                if (File.Exists(latest) && new FileInfo(latest).Length >= 4096)
                {
                    File.Copy(latest, previous, overwrite: true);
                }

                File.Copy(temp, latest, overwrite: true);
                TryDeleteFile(temp);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CollectIQ DB] Durable snapshot failed: " + ex);
            }
        }

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
