//
//  FILE            : LocalAuthService.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-19
//  UPDATED         : 2026-06-08
//  DESCRIPTION     :
//      Implements local authentication using the new CollectIQ account,
//      profile, credential, role, login history, and default collection model.
//      This keeps the existing UI working while moving credential storage out
//      of UserProfile and into UserCredential.
//

using System.Security.Cryptography;
using CollectIQ.Enums;
using CollectIQ.Interfaces;
using CollectIQ.Models;
using CollectIQ.Models.Auth;
using CollectIQ.Services.Roles;
using CollectIQ.Services.Session;
using Microsoft.Maui.Storage;

namespace CollectIQ.Services
{
    /// <summary>
    /// Handles local user authentication using the CollectIQ SQLite database.
    /// </summary>
    public sealed class LocalAuthService : IAuthService
    {
        private const string SessionKey = "current_user_email";
        private const string SessionProviderKey = "current_auth_provider";
        private const string LastLoginKey = "last_login";
        private const int PasswordIterations = 100000;
        private const int SaltByteCount = 32;
        private const int HashByteCount = 32;

        private readonly IDatabase database;
        private readonly ISocialAuthService socialAuthService;

        /// <summary>
        /// Initializes a new instance of the LocalAuthService class.
        /// </summary>
        /// <param name="database">The local database abstraction.</param>
        public LocalAuthService(IDatabase database)
            : this(database, new NoOpSocialAuthService())
        {
        }

        /// <summary>
        /// Initializes a new instance of the LocalAuthService class.
        /// </summary>
        /// <param name="database">The local database abstraction.</param>
        /// <param name="socialAuthService">The social authentication broker.</param>
        public LocalAuthService(IDatabase database, ISocialAuthService socialAuthService)
        {
            this.database = database;
            this.socialAuthService = socialAuthService;
        }

        #region Public Authentication Methods

