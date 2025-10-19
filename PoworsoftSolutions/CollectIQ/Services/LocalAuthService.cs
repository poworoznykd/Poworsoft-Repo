//
//  FILE            : LocalAuthService.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-19
//  DESCRIPTION     :
//      Implements local authentication logic for registration,
//      login, and session management.
//
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CollectIQ.Interfaces;
using CollectIQ.Models;
using Microsoft.Maui.Storage;

namespace CollectIQ.Services
{
    /// <summary>
    /// Handles local user authentication using SQLite storage.
    /// </summary>
    public sealed class LocalAuthService : IAuthService
    {
        private readonly IDatabase _db;
        private const string SessionKey = "current_user_email";

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

            await SecureStorage.SetAsync(SessionKey, email);
            return true;
        }

        public async Task<bool> SignOutAsync()
        {
            SecureStorage.Remove(SessionKey);
            await Task.Delay(30);
            return true;
        }

        public async Task<bool> IsSignedInAsync()
        {
            var email = await SecureStorage.GetAsync(SessionKey);
            return !string.IsNullOrEmpty(email);
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
