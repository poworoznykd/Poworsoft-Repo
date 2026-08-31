using CollectIQ.Interfaces;
using CollectIQ.Models;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using SQLite;
using System.Security.Cryptography;
using System.Text;

namespace CollectIQ.Services
{
    /// <summary>
    /// Writes a persistent, human-readable authentication/database trace.
    /// This logger is deliberately diagnostic-only: failures here must never
    /// prevent login, profile restoration, or normal app startup.
    /// </summary>
    public static class AccountDiagnosticLogger
    {
        private static readonly SemaphoreSlim WriteLock = new(1, 1);

        public static string DiagnosticDirectory =>
            Path.Combine(FileSystem.AppDataDirectory, "Diagnostics");

        public static string LatestSnapshotPath =>
            Path.Combine(DiagnosticDirectory, "collectiq_account_diagnostics_latest.txt");

        public static string HistoryPath =>
            Path.Combine(DiagnosticDirectory, "collectiq_account_diagnostics_history.txt");

        public static async Task WriteSnapshotAsync(
            IDatabase database,
            string stage,
            string? requestedEmail = null,
            string? detail = null)
        {
            try
            {
                await WriteLock.WaitAsync();
                try
                {
                    Directory.CreateDirectory(DiagnosticDirectory);

                    await database.InitializeAsync();
                    SQLiteAsyncConnection connection = await database.GetConnectionAsync();

                    List<UserAccount> accounts = await connection.Table<UserAccount>().ToListAsync();
                    List<UserProfile> profiles = await connection.Table<UserProfile>().ToListAsync();
                    List<UserCredential> credentials = await connection.Table<UserCredential>().ToListAsync();
                    List<CardCollection> collections = await connection.Table<CardCollection>().ToListAsync();
                    List<Card> cards = await connection.Table<Card>().ToListAsync();

                    string databasePath = database.GetDatabasePath();
                    string databaseIdentity = await GetDatabaseIdentityAsync(connection);
                    int schemaVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version;");

                    string? sessionEmail = await SafeSecureGetAsync("current_user_email");
                    string? sessionAccountId = await SafeSecureGetAsync("current_user_account_id");
                    string? sessionProvider = await SafeSecureGetAsync("current_auth_provider");
                    string? sessionLastLogin = await SafeSecureGetAsync("last_login");

                    StringBuilder sb = new();
                    sb.AppendLine("============================================================");
                    sb.AppendLine("COLLECTIQ ACCOUNT / DATABASE DIAGNOSTIC");
                    sb.AppendLine("============================================================");
                    sb.AppendLine($"UTC Time: {DateTime.UtcNow:O}");
                    sb.AppendLine($"Stage: {stage}");
                    if (!string.IsNullOrWhiteSpace(requestedEmail))
                        sb.AppendLine($"Requested email: {NormalizeEmail(requestedEmail)}");
                    if (!string.IsNullOrWhiteSpace(detail))
                        sb.AppendLine($"Detail: {detail}");
                    sb.AppendLine();

                    sb.AppendLine("APP / DEVICE");
                    sb.AppendLine($"App version: {SafeAppVersion()}");
                    sb.AppendLine($"Platform: {DeviceInfo.Platform}");
                    sb.AppendLine($"OS version: {DeviceInfo.VersionString}");
                    sb.AppendLine($"Device: {DeviceInfo.Manufacturer} {DeviceInfo.Model}");
                    sb.AppendLine();

                    sb.AppendLine("DATABASE");
                    sb.AppendLine($"Path: {databasePath}");
                    sb.AppendLine($"Exists: {File.Exists(databasePath)}");
                    sb.AppendLine($"Size bytes: {(File.Exists(databasePath) ? new FileInfo(databasePath).Length : 0)}");
                    sb.AppendLine($"Database identity: {(string.IsNullOrWhiteSpace(databaseIdentity) ? "(missing)" : databaseIdentity)}");
                    sb.AppendLine($"Schema version: {schemaVersion}");
                    sb.AppendLine($"UserAccount rows: {accounts.Count}");
                    sb.AppendLine($"UserProfile rows: {profiles.Count}");
                    sb.AppendLine($"UserCredential rows: {credentials.Count}");
                    sb.AppendLine($"CardCollection rows: {collections.Count}");
                    sb.AppendLine($"Card rows: {cards.Count}");
                    sb.AppendLine();

                    sb.AppendLine("SECURE STORAGE SESSION");
                    sb.AppendLine($"current_user_email: {Display(sessionEmail)}");
                    sb.AppendLine($"current_user_account_id: {Display(sessionAccountId)}");
                    sb.AppendLine($"current_auth_provider: {Display(sessionProvider)}");
                    sb.AppendLine($"last_login: {Display(sessionLastLogin)}");
                    sb.AppendLine();

                    sb.AppendLine("ACCOUNTS / PROFILES / CREDENTIALS / OWNERSHIP");
                    if (accounts.Count == 0)
                    {
                        sb.AppendLine("(no UserAccount rows)");
                    }

                    foreach (UserAccount account in accounts.OrderBy(a => a.IsGuest).ThenBy(a => a.EmailNormalized))
                    {
                        string accountEmail = NormalizeEmail(
                            !string.IsNullOrWhiteSpace(account.EmailNormalized)
                                ? account.EmailNormalized
                                : account.Email);

                        List<UserProfile> linkedProfiles = profiles
                            .Where(p => p.UserAccountId == account.Id)
                            .ToList();

                        List<UserCredential> linkedCredentials = credentials
                            .Where(c => c.UserAccountId == account.Id)
                            .ToList();

                        List<CardCollection> ownedCollections = collections
                            .Where(c => c.OwnerUserAccountId == account.Id && !c.IsDeleted)
                            .ToList();

                        HashSet<string> ownedIds = ownedCollections
                            .Select(c => c.Id)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        int ownedCardCount = cards.Count(c =>
                            !c.IsDeleted &&
                            !string.IsNullOrWhiteSpace(c.CollectionId) &&
                            ownedIds.Contains(c.CollectionId));

                        sb.AppendLine("------------------------------------------------------------");
                        sb.AppendLine($"UserAccount.Id: {account.Id}");
                        sb.AppendLine($"Email: {Display(account.Email)}");
                        sb.AppendLine($"EmailNormalized: {Display(account.EmailNormalized)}");
                        sb.AppendLine($"Canonical email: {Display(accountEmail)}");
                        sb.AppendLine($"Status: {Display(account.AccountStatus)}");
                        sb.AppendLine($"Guest: {account.IsGuest}");
                        sb.AppendLine($"LastLoginUtc: {account.LastLoginUtc:O}");
                        sb.AppendLine($"Linked profile count: {linkedProfiles.Count}");

                        foreach (UserProfile profile in linkedProfiles)
                        {
                            sb.AppendLine($"  Profile.Id: {profile.Id}");
                            sb.AppendLine($"  Profile.Email: {Display(profile.Email)}");
                            sb.AppendLine($"  Profile.DisplayName: {Display(profile.DisplayName)}");
                            sb.AppendLine($"  Profile.Role: {Display(profile.Role)}");
                            sb.AppendLine($"  Profile.PasswordHashPresent: {!string.IsNullOrWhiteSpace(profile.PasswordHash)}");
                            sb.AppendLine($"  Profile.PasswordHashFingerprint: {Fingerprint(profile.PasswordHash)}");
                        }

                        sb.AppendLine($"Credential count: {linkedCredentials.Count}");
                        foreach (UserCredential credential in linkedCredentials)
                        {
                            sb.AppendLine($"  Credential.Id: {credential.Id}");
                            sb.AppendLine($"  Provider: {Display(credential.AuthProvider)}");
                            sb.AppendLine($"  ProviderUserId: {Display(credential.ProviderUserId)}");
                            sb.AppendLine($"  PasswordHashPresent: {!string.IsNullOrWhiteSpace(credential.PasswordHash)}");
                            sb.AppendLine($"  PasswordAlgorithm: {Display(credential.PasswordAlgorithm)}");
                            sb.AppendLine($"  PasswordHashFingerprint: {Fingerprint(credential.PasswordHash)}");
                            sb.AppendLine($"  LastChangedUtc: {credential.LastChangedUtc:O}");
                        }

                        sb.AppendLine($"Owned collections: {ownedCollections.Count}");
                        foreach (CardCollection collection in ownedCollections)
                        {
                            int collectionCards = cards.Count(c =>
                                !c.IsDeleted &&
                                string.Equals(c.CollectionId, collection.Id, StringComparison.OrdinalIgnoreCase));
                            sb.AppendLine($"  Collection: {collection.Name} | Id={collection.Id} | Cards={collectionCards} | Default={collection.IsDefault}");
                        }
                        sb.AppendLine($"Cards in owned collections: {ownedCardCount}");
                    }

                    // Profiles that are currently orphaned are especially important.
                    List<UserProfile> orphanProfiles = profiles
                        .Where(p => string.IsNullOrWhiteSpace(p.UserAccountId) ||
                                    !accounts.Any(a => a.Id == p.UserAccountId))
                        .ToList();

                    sb.AppendLine();
                    sb.AppendLine("ORPHAN / UNLINKED PROFILES");
                    if (orphanProfiles.Count == 0)
                    {
                        sb.AppendLine("(none)");
                    }
                    else
                    {
                        foreach (UserProfile profile in orphanProfiles)
                        {
                            sb.AppendLine(
                                $"Profile.Id={profile.Id} | UserAccountId={Display(profile.UserAccountId)} | " +
                                $"Email={Display(profile.Email)} | DisplayName={Display(profile.DisplayName)} | " +
                                $"PasswordHashPresent={!string.IsNullOrWhiteSpace(profile.PasswordHash)}");
                        }
                    }

                    sb.AppendLine();
                    sb.AppendLine("NOTE");
                    sb.AppendLine("Plaintext passwords are intentionally NOT logged. PasswordHashFingerprint is");
                    sb.AppendLine("a short fingerprint of the already one-way stored hash so we can tell whether");
                    sb.AppendLine("a credential changed or disappeared between runs without exposing the hash.");
                    sb.AppendLine("============================================================");
                    sb.AppendLine();

                    string snapshot = sb.ToString();
                    await File.WriteAllTextAsync(LatestSnapshotPath, snapshot);
                    await File.AppendAllTextAsync(HistoryPath, snapshot);
                }
                finally
                {
                    WriteLock.Release();
                }
            }
            catch
            {
                // Diagnostics must never be capable of breaking authentication.
            }
        }

        private static async Task<string> GetDatabaseIdentityAsync(SQLiteAsyncConnection connection)
        {
            try
            {
                return await connection.ExecuteScalarAsync<string>(
                    "SELECT MetadataValue FROM CollectIQDatabaseMetadata " +
                    "WHERE MetadataKey='DatabaseInstanceId' LIMIT 1;") ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static async Task<string?> SafeSecureGetAsync(string key)
        {
            try { return await SecureStorage.Default.GetAsync(key); }
            catch { return "(SecureStorage read failed)"; }
        }

        private static string Fingerprint(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "(none)";

            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes)[..16];
        }

        private static string SafeAppVersion()
        {
            try { return $"{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})"; }
            catch { return "(unknown)"; }
        }

        private static string NormalizeEmail(string? value) =>
            (value ?? string.Empty).Trim().ToLowerInvariant();

        private static string Display(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "(blank)" : value;
    }
}
