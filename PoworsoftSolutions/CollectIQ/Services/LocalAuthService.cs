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
            // Minimal OAuth scaffold using WebAuthenticator.
            // You must configure provider client ids + redirect URI for it to work.

            var (authUrl, callbackUrl) = SocialAuthSettings.GetAuthUrls(provider);
            if (string.IsNullOrWhiteSpace(authUrl) || string.IsNullOrWhiteSpace(callbackUrl))
            {
                return false;
            }

            WebAuthenticatorResult result = await WebAuthenticator.AuthenticateAsync(
                new Uri(authUrl),
                new Uri(callbackUrl));

            string email = SocialAuthSettings.TryGetEmail(result);
            if (string.IsNullOrWhiteSpace(email))
            {
                return false;
            }

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
            await SecureStorage.SetAsync(SessionProviderKey, provider.ToString());

            UserSession.CurrentUser = profile;
            UserSession.CurrentRoleBehavior = new RegularRoleBehavior();

            return true;
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
