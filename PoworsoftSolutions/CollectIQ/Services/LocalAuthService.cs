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

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CollectIQ.Enums;
using CollectIQ.Interfaces;
using CollectIQ.Models;
using CollectIQ.Services.Roles;
using CollectIQ.Services.Session;
using Microsoft.Maui.Authentication;
using Microsoft.Maui.Storage;
using SQLite;

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

        /// <summary>
        /// Initializes a new instance of the LocalAuthService class.
        /// </summary>
        /// <param name="database">The local database abstraction.</param>
        public LocalAuthService(IDatabase database)
        {
            this.database = database;
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
        /// Attempts social provider sign-in using MAUI WebAuthenticator.
        /// </summary>
        /// <param name="provider">The authentication provider.</param>
        /// <returns>True when provider sign-in succeeds; otherwise false.</returns>
        public async Task<bool> SignInWithProviderAsync(AuthProvider provider)
        {
            await database.InitializeAsync();

            try
            {
                if (provider == AuthProvider.Google)
                {
                    return await SignInWithGoogleAsync();
                }

                if (provider == AuthProvider.Facebook)
                {
                    return await SignInWithFacebookAsync();
                }

                return false;
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine($"[CollectIQ AUTH] {provider} sign-in was cancelled.");
                return false;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[CollectIQ AUTH] {provider} sign-in was cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CollectIQ AUTH] {provider} sign-in failed: {ex}");
                return false;
            }
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Signs in with Google using Authorization Code with PKCE.
        /// </summary>
        /// <returns>True when Google sign-in succeeds.</returns>
        private async Task<bool> SignInWithGoogleAsync()
        {
            if (string.IsNullOrWhiteSpace(SocialAuthSettings.GoogleClientId))
            {
                Debug.WriteLine("[CollectIQ AUTH] Google client ID is not configured.");
                return false;
            }

            string state = CreateBase64Url(RandomNumberGenerator.GetBytes(32));
            string codeVerifier = CreatePkceVerifier();
            string codeChallenge = CreatePkceChallenge(codeVerifier);
            string authorizeUrl = SocialAuthSettings.BuildGoogleAuthorizeUrl(codeChallenge, state);

            WebAuthenticatorResult result = await WebAuthenticator.Default.AuthenticateAsync(
                new Uri(authorizeUrl),
                new Uri(SocialAuthSettings.CallbackUrl));

            string returnedState = SocialAuthSettings.TryGetState(result);
            if (!string.Equals(returnedState, state, StringComparison.Ordinal))
            {
                Debug.WriteLine("[CollectIQ AUTH] Google state validation failed.");
                return false;
            }

            string authCode = SocialAuthSettings.TryGetAuthCode(result);
            if (string.IsNullOrWhiteSpace(authCode))
            {
                Debug.WriteLine("[CollectIQ AUTH] Google did not return an authorization code.");
                return false;
            }

            string accessToken = await ExchangeGoogleCodeForAccessTokenAsync(authCode, codeVerifier);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                Debug.WriteLine("[CollectIQ AUTH] Google token exchange failed.");
                return false;
            }

            SocialUserInfo? userInfo = await GetGoogleUserInfoAsync(accessToken);
            if (userInfo == null)
            {
                Debug.WriteLine("[CollectIQ AUTH] Google user info lookup failed.");
                return false;
            }

            return await CompleteProviderSignInAsync(AuthProvider.Google, userInfo);
        }

        /// <summary>
        /// Signs in with Facebook using MAUI WebAuthenticator.
        /// </summary>
        /// <returns>True when Facebook sign-in succeeds.</returns>
        private async Task<bool> SignInWithFacebookAsync()
        {
            if (string.IsNullOrWhiteSpace(SocialAuthSettings.FacebookClientId))
            {
                Debug.WriteLine("[CollectIQ AUTH] Facebook client ID is not configured.");
                return false;
            }

            string state = CreateBase64Url(RandomNumberGenerator.GetBytes(32));
            string authorizeUrl = SocialAuthSettings.BuildFacebookAuthorizeUrl(state);

            WebAuthenticatorResult result = await WebAuthenticator.Default.AuthenticateAsync(
                new Uri(authorizeUrl),
                new Uri(SocialAuthSettings.CallbackUrl));

            string returnedState = SocialAuthSettings.TryGetState(result);
            if (!string.IsNullOrWhiteSpace(returnedState) && !string.Equals(returnedState, state, StringComparison.Ordinal))
            {
                Debug.WriteLine("[CollectIQ AUTH] Facebook state validation failed.");
                return false;
            }

            string accessToken = SocialAuthSettings.TryGetAccessToken(result);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                Debug.WriteLine("[CollectIQ AUTH] Facebook did not return an access token.");
                return false;
            }

            SocialUserInfo? userInfo = await GetFacebookUserInfoAsync(accessToken);
            if (userInfo == null)
            {
                Debug.WriteLine("[CollectIQ AUTH] Facebook user info lookup failed.");
                return false;
            }

            return await CompleteProviderSignInAsync(AuthProvider.Facebook, userInfo);
        }

        /// <summary>
        /// Completes provider sign-in by creating or updating the local authorized user cache.
        /// </summary>
        /// <param name="provider">The auth provider.</param>
        /// <param name="userInfo">The provider user information.</param>
        /// <returns>True when local account setup succeeds.</returns>
        private async Task<bool> CompleteProviderSignInAsync(AuthProvider provider, SocialUserInfo userInfo)
        {
            if (userInfo == null || string.IsNullOrWhiteSpace(userInfo.ProviderUserId))
            {
                return false;
            }

            string normalizedEmail = NormalizeEmail(userInfo.Email);

            if (string.IsNullOrWhiteSpace(normalizedEmail))
            {
                normalizedEmail = $"{provider.ToString().ToLowerInvariant()}-{userInfo.ProviderUserId}@collectiq.provider.local";
            }

            UserAccount? account = await database.GetUserAccountByEmailAsync(normalizedEmail);
            DateTime now = DateTime.UtcNow;

            if (account == null)
            {
                account = new UserAccount
                {
                    Email = normalizedEmail,
                    EmailNormalized = normalizedEmail,
                    AccountStatus = AccountStatuses.Active,
                    IsEmailVerified = !normalizedEmail.EndsWith("@collectiq.provider.local", StringComparison.OrdinalIgnoreCase),
                    IsGuest = false,
                    CreatedUtc = now,
                    UpdatedUtc = now,
                    LastLoginUtc = now
                };
            }
            else
            {
                account.Email = normalizedEmail;
                account.EmailNormalized = normalizedEmail;
                account.AccountStatus = AccountStatuses.Active;
                account.IsGuest = false;
                account.LastLoginUtc = now;
                account.UpdatedUtc = now;
            }

            await database.UpsertUserAccountAsync(account);

            UserCredential credential = await GetOrCreateProviderCredentialAsync(account.Id, provider, userInfo.ProviderUserId);
            credential.ProviderUserId = userInfo.ProviderUserId;
            credential.PasswordHash = null;
            credential.PasswordSalt = null;
            credential.PasswordAlgorithm = "ExternalProvider";
            credential.UpdatedUtc = now;
            credential.LastChangedUtc = credential.LastChangedUtc ?? now;

            await database.UpsertUserCredentialAsync(credential);

            UserProfile profile = await EnsureProfileForAccountAsync(account, UserRoles.Regular);
            profile.UserAccountId = account.Id;
            profile.Email = normalizedEmail;
            profile.ProviderUserId = userInfo.ProviderUserId;
            profile.Role = UserRoles.Regular;
            profile.LastLoginUtc = now;
            profile.UpdatedUtc = now;

            if (!string.IsNullOrWhiteSpace(userInfo.DisplayName))
            {
                profile.DisplayName = userInfo.DisplayName;
            }
            else if (string.IsNullOrWhiteSpace(profile.DisplayName))
            {
                profile.DisplayName = normalizedEmail;
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
                LoginUtc = now
            });

            return true;
        }

        /// <summary>
        /// Gets or creates a provider credential for an account.
        /// </summary>
        /// <param name="userAccountId">The account ID.</param>
        /// <param name="provider">The auth provider.</param>
        /// <param name="providerUserId">The provider user ID.</param>
        /// <returns>The provider credential.</returns>
        private async Task<UserCredential> GetOrCreateProviderCredentialAsync(string userAccountId, AuthProvider provider, string providerUserId)
        {
            SQLiteAsyncConnection connection = await database.GetConnectionAsync();
            string providerName = provider.ToString();

            UserCredential? credential = await connection.Table<UserCredential>()
                .Where(c => c.UserAccountId == userAccountId && c.AuthProvider == providerName)
                .FirstOrDefaultAsync();

            credential ??= await connection.Table<UserCredential>()
                .Where(c => c.AuthProvider == providerName && c.ProviderUserId == providerUserId)
                .FirstOrDefaultAsync();

            return credential ?? new UserCredential
            {
                UserAccountId = userAccountId,
                AuthProvider = providerName,
                ProviderUserId = providerUserId,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Exchanges a Google authorization code for an access token.
        /// </summary>
        /// <param name="authCode">The authorization code.</param>
        /// <param name="codeVerifier">The PKCE code verifier.</param>
        /// <returns>The access token, or an empty string.</returns>
        private static async Task<string> ExchangeGoogleCodeForAccessTokenAsync(string authCode, string codeVerifier)
        {
            using HttpClient client = new HttpClient();

            Dictionary<string, string> form = new Dictionary<string, string>
            {
                ["client_id"] = SocialAuthSettings.GoogleClientId,
                ["code"] = authCode,
                ["code_verifier"] = codeVerifier,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = SocialAuthSettings.CallbackUrl
            };

            using HttpResponseMessage response = await client.PostAsync(
                SocialAuthSettings.GoogleTokenEndpoint,
                new FormUrlEncodedContent(form));

            string json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[CollectIQ AUTH] Google token response: {(int)response.StatusCode} {json}");
                return string.Empty;
            }

            using JsonDocument document = JsonDocument.Parse(json);

            if (document.RootElement.TryGetProperty("access_token", out JsonElement tokenElement))
            {
                return tokenElement.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        /// <summary>
        /// Gets Google user profile information from the OpenID userinfo endpoint.
        /// </summary>
        /// <param name="accessToken">The access token.</param>
        /// <returns>The social user info, or null.</returns>
        private static async Task<SocialUserInfo?> GetGoogleUserInfoAsync(string accessToken)
        {
            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            string json = await client.GetStringAsync(SocialAuthSettings.GoogleUserInfoEndpoint);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            string providerId = GetJsonString(root, "sub");
            string email = GetJsonString(root, "email");
            string name = GetJsonString(root, "name");

            if (string.IsNullOrWhiteSpace(providerId))
            {
                return null;
            }

            return new SocialUserInfo
            {
                ProviderUserId = providerId,
                Email = email,
                DisplayName = name
            };
        }

        /// <summary>
        /// Gets Facebook user profile information from the Graph API.
        /// </summary>
        /// <param name="accessToken">The access token.</param>
        /// <returns>The social user info, or null.</returns>
        private static async Task<SocialUserInfo?> GetFacebookUserInfoAsync(string accessToken)
        {
            using HttpClient client = new HttpClient();
            string url = $"{SocialAuthSettings.FacebookUserInfoEndpoint}?fields=id,name,email&access_token={Uri.EscapeDataString(accessToken)}";
            string json = await client.GetStringAsync(url);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            string providerId = GetJsonString(root, "id");
            string email = GetJsonString(root, "email");
            string name = GetJsonString(root, "name");

            if (string.IsNullOrWhiteSpace(providerId))
            {
                return null;
            }

            return new SocialUserInfo
            {
                ProviderUserId = providerId,
                Email = email,
                DisplayName = name
            };
        }

        /// <summary>
        /// Gets a string property from a JSON element.
        /// </summary>
        /// <param name="root">The root JSON element.</param>
        /// <param name="propertyName">The property name.</param>
        /// <returns>The value, or an empty string.</returns>
        private static string GetJsonString(JsonElement root, string propertyName)
        {
            if (root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        /// <summary>
        /// Creates a PKCE verifier value.
        /// </summary>
        /// <returns>The verifier.</returns>
        private static string CreatePkceVerifier()
        {
            return CreateBase64Url(RandomNumberGenerator.GetBytes(64));
        }

        /// <summary>
        /// Creates the PKCE code challenge for a verifier.
        /// </summary>
        /// <param name="verifier">The verifier.</param>
        /// <returns>The code challenge.</returns>
        private static string CreatePkceChallenge(string verifier)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(verifier);
            byte[] hash = SHA256.HashData(bytes);
            return CreateBase64Url(hash);
        }

        /// <summary>
        /// Converts bytes to Base64 URL format.
        /// </summary>
        /// <param name="bytes">The bytes.</param>
        /// <returns>The Base64 URL value.</returns>
        private static string CreateBase64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        /// <summary>
        /// Contains normalized social user information returned by a provider.
        /// </summary>
        private sealed class SocialUserInfo
        {
            public string ProviderUserId { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
        }

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