        /// <summary>
        /// Registers a new local CollectIQ account.
        /// </summary>
        /// <param name="email">The user's email address.</param>
        /// <param name="password">The user's plain-text password.</param>
        /// <returns>True when registration succeeds; otherwise false.</returns>
        public async Task<bool> RegisterAsync(string email, string password)
        {
            await database.InitializeAsync();

            string normalizedEmail = NormalizeEmail(email);

            if (string.IsNullOrWhiteSpace(normalizedEmail) || string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            if (await database.UserExistsAsync(normalizedEmail))
            {
                return false;
            }

            UserAccount account = new UserAccount
            {
                Email = normalizedEmail,
                EmailNormalized = normalizedEmail,
                AccountStatus = AccountStatuses.Active,
                IsGuest = false,
                LastLoginUtc = DateTime.UtcNow,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };

            await database.UpsertUserAccountAsync(account);

            string passwordHash = CreatePasswordHash(password);

            UserCredential credential = new UserCredential
            {
                UserAccountId = account.Id,
                AuthProvider = AuthProvider.Local.ToString(),
                PasswordHash = passwordHash,
                PasswordAlgorithm = "PBKDF2-SHA256-100000",
                LastChangedUtc = DateTime.UtcNow,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };

            await database.UpsertUserCredentialAsync(credential);

            UserProfile profile = new UserProfile
            {
                UserAccountId = account.Id,
                Email = normalizedEmail,
                DisplayName = normalizedEmail,
                Role = UserRoles.Regular,
                LastLoginUtc = DateTime.UtcNow,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow,
                PasswordHash = passwordHash
            };

            await database.UpsertUserProfileAsync(profile);
            await database.GetOrCreateDefaultCollectionAsync(account.Id);
            await StoreSessionAsync(normalizedEmail, AuthProvider.Local);
            await SetCurrentUserAsync(profile);

            await database.RecordLoginHistoryAsync(new LoginHistory
            {
                UserAccountId = account.Id,
                EmailNormalized = normalizedEmail,
                AuthProvider = AuthProvider.Local.ToString(),
                WasSuccessful = true,
                LoginUtc = DateTime.UtcNow
            });

            return true;
        }

        /// <summary>
        /// Logs in a local CollectIQ user.
        /// </summary>
        /// <param name="email">The user's email address.</param>
        /// <param name="password">The user's plain-text password.</param>
        /// <returns>True when login succeeds; otherwise false.</returns>
        public async Task<bool> LoginAsync(string email, string password)
        {
            await database.InitializeAsync();

            string normalizedEmail = NormalizeEmail(email);

            UserAccount? account = await database.GetUserAccountByEmailAsync(normalizedEmail);
            UserProfile? profile = await database.GetUserProfileByEmailAsync(normalizedEmail);

            if (account == null && profile != null)
            {
                account = await CreateAccountForLegacyProfileAsync(profile);
            }

            if (account == null)
            {
                await RecordFailedLoginAsync(string.Empty, normalizedEmail, "Account not found.");
                return false;
            }

            if (!string.Equals(account.AccountStatus, AccountStatuses.Active, StringComparison.OrdinalIgnoreCase))
            {
                await RecordFailedLoginAsync(account.Id, normalizedEmail, "Account is not active.");
                return false;
            }

            UserCredential? credential = await database.GetLocalCredentialAsync(account.Id);
            string? storedHash = credential?.PasswordHash ?? profile?.PasswordHash;

            if (string.IsNullOrWhiteSpace(storedHash))
            {
                await RecordFailedLoginAsync(account.Id, normalizedEmail, "Password hash missing.");
                return false;
            }

            bool passwordMatches = VerifyPassword(password, storedHash);

            if (!passwordMatches)
            {
                await RecordFailedLoginAsync(account.Id, normalizedEmail, "Invalid password.");
                return false;
            }

            // Upgrade older SHA256 hashes to PBKDF2 after a successful login.
            if (!storedHash.StartsWith("PBKDF2$", StringComparison.OrdinalIgnoreCase))
            {
                string upgradedHash = CreatePasswordHash(password);

                credential ??= new UserCredential
                {
                    UserAccountId = account.Id,
                    AuthProvider = AuthProvider.Local.ToString(),
                    CreatedUtc = DateTime.UtcNow
                };

                credential.PasswordHash = upgradedHash;
                credential.PasswordAlgorithm = "PBKDF2-SHA256-100000";
                credential.LastChangedUtc = DateTime.UtcNow;
                credential.UpdatedUtc = DateTime.UtcNow;

                await database.UpsertUserCredentialAsync(credential);

                if (profile != null)
                {
                    profile.PasswordHash = upgradedHash;
                    await database.UpsertUserProfileAsync(profile);
                }
            }

            account.LastLoginUtc = DateTime.UtcNow;
            await database.UpsertUserAccountAsync(account);

            profile ??= await EnsureProfileForAccountAsync(account, UserRoles.Regular);
            profile.LastLoginUtc = DateTime.UtcNow;
            profile.UserAccountId = account.Id;
            await database.UpsertUserProfileAsync(profile);

            await database.GetOrCreateDefaultCollectionAsync(account.Id);
            await StoreSessionAsync(normalizedEmail, AuthProvider.Local);
            await SetCurrentUserAsync(profile);

            await database.RecordLoginHistoryAsync(new LoginHistory
            {
                UserAccountId = account.Id,
                EmailNormalized = normalizedEmail,
                AuthProvider = AuthProvider.Local.ToString(),
                WasSuccessful = true,
                LoginUtc = DateTime.UtcNow
            });

            return true;
        }

        /// <summary>
        /// Signs out the current user.
        /// </summary>
        /// <returns>True when sign-out succeeds.</returns>
        public Task<bool> SignOutAsync()
        {
            SecureStorage.Remove(SessionKey);
            SecureStorage.Remove(SessionProviderKey);
            SecureStorage.Remove(LastLoginKey);

            UserSession.CurrentUser = null;
            UserSession.CurrentRoleBehavior = null;

            return Task.FromResult(true);
        }

        /// <summary>
        /// Determines whether the current local session is still valid.
        /// </summary>
        /// <returns>True when the user is signed in; otherwise false.</returns>
        public async Task<bool> IsSignedInAsync()
        {
            string? email = await SecureStorage.GetAsync(SessionKey);
            string? lastLogin = await SecureStorage.GetAsync(LastLoginKey);

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(lastLogin))
            {
                return false;
            }

            if (!DateTime.TryParse(lastLogin, out DateTime timestamp))
            {
                return false;
            }

            if (DateTime.UtcNow - timestamp > TimeSpan.FromDays(30))
            {
                return false;
            }

            UserProfile? profile = await database.GetUserProfileByEmailAsync(email);

            if (profile == null)
            {
                return false;
            }

            await SetCurrentUserAsync(profile);
            return true;
        }

