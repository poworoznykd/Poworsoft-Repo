//
//  FILE            : LocalAuthService.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-19
//  DESCRIPTION     :
//      Implements local authentication logic for registration,
//      login, and session management.
//
using CollectIQ.Interfaces;
using CollectIQ.Models;
using CollectIQ.Services.Roles;
using CollectIQ.Services.Session;
using CollectIQ.Enums;
using Microsoft.Maui.Authentication;
using Microsoft.Maui.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CollectIQ.Services
{
    /// <summary>
    /// Handles local user authentication using SQLite storage.
    /// </summary>
    public sealed class LocalAuthService : IAuthService
    {
        private readonly IDatabase _db;
        private const string SessionKey = "current_user_email";
        private const string SessionProviderKey = "current_auth_provider";
        private const string LastLoginKey = "last_login";

        public LocalAuthService(IDatabase db)
        {
            _db = db;
        }

        public async Task<bool> RegisterAsync(string email, string password)
        {
            await _db.InitializeAsync();
            string hash = ComputeHash(password);
            var existing = await _db.GetUserProfileByEmailAsync(email);
            if (existing != null)
                return false; // already registered

            await _db.StorePasswordHashAsync(email, hash);
            await SecureStorage.SetAsync(SessionKey, email);
            return true;
        }

        public async Task<bool> LoginAsync(string email, string password)
        {
            await _db.InitializeAsync();
            var storedHash = await _db.GetPasswordHashAsync(email);
            if (storedHash == null)
                return false;

            if (storedHash != ComputeHash(password))
                return false;

            // ============================================================
            //  STEP 1: Store session credentials securely
            // ============================================================
            await SecureStorage.SetAsync(SessionKey, email);
            await SecureStorage.SetAsync(LastLoginKey, DateTime.UtcNow.ToString("O"));
            await SecureStorage.SetAsync(SessionProviderKey, AuthProvider.Local.ToString());

            // ============================================================
            // STEP 2: Load the full user object
            // ============================================================
            var user = await _db.GetUserProfileByEmailAsync(email);
            if (user == null)
                return false;

            // ============================================================
            // STEP 3: Resolve the role behavior (Strategy Pattern)
            // ============================================================
            var behaviors = new List<IUserRoleBehavior>
            {
                new AdminRoleBehavior(),
                new RegularRoleBehavior(),
                new GuestRoleBehavior()
            };

            var roleBehavior = behaviors.First(b => b.Role == user.Role);

            // ============================================================
            // STEP 4: Set Session
            // ============================================================
            UserSession.CurrentUser = user;
            UserSession.CurrentRoleBehavior = roleBehavior;

            return true;
        }

        // ============================================================
        //  OPTIONAL AUTH FLOWS
        // ============================================================

        public async Task<bool> SignInGuestAsync()
        {
            await _db.InitializeAsync();

            string email = $"guest-{Guid.NewGuid():N}@collectiq.local";

            var profile = new UserProfile
            {
                Email = email,
                DisplayName = "Guest",
                Role = UserRoles.Guest,
                CreatedUtc = DateTime.UtcNow,
                LastLoginUtc = DateTime.UtcNow
            };

            await _db.UpsertUserProfileAsync(profile);

            await SecureStorage.SetAsync(SessionKey, email);
            await SecureStorage.SetAsync(LastLoginKey, DateTime.UtcNow.ToString("O"));
            await SecureStorage.SetAsync(SessionProviderKey, AuthProvider.Guest.ToString());

            UserSession.CurrentUser = profile;
            UserSession.CurrentRoleBehavior = new GuestRoleBehavior();

            return true;
        }

        public async Task<bool> SignInWithProviderAsync(AuthProvider provider)
        {
            // Step 4: Implement Google OAuth (Authorization Code + PKCE).
            // Step 5: Facebook will be implemented later.

            try
            {
                if (provider == AuthProvider.Google)
                {
                    return await SignInWithGoogleAsync();
                }

                // Not implemented until Step 5
                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> SignInWithGoogleAsync()
        {
            if (string.IsNullOrWhiteSpace(SocialAuthSettings.GoogleClientId))
            {
                return false;
            }

            // PKCE
            string codeVerifier = CreateCodeVerifier();
            string codeChallenge = CreateCodeChallenge(codeVerifier);

            // CSRF protection
            string state = Guid.NewGuid().ToString("N");

            string authUrl = SocialAuthSettings.BuildGoogleAuthorizeUrl(codeChallenge, state);
            string callbackUrl = SocialAuthSettings.CallbackUrl;

            WebAuthenticatorResult result = await WebAuthenticator.AuthenticateAsync(
                new Uri(authUrl),
                new Uri(callbackUrl));

            // Validate state
            string returnedState = SocialAuthSettings.TryGetState(result);
            if (string.IsNullOrWhiteSpace(returnedState) || !string.Equals(state, returnedState, StringComparison.Ordinal))
            {
                return false;
            }

            // Get auth code
            string code = SocialAuthSettings.TryGetAuthCode(result);
            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            // Exchange code -> token
            TokenResponse? token = await ExchangeGoogleCodeForTokenAsync(code, codeVerifier);
            if (token == null || string.IsNullOrWhiteSpace(token.AccessToken))
            {
                return false;
            }

            // Get userinfo (email)
            string email = await GetGoogleEmailAsync(token.AccessToken);
            if (string.IsNullOrWhiteSpace(email))
            {
                return false;
            }

            // Persist user like local login
            await _db.InitializeAsync();

            UserProfile? profile = await _db.GetUserProfileByEmailAsync(email);
            if (profile == null)
            {
                profile = new UserProfile
                {
                    Email = email,
                    DisplayName = email,
                    Role = UserRoles.Regular,
                    CreatedUtc = DateTime.UtcNow
                };
            }

            profile.LastLoginUtc = DateTime.UtcNow;
            await _db.UpsertUserProfileAsync(profile);

            await SecureStorage.SetAsync(SessionKey, email);
            await SecureStorage.SetAsync(LastLoginKey, DateTime.UtcNow.ToString("O"));
            await SecureStorage.SetAsync(SessionProviderKey, AuthProvider.Google.ToString());

            UserSession.CurrentUser = profile;
            UserSession.CurrentRoleBehavior = new RegularRoleBehavior();

            return true;
        }

        private async Task<TokenResponse?> ExchangeGoogleCodeForTokenAsync(string code, string codeVerifier)
        {
            using HttpClient client = new HttpClient();

            var form = new Dictionary<string, string>
            {
                { "client_id", SocialAuthSettings.GoogleClientId },
                { "code", code },
                { "code_verifier", codeVerifier },
                { "redirect_uri", SocialAuthSettings.CallbackUrl },
                { "grant_type", "authorization_code" }
            };

            using var content = new FormUrlEncodedContent(form);

            using HttpResponseMessage response = await client.PostAsync(SocialAuthSettings.GoogleTokenEndpoint, content);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<TokenResponse>(json);
        }

        private async Task<string> GetGoogleEmailAsync(string accessToken)
        {
            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using HttpResponseMessage response = await client.GetAsync(SocialAuthSettings.GoogleUserInfoEndpoint);
            if (!response.IsSuccessStatusCode)
            {
                return string.Empty;
            }

            string json = await response.Content.ReadAsStringAsync();
            UserInfoResponse? userInfo = JsonSerializer.Deserialize<UserInfoResponse>(json);

            return userInfo?.Email ?? string.Empty;
        }

        private static string CreateCodeVerifier()
        {
            // 32 bytes -> 43-44 chars base64url (safe range for PKCE)
            byte[] bytes = RandomNumberGenerator.GetBytes(32);
            return Base64UrlEncode(bytes);
        }

        private static string CreateCodeChallenge(string verifier)
        {
            using var sha = SHA256.Create();
            byte[] bytes = Encoding.ASCII.GetBytes(verifier);
            byte[] hash = sha.ComputeHash(bytes);
            return Base64UrlEncode(hash);
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", string.Empty);
        }

        private sealed class TokenResponse
        {
            public string? AccessToken { get; set; }
            public string? IdToken { get; set; }
            public int ExpiresIn { get; set; }
            public string? TokenType { get; set; }

            // JSON property names from Google
            public string? access_token
            {
                get => AccessToken;
                set => AccessToken = value;
            }

            public string? id_token
            {
                get => IdToken;
                set => IdToken = value;
            }

            public int expires_in
            {
                get => ExpiresIn;
                set => ExpiresIn = value;
            }

            public string? token_type
            {
                get => TokenType;
                set => TokenType = value;
            }
        }

        private sealed class UserInfoResponse
        {
            public string? Email { get; set; }

            // JSON property names from Google userinfo
            public string? email
            {
                get => Email;
                set => Email = value;
            }
        }

        public async Task<UserProfile?> GetCurrentUserProfileAsync()
        {
            string? email = await GetCurrentUserEmailAsync();
            if (string.IsNullOrWhiteSpace(email))
            {
                return null;
            }

            await _db.InitializeAsync();
            return await _db.GetUserProfileByEmailAsync(email);
        }

        public async Task<bool> SignOutAsync()
        {
            SecureStorage.Remove(SessionKey);
            SecureStorage.Remove(SessionProviderKey);
            SecureStorage.Remove(LastLoginKey);

            UserSession.CurrentUser = null;
            UserSession.CurrentRoleBehavior = null;
            await Task.Delay(30);
            return true;
        }

        public async Task<bool> IsSignedInAsync()
        {
            var email = await SecureStorage.GetAsync(SessionKey);
            var lastLogin = await SecureStorage.GetAsync(LastLoginKey);

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(lastLogin))
                return false;

            // Expire session after 12 hours
            if (DateTime.TryParse(lastLogin, out DateTime timestamp))
            {
                return DateTime.UtcNow - timestamp < TimeSpan.FromHours(12);
            }

            return false;
        }

        public async Task<string?> GetCurrentUserEmailAsync()
        {
            return await SecureStorage.GetAsync(SessionKey);
        }

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = sha.ComputeHash(bytes);
            return Convert.ToHexString(hashBytes);
        }
    }
}