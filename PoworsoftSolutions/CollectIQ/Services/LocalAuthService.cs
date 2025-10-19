//
//  FILE            : LocalAuthService.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-21
//  DESCRIPTION     :
//      Provides a lightweight local authentication service using SQLite.
//      Handles registration, login, sign-out, and session validation.
//      Follows SET coding standards.
//
using System.Security.Cryptography;
using System.Text;
using CollectIQ.Interfaces;
using CollectIQ.Models;

namespace CollectIQ.Services
{
    /// <summary>
    /// Implements local authentication without any external provider.
    /// </summary>
    public sealed class LocalAuthService : IAuthService
    {
        private readonly IDatabase _database;
        private UserProfile? _currentUser;

        /// <summary>
        /// Initializes a new instance of <see cref="LocalAuthService"/>.
        /// </summary>
        /// <param name="database">Injected database service.</param>
        public LocalAuthService(IDatabase database)
        {
            _database = database;
        }

        /// <summary>
        /// Registers a new user and saves their password hash locally.
        /// </summary>
        public async Task<bool> RegisterAsync(string email, string password)
        {
            try
            {
                await _database.InitializeAsync();

                // Prevent duplicates.
                var existing = await GetUserByEmailAsync(email);
                if (existing != null)
                    return false;

                string passwordHash = ComputeSha256(password);

                var profile = new UserProfile
                {
                    Id = Guid.NewGuid().ToString(),
                    Email = email,
                    DisplayName = email.Split('@')[0],
                    ProviderUserId = null,
                    GuestId = null
                };

                await _database.UpsertUserProfileAsync(profile);
                await StorePasswordHashAsync(profile.Id, passwordHash);

                _currentUser = profile;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Logs in a user if their email and password hash match.
        /// </summary>
        public async Task<bool> LoginWithPasswordAsync(string email, string password)
        {
            try
            {
                await _database.InitializeAsync();

                var profile = await GetUserByEmailAsync(email);
                if (profile == null)
                    return false;

                string? storedHash = await GetPasswordHashAsync(profile.Id);
                if (storedHash == null)
                    return false;

                string passwordHash = ComputeSha256(password);
                if (storedHash == passwordHash)
                {
                    _currentUser = profile;
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks whether a user is currently signed in.
        /// </summary>
        public Task<bool> IsSignedInAsync()
        {
            return Task.FromResult(_currentUser != null);
        }

        /// <summary>
        /// Signs out the current user.
        /// </summary>
        public Task<bool> SignOutAsync()
        {
            _currentUser = null;
            return Task.FromResult(true);
        }

        /// <summary>
        /// Returns the email address of the currently signed-in user.
        /// </summary>
        public Task<string?> GetCurrentUserEmailAsync()
        {
            return Task.FromResult(_currentUser?.Email);
        }

        #region Private Helpers

        /// <summary>
        /// Computes a SHA-256 hash for the given password.
        /// </summary>
        private static string ComputeSha256(string input)
        {
            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }

        /// <summary>
        /// Retrieves a user profile by email.
        /// </summary>
        private async Task<UserProfile?> GetUserByEmailAsync(string email)
        {
            var current = await _database.GetUserProfileAsync();
            if (current != null &&
                string.Equals(current.Email, email, StringComparison.OrdinalIgnoreCase))
                return current;

            return null;
        }

        /// <summary>
        /// Stores a password hash (by user ID) in a local text file.
        /// </summary>
        private async Task StorePasswordHashAsync(string userId, string passwordHash)
        {
            var path = Path.Combine(FileSystem.AppDataDirectory, "user_passwords.txt");
            var lines = new List<string>();

            if (File.Exists(path))
                lines.AddRange(await File.ReadAllLinesAsync(path));

            // Replace existing line if present.
            lines.RemoveAll(line => line.StartsWith(userId + ":", StringComparison.OrdinalIgnoreCase));
            lines.Add($"{userId}:{passwordHash}");

            await File.WriteAllLinesAsync(path, lines);
        }

        /// <summary>
        /// Retrieves the stored password hash for the given user ID.
        /// </summary>
        private async Task<string?> GetPasswordHashAsync(string userId)
        {
            var path = Path.Combine(FileSystem.AppDataDirectory, "user_passwords.txt");
            if (!File.Exists(path))
                return null;

            var lines = await File.ReadAllLinesAsync(path);
            var match = lines.FirstOrDefault(line => line.StartsWith(userId + ":", StringComparison.OrdinalIgnoreCase));
            return match?.Split(':')[1];
        }

        #endregion
    }
}
