/******************************************************************************
 *
 * FILE          : DatabaseSchema.cs
 * PROJECT       : CollectIQ
 * PROGRAMMER    : Darryl Poworoznyk
 * FIRST VERSION : 2026-06-02
 *
 * DESCRIPTION:
 *
 * This file contains all SQLite schema creation statements for the local
 * CollectIQ database.
 *
 * The schema supports:
 *
 * - Offline user sessions
 * - User roles and privileges
 * - Subscription plans
 * - Rewards
 * - Collections
 * - Collection sharing and invites
 * - Cards
 * - Card images
 * - Custom card fields
 * - Marketplace listings and offers
 * - Synchronization queue
 * - Application settings
 * - Audit logging
 *
 * CHANGE LOG:
 *
 * Date         Programmer              Description
 * ----------   --------------------    ---------------------------------------
 * 2026-06-02   Darryl Poworoznyk       Initial creation.
 *
 *****************************************************************************/

namespace CollectIQ.Data
{
    /// <summary>
    /// Contains all SQLite schema creation statements.
    /// </summary>
    public static class DatabaseSchema
    {
        #region Public Members

        /// <summary>
        /// SQL statements executed during local database initialization.
        /// </summary>
        public static readonly string[] CreateStatements =
        {
            /*
             * Schema version.
             */
            """
            CREATE TABLE IF NOT EXISTS SchemaVersion
            (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                Version INTEGER NOT NULL,
                UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """,

            """
            INSERT OR IGNORE INTO SchemaVersion
            (
                Id,
                Version,
                UpdatedAt
            )
            VALUES
            (
                1,
                1,
                datetime('now')
            );
            """,

            /*
             * User account.
             */
            """
            CREATE TABLE IF NOT EXISTS User
            (
                LocalId INTEGER PRIMARY KEY AUTOINCREMENT,
                ServerId TEXT NULL,

                Email TEXT NOT NULL,
                DisplayName TEXT NULL,

                SubscriptionPlanKey TEXT NULL,

                IsAdmin INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1,

                SyncStatus TEXT NOT NULL DEFAULT 'Pending',
                LastSyncedAt TEXT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0,

                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NULL
            );
            """,

            /*
             * Cached user session for offline access.
             */
            """
            CREATE TABLE IF NOT EXISTS UserSession
            (
                LocalId INTEGER PRIMARY KEY AUTOINCREMENT,

                ServerUserId TEXT NULL,

                Email TEXT NOT NULL,
                DisplayName TEXT NULL,

                AccessToken TEXT NULL,
                RefreshToken TEXT NULL,

                LastSuccessfulLogin TEXT NOT NULL,
                OfflineAccessUntil TEXT NOT NULL,

                IsActive INTEGER NOT NULL DEFAULT 1,

                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NULL
            );
            """,

            /*
             * Roles.
             */
            """
            CREATE TABLE IF NOT EXISTS Role
            (
                LocalId INTEGER PRIMARY KEY AUTOINCREMENT,
                ServerId TEXT NULL,

                Name TEXT NOT NULL UNIQUE,
                Description TEXT NULL,

                IsSystemRole INTEGER NOT NULL DEFAULT 0,

                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NULL
            );
            """,

            /*
             * Privileges.
             */
            """
            CREATE TABLE IF NOT EXISTS Privilege
            (
                LocalId INTEGER PRIMARY KEY AUTOINCREMENT,
                ServerId TEXT NULL,

                Name TEXT NOT NULL UNIQUE,
                Description TEXT NULL,

                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NULL
            );
            """,

            /*
             * User to role bridge.
             */
            """
            CREATE TABLE IF NOT EXISTS UserRole
            (
                LocalId INTEGER PRIMARY KEY AUTOINCREMENT,

                UserLocalId INTEGER NOT NULL,
                RoleLocalId INTEGER NOT NULL,

                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,

                FOREIGN KEY (UserLocalId)
                    REFERENCES User(LocalId)
                    ON DELETE CASCADE,

                FOREIGN KEY (RoleLocalId)
                    REFERENCES Role(LocalId)
                    ON DELETE CASCADE,

                UNIQUE(UserLocalId, RoleLocalId)
            );
            """,

            /*
             * Role to privilege bridge.
             */
            """
            CREATE TABLE IF NOT EXISTS RolePrivilege
            (
                LocalId INTEGER PRIMARY KEY AUTOINCREMENT,

                RoleLocalId INTEGER NOT NULL,
                PrivilegeLocalId INTEGER NOT NULL,

                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,

                FOREIGN KEY (RoleLocalId)
                    REFERENCES Role(LocalId)
                    ON DELETE CASCADE,

                FOREIGN KEY (PrivilegeLocalId)
                    REFERENCES Privilege(LocalId)
                    ON DELETE CASCADE,

                UNIQUE(RoleLocalId, PrivilegeLocalId)
            );
            """,

            /*
             * Subscription plans.
             */
            """
            CREATE TABLE IF NOT EXISTS SubscriptionPlan
            (
                LocalId INTEGER PRIMARY KEY AUTOINCREMENT,
                ServerId TEXT NULL,

                PlanKey TEXT NOT NULL UNIQUE,
                Name TEXT NOT NULL,
                Description TEXT NULL,

                MonthlyPrice REAL NOT NULL DEFAULT 0,
                YearlyPrice REAL NOT NULL DEFAULT 0,

                MaxCollections INTEGER NULL,
                MaxCards INTEGER NULL,

                CanShareCollections INTEGER NOT NULL DEFAULT 0,
                CanSellCards INTEGER NOT NULL DEFAULT 0,
                CanUseAdvancedInsights INTEGER NOT NULL DEFAULT 0,

                IsActive INTEGER NOT NULL DEFAULT 1,

                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NULL
            );
            """,

            /*
             * User subscriptions.
             */
            """
            CREATE TABLE IF NOT EXISTS UserSubscription
            (
                LocalId INTEGER PRIMARY KEY AUTOINCREMENT,
                ServerId TEXT NULL,

                UserLocalId INTEGER NOT NULL,
                SubscriptionPlanLocalId INTEGER NOT NULL,

                Status TEXT NOT NULL,

                StartDate TEXT NOT NULL,
                EndDate TEXT NULL,
                CancelledAt TEXT NULL,

                ExternalSubscriptionId TEXT NULL,
                ExternalProvider TEXT NULL,

                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NULL,

                FOREIGN KEY (UserLocalId)
                    REFERENCES User(LocalId)
                    ON DELETE CASCADE,

                FOREIGN KEY (SubscriptionPlanLocalId)
                    REFERENCES SubscriptionPlan(LocalId)
                    ON DELETE RESTRICT
            );
            """,

            /*
             * Reward account.
             */
            """
            CREATE TABLE IF NOT EXISTS RewardAccount
            (
                LocalId INTEGER PRIMARY KEY AUTOINCREMENT,
                ServerId TEXT NULL,

                UserLocalId INTEGER NOT NULL UNIQUE,

                PointsBalance INTEGER NOT NULL DEFAULT 0,
                LifetimePointsEarned INTEGER NOT NULL DEFAULT 0,
                LifetimePointsSpent INTEGER NOT NULL DEFAULT 0,

                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NULL,

                FOREIGN KEY (UserLocalId)
                    REFERENCES User(LocalId)
                    ON DELETE CASCADE
            );
            """,

            /*
             * Reward transactions.
             */
            """
            CREATE TABLE IF NOT EXISTS RewardTransaction
            (
                LocalId INTEGER PRIMARY KEY AUTOINCREMENT,
                ServerId TEXT NULL,

                RewardAccountLocalId INTEGER NOT NULL,

                TransactionType TEXT NOT NULL,
                Points INTEGER NOT NULL,
                Description TEXT NULL,

                ReferenceType TEXT NULL,
                ReferenceId TEXT NULL,

                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,

                FOREIGN KEY (RewardAccountLocalId)
                    REFERENCES RewardAccount(LocalId)
                    ON DELETE CASCADE
            );
            """,

            /*
             * Collections.
             */
            """
            CREATE TABLE IF NOT EXISTS Collection
            (
                LocalId INTEGER PRIMARY KEY AUTOINCREMENT,
                ServerId TEXT NULL,

                OwnerServerUserId TEXT NULL,

                Name TEXT NOT NULL,
                Description TEXT NULL,

                IsDefault INTEGER NOT NULL DEFAULT 0,
                IsShared INTEGER NOT NULL DEFAULT 0,
                IsForSale INTEGER NOT NULL DEFAULT 0,

                SyncStatus TEXT NOT NULL DEFAULT 'Pending',
                LastSyncedAt TEXT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0,

                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NULL
            );
            """,

            /*
             * Collection members.
             */
            """
            CREATE TABLE IF NOT EXISTS CollectionMember
            (
                LocalId INTEGER PRIMARY KEY AUTOINCREMENT,
                ServerId TEXT NULL,

                CollectionLocalId INTEGER NOT NULL,
                UserLocalId INTEGER NOT NULL,

                PermissionLevel TEXT NOT NULL,

                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NULL,

                FOREIGN KEY (CollectionLocalId)
                    REFERENCES Collection(LocalId)
                    ON DELETE CASCADE,

                FOREIGN KEY (UserLocalId)
                    REFERENCES User(LocalId)
                    ON DELETE CASCADE,

                UNIQUE(CollectionLocalId, UserLocalId)
            );
            """,

            /*
             * Collection invites.
             */
            """
            CREATE TABLE IF NOT EXISTS CollectionInvite
            (
                LocalId INTEGER PRIMARY KEY AUTOINCREMENT,
                ServerId TEXT NULL,

                CollectionLocalId INTEGER NOT NULL,

                Email TEXT NOT NULL,
                InviteToken TEXT NOT NULL,
                Status TEXT NOT NULL,

                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                ExpiresAt TEXT NULL,
                AcceptedAt TEXT NULL,
                DeclinedAt TEXT NULL,

                FOREIGN KEY (CollectionLocalId)
                    REFERENCES Collection(LocalId)
                    ON DELETE CASCADE
            );
            """,

            /*
             * Cards.
             */
            """
            CREATE TABLE IF NOT EXISTS Card
            (
                LocalId INTEGER PRIMARY KEY AUTOINCREMENT,
                ServerId TEXT NULL,

                CollectionLocalId INTEGER NULL,

                Title TEXT NOT NULL,

                PlayerName TEXT NULL,
                Year TEXT NULL,

                Brand TEXT NULL,
                SetName TEXT NULL,
                CardNumber TEXT NULL,

                Sport TEXT NULL,
                Team TEXT NULL,
                League TEXT NULL,

                Grade TEXT NULL,
                GradingCompany TEXT NULL,
                CertificationNumber TEXT NULL,

                FrontImagePath TEXT NULL,
                BackImagePath TEXT NULL,

                EstimatedValue REAL NULL,
                PurchasePrice REAL NULL,
                AskingPrice REAL NULL,
                SoldPrice REAL NULL,

                ConditionNotes TEXT NULL,
                Notes TEXT NULL,

                IsFavorite INTEGER NOT NULL DEFAULT 0,
                IsForSale INTEGER NOT NULL DEFAULT 0,
                IsSold INTEGER NOT NULL DEFAULT 0,
                IsArchived INTEGER NOT NULL DEFAULT 0,

                SyncStatus TEXT NOT NULL DEFAULT 'Pending',
                LastSyncedAt TEXT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0,

                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NULL,

                FOREIGN KEY (CollectionLocalId)
                    REFERENCES Collection(LocalId)
                    ON DELETE SET NULL
            );
            """,

            /*
             * Card images.
             */
            """
            CREATE TABLE IF NOT EXISTS CardImage
            (
                LocalId INTEGER PRIMARY KEY AUTOINCREMENT,
                ServerId TEXT NULL,

                CardLocalId INTEGER NOT NULL,

                ImagePath TEXT NOT NULL,
                ImageType TEXT NOT NULL,

                SyncStatus TEXT NOT NULL DEFAULT 'Pending',
                LastSyncedAt TEXT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0,

                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NULL,

                FOREIGN KEY (CardLocalId)
                    REFERENCES Card(LocalId)
                    ON DELETE CASCADE
            );
            """,

            /*
             * Custom card fields.
             */
            """
            CREATE TABLE IF NOT EXISTS CardCustomField
            (
                LocalId INTEGER PRIMARY KEY AUTOINCREMENT,
                ServerId TEXT NULL,

                CardLocalId INTEGER NOT NULL,

                FieldName TEXT NOT NULL,
                FieldValue TEXT NULL,
                FieldType TEXT NOT NULL DEFAULT 'Text',

                SyncStatus TEXT NOT NULL DEFAULT 'Pending',
                LastSyncedAt TEXT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0,

                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NULL,

                FOREIGN KEY (CardLocalId)
                    REFERENCES Card(LocalId)
                    ON DELETE CASCADE,

                UNIQUE(CardLocalId, FieldName)
            );
            """,

            /*
             * Marketplace listings.
             */
            """
            CREATE TABLE IF NOT EXISTS MarketplaceListing
            (
                LocalId INTEGER PRIMARY KEY AUTOINCREMENT,
                ServerId TEXT NULL,

                CardLocalId INTEGER NOT NULL,

                ListingPrice REAL NOT NULL,
                Currency TEXT NOT NULL DEFAULT 'CAD',
                Status TEXT NOT NULL,

                Description TEXT NULL,

                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NULL,

                FOREIGN KEY (CardLocalId)
                    REFERENCES Card(LocalId)
                    ON DELETE CASCADE
            );
            """,

            /*
             * Marketplace offers.
             */
            """
            CREATE TABLE IF NOT EXISTS MarketplaceOffer
            (
                LocalId INTEGER PRIMARY KEY AUTOINCREMENT,
                ServerId TEXT NULL,

                ListingLocalId INTEGER NOT NULL,
                BuyerUserLocalId INTEGER NOT NULL,

                OfferAmount REAL NOT NULL,
                Currency TEXT NOT NULL DEFAULT 'CAD',
                Status TEXT NOT NULL,

                Message TEXT NULL,

                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NULL,

                FOREIGN KEY (ListingLocalId)
                    REFERENCES MarketplaceListing(LocalId)
                    ON DELETE CASCADE,

                FOREIGN KEY (BuyerUserLocalId)
                    REFERENCES User(LocalId)
                    ON DELETE CASCADE
            );
            """,

            /*
             * Synchronization queue.
             */
            """
            CREATE TABLE IF NOT EXISTS SyncQueue
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,

                EntityType TEXT NOT NULL,
                LocalEntityId INTEGER NOT NULL,

                Operation TEXT NOT NULL,
                PayloadJson TEXT NULL,

                AttemptCount INTEGER NOT NULL DEFAULT 0,
                LastAttemptAt TEXT NULL,
                LastError TEXT NULL,

                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """,

            /*
             * Application settings.
             */
            """
            CREATE TABLE IF NOT EXISTS AppSetting
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,

                SettingKey TEXT NOT NULL UNIQUE,
                SettingValue TEXT NULL,

                UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """,

            /*
             * Audit log.
             */
            """
            CREATE TABLE IF NOT EXISTS AuditLog
            (
                LocalId INTEGER PRIMARY KEY AUTOINCREMENT,

                UserLocalId INTEGER NULL,

                EntityType TEXT NOT NULL,
                EntityId TEXT NOT NULL,

                Action TEXT NOT NULL,
                Description TEXT NULL,

                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,

                FOREIGN KEY (UserLocalId)
                    REFERENCES User(LocalId)
                    ON DELETE SET NULL
            );
            """,

            /*
             * Seed roles.
             */
            """
            INSERT OR IGNORE INTO Role
            (
                Name,
                Description,
                IsSystemRole
            )
            VALUES
            (
                'Admin',
                'System administrator.',
                1
            );
            """,

            """
            INSERT OR IGNORE INTO Role
            (
                Name,
                Description,
                IsSystemRole
            )
            VALUES
            (
                'User',
                'Standard CollectIQ user.',
                1
            );
            """,

            """
            INSERT OR IGNORE INTO Role
            (
                Name,
                Description,
                IsSystemRole
            )
            VALUES
            (
                'Moderator',
                'Can moderate shared or marketplace content.',
                1
            );
            """,

            /*
             * Seed subscription plans.
             */
            """
            INSERT OR IGNORE INTO SubscriptionPlan
            (
                PlanKey,
                Name,
                Description,
                MonthlyPrice,
                YearlyPrice,
                MaxCollections,
                MaxCards,
                CanShareCollections,
                CanSellCards,
                CanUseAdvancedInsights,
                IsActive
            )
            VALUES
            (
                'FREE',
                'Free',
                'Basic CollectIQ access.',
                0,
                0,
                3,
                100,
                0,
                0,
                0,
                1
            );
            """,

            """
            INSERT OR IGNORE INTO SubscriptionPlan
            (
                PlanKey,
                Name,
                Description,
                MonthlyPrice,
                YearlyPrice,
                MaxCollections,
                MaxCards,
                CanShareCollections,
                CanSellCards,
                CanUseAdvancedInsights,
                IsActive
            )
            VALUES
            (
                'PRO',
                'Pro',
                'Paid collector subscription.',
                9.99,
                99.99,
                50,
                10000,
                1,
                1,
                1,
                1
            );
            """,

            """
            INSERT OR IGNORE INTO SubscriptionPlan
            (
                PlanKey,
                Name,
                Description,
                MonthlyPrice,
                YearlyPrice,
                MaxCollections,
                MaxCards,
                CanShareCollections,
                CanSellCards,
                CanUseAdvancedInsights,
                IsActive
            )
            VALUES
            (
                'BUSINESS',
                'Business',
                'Seller and high-volume collector plan.',
                29.99,
                299.99,
                NULL,
                NULL,
                1,
                1,
                1,
                1
            );
            """,

            /*
             * Indexes.
             */
            "CREATE INDEX IF NOT EXISTS IX_User_Email ON User(Email);",
            "CREATE INDEX IF NOT EXISTS IX_User_ServerId ON User(ServerId);",

            "CREATE INDEX IF NOT EXISTS IX_UserSession_Email ON UserSession(Email);",

            "CREATE INDEX IF NOT EXISTS IX_UserRole_UserLocalId ON UserRole(UserLocalId);",
            "CREATE INDEX IF NOT EXISTS IX_RolePrivilege_RoleLocalId ON RolePrivilege(RoleLocalId);",

            "CREATE INDEX IF NOT EXISTS IX_UserSubscription_UserLocalId ON UserSubscription(UserLocalId);",

            "CREATE INDEX IF NOT EXISTS IX_RewardAccount_UserLocalId ON RewardAccount(UserLocalId);",

            "CREATE INDEX IF NOT EXISTS IX_Collection_Name ON Collection(Name);",
            "CREATE INDEX IF NOT EXISTS IX_Collection_ServerId ON Collection(ServerId);",

            "CREATE INDEX IF NOT EXISTS IX_CollectionMember_CollectionLocalId ON CollectionMember(CollectionLocalId);",
            "CREATE INDEX IF NOT EXISTS IX_CollectionInvite_Email ON CollectionInvite(Email);",

            "CREATE INDEX IF NOT EXISTS IX_Card_CollectionLocalId ON Card(CollectionLocalId);",
            "CREATE INDEX IF NOT EXISTS IX_Card_ServerId ON Card(ServerId);",
            "CREATE INDEX IF NOT EXISTS IX_Card_PlayerName ON Card(PlayerName);",
            "CREATE INDEX IF NOT EXISTS IX_Card_Title ON Card(Title);",
            "CREATE INDEX IF NOT EXISTS IX_Card_Brand ON Card(Brand);",
            "CREATE INDEX IF NOT EXISTS IX_Card_Year ON Card(Year);",

            "CREATE INDEX IF NOT EXISTS IX_CardImage_CardLocalId ON CardImage(CardLocalId);",
            "CREATE INDEX IF NOT EXISTS IX_CardCustomField_CardLocalId ON CardCustomField(CardLocalId);",

            "CREATE INDEX IF NOT EXISTS IX_MarketplaceListing_CardLocalId ON MarketplaceListing(CardLocalId);",
            "CREATE INDEX IF NOT EXISTS IX_MarketplaceOffer_ListingLocalId ON MarketplaceOffer(ListingLocalId);",

            "CREATE INDEX IF NOT EXISTS IX_SyncQueue_EntityType ON SyncQueue(EntityType);",
            "CREATE INDEX IF NOT EXISTS IX_AuditLog_EntityType ON AuditLog(EntityType);"
        };

        #endregion
    }
}