        /// <summary>
        /// Gets the current signed-in user's email address.
        /// </summary>
        /// <returns>The signed-in email address, or null.</returns>
        public Task<string?> GetCurrentUserEmailAsync()
        {
            return SecureStorage.GetAsync(SessionKey);
        }

        /// <summary>
        /// Gets the current signed-in user's profile.
        /// </summary>
        /// <returns>The current user profile, or null.</returns>
        public async Task<UserProfile?> GetCurrentUserProfileAsync()
        {
            string? email = await GetCurrentUserEmailAsync();

            if (string.IsNullOrWhiteSpace(email))
            {
                return null;
            }

            UserProfile? profile = await database.GetUserProfileByEmailAsync(email);

            if (profile != null)
            {
                await SetCurrentUserAsync(profile);
            }

            return profile;
        }

        #endregion

        #region Guest and Provider Sign In

        /// <summary>
        /// Signs the user in as a guest.
        /// </summary>
        /// <returns>True when guest sign-in succeeds.</returns>
        public async Task<bool> SignInGuestAsync()
        {
            await database.InitializeAsync();

            string email = $"guest-{Guid.NewGuid():N}@collectiq.local";
            string normalizedEmail = NormalizeEmail(email);

            UserAccount account = new UserAccount
            {
                Email = normalizedEmail,
                EmailNormalized = normalizedEmail,
                AccountStatus = AccountStatuses.Active,
                IsGuest = true,
                LastLoginUtc = DateTime.UtcNow,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };

            await database.UpsertUserAccountAsync(account);

            UserProfile profile = new UserProfile
            {
                UserAccountId = account.Id,
                Email = normalizedEmail,
                DisplayName = "Guest",
                Role = UserRoles.Guest,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow,
                LastLoginUtc = DateTime.UtcNow
            };

            await database.UpsertUserProfileAsync(profile);
            await database.GetOrCreateDefaultCollectionAsync(account.Id);
            await StoreSessionAsync(normalizedEmail, AuthProvider.Guest);
            await SetCurrentUserAsync(profile);

            return true;
        }

