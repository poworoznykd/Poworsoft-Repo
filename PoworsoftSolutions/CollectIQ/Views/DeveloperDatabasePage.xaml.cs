using CollectIQ.Helpers;
using CollectIQ.Interfaces;
using CollectIQ.Models;
using CollectIQ.Services;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using SQLite;
using System.Diagnostics;
using System.Security.Cryptography;

namespace CollectIQ.Views
{
    public partial class DeveloperDatabasePage : ContentPage
    {
        private const string LastKnownDatabaseIdentityKey = "CollectIQ.LastKnownDatabaseInstanceId";
        private readonly IDatabase database;
        private string databasePath = string.Empty;
        private List<RecoveryAccountOption> recoveryAccounts = new();

        public DeveloperDatabasePage()
        {
            InitializeComponent();
            database = ServiceHelper.GetService<IDatabase>()
                ?? App.Database
                ?? throw new InvalidOperationException("IDatabase is not available.");
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadDatabaseInfoAsync();
        }

        private async void OnBackClicked(object sender, TappedEventArgs e) => await GoBackAsync();

        private async void OnCopyPathClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                await DisplayAlert("Database", "The database path is not loaded yet.", "OK");
                return;
            }
            await Clipboard.Default.SetTextAsync(databasePath);
            await DisplayAlert("Database", "Database path copied to the clipboard.", "OK");
        }

        private async void OnRefreshClicked(object sender, EventArgs e) => await LoadDatabaseInfoAsync();

        private async void OnRecoveryAccountSelected(object sender, EventArgs e)
        {
            int index = RecoveryAccountPicker.SelectedIndex;
            if (index < 0 || index >= recoveryAccounts.Count)
            {
                SelectedRecoveryAccountLabel.Text = "No account selected.";
                return;
            }

            RecoveryAccountOption option = recoveryAccounts[index];
            SelectedRecoveryAccountLabel.Text = option.Details;
            if (!string.IsNullOrWhiteSpace(option.Email))
                RecoveryEmailEntry.Text = option.Email;
        }

        private async void OnRepairLocalLoginClicked(object sender, EventArgs e)
        {
            try
            {
                int selectedIndex = RecoveryAccountPicker.SelectedIndex;
                if (selectedIndex < 0 || selectedIndex >= recoveryAccounts.Count)
                {
                    await DisplayAlert("Local Login Recovery", "Select the existing account you want to repair first.", "OK");
                    return;
                }

                RecoveryAccountOption option = recoveryAccounts[selectedIndex];
                string email = (RecoveryEmailEntry.Text ?? string.Empty).Trim().ToLowerInvariant();
                string password = RecoveryPasswordEntry.Text ?? string.Empty;

                if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                {
                    await DisplayAlert("Local Login Recovery", "Enter the email and password you want the selected existing account to use.", "OK");
                    return;
                }

                await database.InitializeAsync();
                SQLiteAsyncConnection connection = await database.GetConnectionAsync();

                UserAccount? account = await connection.Table<UserAccount>()
                    .Where(item => item.Id == option.UserAccountId)
                    .FirstOrDefaultAsync();

                if (account == null)
                {
                    await DisplayAlert("Local Login Recovery", "The selected UserAccount no longer exists. Nothing was changed.", "OK");
                    await LoadDatabaseInfoAsync();
                    return;
                }

                UserAccount? conflictingAccount = await database.GetUserAccountByEmailAsync(email);
                if (conflictingAccount != null && conflictingAccount.Id != account.Id)
                {
                    await DisplayAlert(
                        "Email Already Used",
                        "That email is already assigned to a different existing UserAccount. Nothing was changed. Select the correct account or inspect the account list first.",
                        "OK");
                    return;
                }

                bool confirmed = await DisplayAlert(
                    "Repair Existing Account?",
                    $"Repair the selected existing account and assign {email} to it? Its UserAccount ID and collection ownership will stay unchanged.",
                    "Repair",
                    "Cancel");

                if (!confirmed)
                    return;

                account.Email = email;
                account.EmailNormalized = email;
                account.AccountStatus = AccountStatuses.Active;
                await database.UpsertUserAccountAsync(account);

                List<UserProfile> profiles = await connection.Table<UserProfile>().ToListAsync();
                UserProfile? linkedProfile = profiles.FirstOrDefault(profile => profile.UserAccountId == account.Id);
                if (linkedProfile == null)
                {
                    linkedProfile = profiles.FirstOrDefault(profile =>
                        string.Equals(profile.Email, option.Email, StringComparison.OrdinalIgnoreCase));
                }

                if (linkedProfile != null)
                {
                    linkedProfile.UserAccountId = account.Id;
                    linkedProfile.Email = email;
                    await database.UpsertUserProfileAsync(linkedProfile);
                }

                string passwordHash = CreateCompatiblePasswordHash(password);

                // Repair the selected account DIRECTLY. Do not route through an
                // email lookup here because the whole reason this recovery tool
                // exists is that old Email / EmailNormalized / profile links may
                // have drifted apart.
                List<UserCredential> existingCredentials = await connection.Table<UserCredential>()
                    .Where(item => item.UserAccountId == account.Id)
                    .ToListAsync();

                foreach (UserCredential existing in existingCredentials)
                {
                    if (string.Equals(existing.AuthProvider, "Local", StringComparison.OrdinalIgnoreCase))
                        await connection.DeleteAsync(existing);
                }

                UserCredential repairedCredential = new UserCredential
                {
                    UserAccountId = account.Id,
                    AuthProvider = "Local",
                    PasswordHash = passwordHash,
                    PasswordAlgorithm = "PBKDF2-SHA256-100000",
                    LastChangedUtc = DateTime.UtcNow,
                    CreatedUtc = DateTime.UtcNow,
                    UpdatedUtc = DateTime.UtcNow
                };
                await connection.InsertAsync(repairedCredential);

                // Keep the legacy profile hash synchronized because older builds
                // can still fall back to this field during authentication.
                if (linkedProfile == null)
                {
                    linkedProfile = new UserProfile
                    {
                        UserAccountId = account.Id,
                        Email = email,
                        DisplayName = email,
                        Role = UserRoles.Regular,
                        CreatedUtc = DateTime.UtcNow,
                        UpdatedUtc = DateTime.UtcNow
                    };
                }
                linkedProfile.UserAccountId = account.Id;
                linkedProfile.Email = email;
                linkedProfile.PasswordHash = passwordHash;
                await database.UpsertUserProfileAsync(linkedProfile);

                // Read the exact row back and validate it with the same PBKDF2
                // format used by LocalAuthService. If this fails, do not tell the
                // user recovery succeeded.
                UserCredential? verificationCredential = await connection.Table<UserCredential>()
                    .Where(item => item.UserAccountId == account.Id && item.AuthProvider == "Local")
                    .FirstOrDefaultAsync();

                if (verificationCredential == null ||
                    string.IsNullOrWhiteSpace(verificationCredential.PasswordHash) ||
                    !VerifyCompatiblePasswordHash(password, verificationCredential.PasswordHash))
                {
                    throw new InvalidOperationException("The repaired credential could not be read back and verified. Login data was not confirmed.");
                }

                UserAccount? verificationAccount = await database.GetUserAccountByEmailAsync(email);
                if (verificationAccount == null || verificationAccount.Id != account.Id)
                {
                    throw new InvalidOperationException("The email now has a credential, but the login email still does not resolve to the selected UserAccount. Nothing should be recreated; please report this message.");
                }

                RecoveryPasswordEntry.Text = string.Empty;
                await LoadDatabaseInfoAsync();

                await DisplayAlert(
                    "Local Login Repaired + Verified",
                    $"The credential was written and verified for the existing account {account.Id}. The login email now resolves to that same account. Sign out of Guest and use {email} with the new password.",
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CollectIQ DB VIEW] Login repair failed: {ex}");
                await DisplayAlert("Local Login Recovery", $"Unable to repair the login: {ex.Message}", "OK");
            }
        }

        private static string CreateCompatiblePasswordHash(string password)
        {
            const int iterations = 100000;
            const int saltByteCount = 32;
            const int hashByteCount = 32;

            byte[] salt = RandomNumberGenerator.GetBytes(saltByteCount);
            using Rfc2898DeriveBytes pbkdf2 = new(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256);

            byte[] hash = pbkdf2.GetBytes(hashByteCount);
            return $"PBKDF2${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        private static bool VerifyCompatiblePasswordHash(string password, string storedHash)
        {
            try
            {
                if (!storedHash.StartsWith("PBKDF2$", StringComparison.OrdinalIgnoreCase))
                    return false;

                string[] parts = storedHash.Split('$');
                if (parts.Length != 4 || !int.TryParse(parts[1], out int iterations))
                    return false;

                byte[] salt = Convert.FromBase64String(parts[2]);
                byte[] expectedHash = Convert.FromBase64String(parts[3]);
                using Rfc2898DeriveBytes pbkdf2 = new(password, salt, iterations, HashAlgorithmName.SHA256);
                byte[] actualHash = pbkdf2.GetBytes(expectedHash.Length);
                return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }
            catch
            {
                return false;
            }
        }

        private async void OnExportDatabaseClicked(object sender, EventArgs e)
        {
            try
            {
                await database.InitializeAsync();
                SQLiteAsyncConnection connection = await database.GetConnectionAsync();

                string exportDirectory = Path.Combine(FileSystem.CacheDirectory, "DatabaseExports");
                Directory.CreateDirectory(exportDirectory);
                string exportPath = Path.Combine(exportDirectory, $"collectiq_{DateTime.UtcNow:yyyyMMdd_HHmmss}.db3");
                if (File.Exists(exportPath)) File.Delete(exportPath);

                // VACUUM INTO creates a consistent standalone SQLite snapshot even
                // when the live database is using WAL mode.
                string escaped = exportPath.Replace("'", "''");
                await connection.ExecuteAsync($"VACUUM INTO '{escaped}';");

                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = "Export CollectIQ SQLite Database",
                    File = new ShareFile(exportPath)
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CollectIQ DB VIEW] Export failed: {ex}");
                await DisplayAlert("Database Export", $"Unable to export the database: {ex.Message}", "OK");
            }
        }

        private async void OnGenerateAuthTraceClicked(object sender, EventArgs e)
        {
            await GenerateAuthTraceAsync();
        }

        private async void OnShareAuthTraceClicked(object sender, EventArgs e)
        {
            try
            {
                if (!File.Exists(AccountDiagnosticLogger.LatestSnapshotPath))
                {
                    await GenerateAuthTraceAsync();
                }

                if (!File.Exists(AccountDiagnosticLogger.LatestSnapshotPath))
                {
                    await DisplayAlert("Auth Trace", "The diagnostic text file could not be generated.", "OK");
                    return;
                }

                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = "CollectIQ Account Diagnostic Trace",
                    File = new ShareFile(AccountDiagnosticLogger.LatestSnapshotPath)
                });
            }
            catch (Exception ex)
            {
                await DisplayAlert("Auth Trace", $"Unable to share the diagnostic trace: {ex.Message}", "OK");
            }
        }

        private async Task GenerateAuthTraceAsync()
        {
            await AccountDiagnosticLogger.WriteSnapshotAsync(
                database,
                "DeveloperDatabase manual snapshot",
                RecoveryEmailEntry?.Text,
                "Manual diagnostic snapshot requested from Developer Database.");

            AuthTracePathLabel.Text = File.Exists(AccountDiagnosticLogger.LatestSnapshotPath)
                ? $"Auth trace: {AccountDiagnosticLogger.LatestSnapshotPath}"
                : "Auth trace: generation failed.";
        }

        private async Task GoBackAsync()
        {
            try
            {
                if (Navigation?.NavigationStack?.Count > 1) await Navigation.PopAsync();
                else await Shell.Current.GoToAsync("..");
            }
            catch { }
        }

        private async Task LoadDatabaseInfoAsync()
        {
            try
            {
                await database.InitializeAsync();
                SQLiteAsyncConnection connection = await database.GetConnectionAsync();
                await AccountDiagnosticLogger.WriteSnapshotAsync(
                    database,
                    "DeveloperDatabase LoadDatabaseInfo",
                    null,
                    "Developer database screen loaded/refreshed.");
                AuthTracePathLabel.Text = $"Auth trace: {AccountDiagnosticLogger.LatestSnapshotPath}";
                await LoadRecoveryAccountsAsync(connection);
                AccountsCollectionView.ItemsSource = await LoadAccountDiagnosticsAsync(connection);

                databasePath = database.GetDatabasePath();
                DatabasePathLabel.Text = databasePath;

                UsersCountLabel.Text = (await GetTableCountAsync(connection, "UserAccount")).ToString();
                ProfilesCountLabel.Text = (await GetTableCountAsync(connection, "UserProfile")).ToString();
                CredentialsCountLabel.Text = (await GetTableCountAsync(connection, "UserCredential")).ToString();
                CollectionsCountLabel.Text = (await GetTableCountAsync(connection, "CardCollection")).ToString();
                CardsCountLabel.Text = (await GetTableCountAsync(connection, "Card")).ToString();

                int schemaVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version;");
                SchemaVersionLabel.Text = schemaVersion.ToString();

                FileInfo file = new(databasePath);
                DatabaseSizeLabel.Text = file.Exists ? FormatBytes(file.Length) : "Missing";

                string backupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DatabaseBackups");
                BackupCountLabel.Text = Directory.Exists(backupRoot)
                    ? new DirectoryInfo(backupRoot).GetDirectories().Length.ToString()
                    : "0";

                string databaseId = await GetMetadataValueAsync(connection, "DatabaseInstanceId");
                DatabaseIdentityLabel.Text = string.IsNullOrWhiteSpace(databaseId) ? "Identity missing" : databaseId;

                string? lastKnownId = await SecureStorage.Default.GetAsync(LastKnownDatabaseIdentityKey);
                if (string.IsNullOrWhiteSpace(lastKnownId) && !string.IsNullOrWhiteSpace(databaseId))
                {
                    await SecureStorage.Default.SetAsync(LastKnownDatabaseIdentityKey, databaseId);
                    IdentityStatusLabel.Text = "Identity baseline saved. Future database replacements will be detectable here.";
                }
                else if (!string.IsNullOrWhiteSpace(databaseId) && !string.Equals(lastKnownId, databaseId, StringComparison.Ordinal))
                {
                    IdentityStatusLabel.Text = "WARNING: This database identity is different from the last database seen by this app. The local database file was replaced or recreated.";
                    IdentityStatusLabel.TextColor = Color.FromArgb("#F87171");
                }
                else
                {
                    IdentityStatusLabel.Text = "Same database identity as the last recorded run.";
                    IdentityStatusLabel.TextColor = Color.FromArgb("#86EFAC");
                }

                TablesCollectionView.ItemsSource = await LoadTableInfosAsync(connection);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CollectIQ DB VIEW] Load failed: {ex}");
                await DisplayAlert("Database", $"Unable to load database information: {ex.Message}", "OK");
            }
        }

        private static async Task<string> GetMetadataValueAsync(SQLiteAsyncConnection connection, string key)
        {
            try
            {
                return await connection.ExecuteScalarAsync<string>(
                    "SELECT MetadataValue FROM CollectIQDatabaseMetadata WHERE MetadataKey = ? LIMIT 1;", key) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static async Task<int> GetTableCountAsync(SQLiteAsyncConnection connection, string tableName)
        {
            try { return await connection.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM {tableName};"); }
            catch { return 0; }
        }

        private static async Task<List<DatabaseTableInfo>> LoadTableInfosAsync(SQLiteAsyncConnection connection)
        {
            List<SqliteMasterRow> rows = await connection.QueryAsync<SqliteMasterRow>(
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;");
            List<DatabaseTableInfo> result = new();
            foreach (SqliteMasterRow row in rows)
            {
                result.Add(new DatabaseTableInfo { Name = row.name, RowCount = await GetTableCountAsync(connection, row.name) });
            }
            return result;
        }

        private static async Task<List<AccountDiagnosticRow>> LoadAccountDiagnosticsAsync(SQLiteAsyncConnection connection)
        {
            List<UserAccount> accounts = await connection.Table<UserAccount>().ToListAsync();
            List<UserProfile> profiles = await connection.Table<UserProfile>().ToListAsync();
            List<UserCredential> credentials = await connection.Table<UserCredential>().ToListAsync();
            List<CardCollection> collections = await connection.Table<CardCollection>().ToListAsync();
            List<Card> cards = await connection.Table<Card>().ToListAsync();

            List<AccountDiagnosticRow> rows = new();
            foreach (UserAccount account in accounts.OrderBy(a => a.IsGuest).ThenBy(a => a.Email))
            {
                UserProfile? profile = profiles.FirstOrDefault(p => p.UserAccountId == account.Id);
                List<UserCredential> accountCredentials = credentials.Where(c => c.UserAccountId == account.Id).ToList();
                HashSet<string> ownedCollectionIds = collections
                    .Where(c => c.OwnerUserAccountId == account.Id && !c.IsDeleted)
                    .Select(c => c.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                int cardCount = cards.Count(c => !c.IsDeleted && ownedCollectionIds.Contains(c.CollectionId ?? string.Empty));

                string providerText = accountCredentials.Count == 0
                    ? "NONE"
                    : string.Join(", ", accountCredentials.Select(c => string.IsNullOrWhiteSpace(c.AuthProvider) ? "Unknown" : c.AuthProvider).Distinct());
                UserCredential? local = accountCredentials.FirstOrDefault(c => string.Equals(c.AuthProvider, "Local", StringComparison.OrdinalIgnoreCase));
                string passwordState = local == null || string.IsNullOrWhiteSpace(local.PasswordHash)
                    ? "MISSING"
                    : "SET (one-way hash; plaintext cannot be recovered)";
                string algorithm = local?.PasswordAlgorithm ?? "n/a";

                rows.Add(new AccountDiagnosticRow
                {
                    DisplayName = !string.IsNullOrWhiteSpace(profile?.DisplayName)
                        ? profile.DisplayName!
                        : (account.IsGuest ? "Guest" : "Unnamed account"),
                    Email = !string.IsNullOrWhiteSpace(account.Email)
                        ? account.Email
                        : profile?.Email ?? "(no email)",
                    AccountIdText = $"UserAccount: {account.Id} • {(account.IsGuest ? "Guest" : account.AccountStatus)}",
                    ProfileIdText = profile == null
                        ? "UserProfile: MISSING"
                        : $"UserProfile: {profile.Id} • linked account {profile.UserAccountId}",
                    CredentialText = $"Provider(s): {providerText} • Local password: {passwordState} • Algorithm: {algorithm}",
                    OwnershipText = $"Owned collections: {ownedCollectionIds.Count} • Cards in owned collections: {cardCount}"
                });
            }

            return rows;
        }

        private sealed class AccountDiagnosticRow
        {
            public string DisplayName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string AccountIdText { get; set; } = string.Empty;
            public string ProfileIdText { get; set; } = string.Empty;
            public string CredentialText { get; set; } = string.Empty;
            public string OwnershipText { get; set; } = string.Empty;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KB";
            return $"{bytes / (1024.0 * 1024.0):0.0} MB";
        }

        private sealed class SqliteMasterRow { public string name { get; set; } = string.Empty; }
        private sealed class DatabaseTableInfo { public string Name { get; set; } = string.Empty; public int RowCount { get; set; } }
        private async Task LoadRecoveryAccountsAsync(SQLiteAsyncConnection connection)
        {
            List<UserAccount> accounts = await connection.Table<UserAccount>().ToListAsync();
            List<UserProfile> profiles = await connection.Table<UserProfile>().ToListAsync();
            List<CardCollection> collections = await connection.Table<CardCollection>().ToListAsync();

            recoveryAccounts = accounts
                .OrderBy(account => account.IsGuest)
                .ThenBy(account => account.Email)
                .Select(account =>
                {
                    UserProfile? profile = profiles.FirstOrDefault(item => item.UserAccountId == account.Id);
                    int ownedCollections = collections.Count(collection => collection.OwnerUserAccountId == account.Id);
                    string email = !string.IsNullOrWhiteSpace(account.Email)
                        ? account.Email
                        : profile?.Email ?? string.Empty;
                    string display = !string.IsNullOrWhiteSpace(profile?.DisplayName)
                        ? profile.DisplayName!
                        : (!string.IsNullOrWhiteSpace(email) ? email : "Unnamed account");
                    string guestText = account.IsGuest ? " • GUEST" : string.Empty;
                    string itemText = $"{display} • collections {ownedCollections}{guestText}";
                    string details =
                        $"UserAccount ID: {account.Id}\n" +
                        $"Stored account email: {(string.IsNullOrWhiteSpace(account.Email) ? "(blank)" : account.Email)}\n" +
                        $"Profile email: {(string.IsNullOrWhiteSpace(profile?.Email) ? "(blank)" : profile!.Email)}\n" +
                        $"Display name: {(string.IsNullOrWhiteSpace(profile?.DisplayName) ? "(blank)" : profile!.DisplayName)}\n" +
                        $"Owns {ownedCollections} collection(s)" + guestText;
                    return new RecoveryAccountOption
                    {
                        UserAccountId = account.Id,
                        Email = email,
                        DisplayText = itemText,
                        Details = details
                    };
                })
                .ToList();

            RecoveryAccountPicker.ItemsSource = recoveryAccounts.Select(item => item.DisplayText).ToList();
            if (recoveryAccounts.Count > 0 && RecoveryAccountPicker.SelectedIndex < 0)
                RecoveryAccountPicker.SelectedIndex = 0;
        }

        private sealed class RecoveryAccountOption
        {
            public string UserAccountId { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string DisplayText { get; set; } = string.Empty;
            public string Details { get; set; } = string.Empty;
        }

    }
}