        /// <summary>
        /// Signs the user in using a social authentication provider through the configured broker.
        /// </summary>
        /// <param name="provider">The auth provider.</param>
        /// <returns>True when sign-in succeeds; otherwise false.</returns>
        public async Task<bool> SignInWithProviderAsync(AuthProvider provider)
        {
            await database.InitializeAsync();

            SocialAuthUser socialUser = await socialAuthService.SignInAsync(provider);

            if (!socialUser.IsSuccess)
            {
                string errorMessage = string.IsNullOrWhiteSpace(socialUser.ErrorMessage)
                    ? $"{provider} sign-in failed."
                    : socialUser.ErrorMessage;

                System.Diagnostics.Debug.WriteLine($"[CollectIQ AUTH] {provider} sign-in failed: {errorMessage}");
                throw new InvalidOperationException(errorMessage);
            }

            string normalizedEmail = NormalizeEmail(socialUser.Email);

            if (string.IsNullOrWhiteSpace(normalizedEmail))
            {
                normalizedEmail = $"{provider.ToString().ToLowerInvariant()}-{socialUser.ProviderUserId}@collectiq.local";
            }

            UserAccount? account = await database.GetUserAccountByEmailAsync(normalizedEmail);

            if (account == null)
            {
                account = new UserAccount
                {
                    Email = normalizedEmail,
                    EmailNormalized = normalizedEmail,
                    AccountStatus = AccountStatuses.Active,
                    IsEmailVerified = true,
                    IsGuest = false,
                    LastLoginUtc = DateTime.UtcNow,
                    CreatedUtc = DateTime.UtcNow,
                    UpdatedUtc = DateTime.UtcNow
                };
            }
            else
            {
                account.Email = normalizedEmail;
                account.EmailNormalized = normalizedEmail;
                account.AccountStatus = AccountStatuses.Active;
                account.IsGuest = false;
                account.LastLoginUtc = DateTime.UtcNow;
                account.UpdatedUtc = DateTime.UtcNow;
            }

            await database.UpsertUserAccountAsync(account);

            UserCredential credential = new UserCredential
            {
                UserAccountId = account.Id,
                AuthProvider = provider.ToString(),
                ProviderUserId = socialUser.ProviderUserId,
                PasswordHash = null,
                PasswordAlgorithm = "ExternalOAuth",
                LastChangedUtc = DateTime.UtcNow,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };

            await database.UpsertUserCredentialAsync(credential);

            UserProfile? profile = await database.GetUserProfileByEmailAsync(normalizedEmail);

            if (profile == null)
            {
                profile = new UserProfile
                {
                    UserAccountId = account.Id,
                    Email = normalizedEmail,
                    DisplayName = string.IsNullOrWhiteSpace(socialUser.DisplayName)
                        ? normalizedEmail
                        : socialUser.DisplayName,
                    ProviderUserId = socialUser.ProviderUserId,
                    Role = UserRoles.Regular,
                    CreatedUtc = DateTime.UtcNow,
                    UpdatedUtc = DateTime.UtcNow,
                    LastLoginUtc = DateTime.UtcNow
                };
            }
            else
            {
                profile.UserAccountId = account.Id;
                profile.Email = normalizedEmail;
                profile.ProviderUserId = socialUser.ProviderUserId;
                profile.DisplayName = string.IsNullOrWhiteSpace(socialUser.DisplayName)
                    ? profile.DisplayName
                    : socialUser.DisplayName;
                profile.Role = UserRoles.Normalize(profile.Role);
                profile.LastLoginUtc = DateTime.UtcNow;
                profile.UpdatedUtc = DateTime.UtcNow;
            }

            await database.UpsertUserProfileAsync(profile);
            await database.GetOrCreateDefaultCollectionAsync(account.Id);
            await StoreSessionAsync(normalizedEmail, provider);
            await SetCurrentUserAsync(profile);

            await database.RecordLoginHistoryAsync(new LoginHistory
            {
                UserAccountId = account.Id,
                EmailNormalized = normalizedEmail,
                AuthProvider = provider.ToString(),
                WasSuccessful = true,
                LoginUtc = DateTime.UtcNow
            });

            return true;
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Creates an account for an older UserProfile-only record.
        /// </summary>
        /// <param name="profile">The legacy profile.</param>
        /// <returns>The created user account.</returns>
        private async Task<UserAccount> CreateAccountForLegacyProfileAsync(UserProfile profile)
        {
            string normalizedEmail = NormalizeEmail(profile.Email);

            UserAccount account = new UserAccount
            {
                Email = normalizedEmail,
                EmailNormalized = normalizedEmail,
                AccountStatus = AccountStatuses.Active,
                IsGuest = string.Equals(profile.Role, UserRoles.Guest, StringComparison.OrdinalIgnoreCase),
                LastLoginUtc = profile.LastLoginUtc,
                CreatedUtc = profile.CreatedUtc == default ? DateTime.UtcNow : profile.CreatedUtc,
                UpdatedUtc = DateTime.UtcNow
            };

            await database.UpsertUserAccountAsync(account);

            profile.UserAccountId = account.Id;
            profile.Email = normalizedEmail;
            await database.UpsertUserProfileAsync(profile);

            if (!string.IsNullOrWhiteSpace(profile.PasswordHash))
            {
                await database.UpsertUserCredentialAsync(new UserCredential
                {
                    UserAccountId = account.Id,
                    AuthProvider = AuthProvider.Local.ToString(),
                    PasswordHash = profile.PasswordHash,
                    PasswordAlgorithm = profile.PasswordHash.StartsWith("PBKDF2$", StringComparison.OrdinalIgnoreCase)
                        ? "PBKDF2-SHA256-100000"
                        : "Legacy-SHA256",
                    CreatedUtc = DateTime.UtcNow,
                    UpdatedUtc = DateTime.UtcNow
                });
            }

            return account;
        }

        /// <summary>
        /// Ensures a profile exists for an account.
        /// </summary>
        /// <param name="account">The account.</param>
        /// <param name="role">The role to assign.</param>
        /// <returns>The profile.</returns>
        private async Task<UserProfile> EnsureProfileForAccountAsync(UserAccount account, string role)
        {
            UserProfile? profile = await database.GetUserProfileByEmailAsync(account.EmailNormalized);

            if (profile != null)
            {
                return profile;
            }

            profile = new UserProfile
            {
                UserAccountId = account.Id,
                Email = account.EmailNormalized,
                DisplayName = account.Email,
                Role = role,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };

            await database.UpsertUserProfileAsync(profile);
            return profile;
        }

        /// <summary>
        /// Records a failed login attempt.
        /// </summary>
        /// <param name="userAccountId">The user account ID, if known.</param>
        /// <param name="emailNormalized">The normalized email address.</param>
        /// <param name="reason">The failure reason.</param>
        private async Task RecordFailedLoginAsync(string userAccountId, string emailNormalized, string reason)
        {
            await database.RecordLoginHistoryAsync(new LoginHistory
            {
                UserAccountId = userAccountId,
                EmailNormalized = emailNormalized,
                AuthProvider = AuthProvider.Local.ToString(),
                WasSuccessful = false,
                FailureReason = reason,
                LoginUtc = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Stores the active session values.
        /// </summary>
        /// <param name="email">The normalized email address.</param>
        /// <param name="provider">The auth provider.</param>
        private static async Task StoreSessionAsync(string email, AuthProvider provider)
        {
            await SecureStorage.SetAsync(SessionKey, email);
            await SecureStorage.SetAsync(LastLoginKey, DateTime.UtcNow.ToString("O"));
            await SecureStorage.SetAsync(SessionProviderKey, provider.ToString());
        }

        /// <summary>
        /// Updates the current in-memory user session.
        /// </summary>
        /// <param name="profile">The current user profile.</param>
        private static Task SetCurrentUserAsync(UserProfile profile)
        {
            profile.Role = UserRoles.Normalize(profile.Role);
            UserSession.CurrentUser = profile;
            UserSession.CurrentRoleBehavior = RoleBehaviorResolver.Resolve(profile.Role);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Normalizes an email address.
        /// </summary>
        /// <param name="email">The email address.</param>
        /// <returns>The normalized email address.</returns>
        private static string NormalizeEmail(string? email)
        {
            return (email ?? string.Empty).Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Creates a PBKDF2 password hash string.
        /// </summary>
        /// <param name="password">The plain-text password.</param>
        /// <returns>The encoded password hash.</returns>
        private static string CreatePasswordHash(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltByteCount);

            using Rfc2898DeriveBytes pbkdf2 = new Rfc2898DeriveBytes(
                password,
                salt,
                PasswordIterations,
                HashAlgorithmName.SHA256);

            byte[] hash = pbkdf2.GetBytes(HashByteCount);

            return $"PBKDF2${PasswordIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        /// <summary>
        /// Verifies a password against either the new PBKDF2 format or the older SHA256 format.
        /// </summary>
        /// <param name="password">The plain-text password.</param>
        /// <param name="storedHash">The stored hash.</param>
        /// <returns>True when the password matches; otherwise false.</returns>
        private static bool VerifyPassword(string password, string storedHash)
        {
            if (storedHash.StartsWith("PBKDF2$", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = storedHash.Split('$');

                if (parts.Length != 4 || !int.TryParse(parts[1], out int iterations))
                {
                    return false;
                }

                byte[] salt = Convert.FromBase64String(parts[2]);
                byte[] expectedHash = Convert.FromBase64String(parts[3]);

                using Rfc2898DeriveBytes pbkdf2 = new Rfc2898DeriveBytes(
                    password,
                    salt,
                    iterations,
                    HashAlgorithmName.SHA256);

                byte[] actualHash = pbkdf2.GetBytes(expectedHash.Length);

                return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }

            string legacyHash = CreateLegacySha256Hash(password);
            return string.Equals(storedHash, legacyHash, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Creates the legacy SHA256 hash used by older CollectIQ builds.
        /// </summary>
        /// <param name="input">The plain-text password.</param>
        /// <returns>The legacy hash.</returns>
        private static string CreateLegacySha256Hash(string input)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = SHA256.HashData(bytes);
            return Convert.ToHexString(hashBytes);
        }

        #endregion
    }
}
