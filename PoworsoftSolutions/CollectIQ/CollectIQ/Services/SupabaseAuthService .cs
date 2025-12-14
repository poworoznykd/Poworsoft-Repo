using System.Threading.Tasks;
using CollectIQ.Interfaces;
using CollectIQ.Models;
using Microsoft.Maui.Storage;

namespace CollectIQ.Services
{
    /// <summary>
    /// Supabase-backed implementation of IAuthService (stubbed for UI/dev).
    /// </summary>
    public sealed class SupabaseAuthService //: IAuthService
    {
        private readonly IDatabase _db;
        private const string TokenKey = "auth_token";
        private const string GuestIdKey = "guest_id";

        /// <summary>
        /// Initializes a new instance of the <see cref="SupabaseAuthService"/> class.
        /// </summary>
        public SupabaseAuthService(IDatabase db)
        {
            _db = db;
        }

        /// <summary>
        /// Registers a new account using email and password.
        /// </summary>
        public async Task<bool> RegisterAsync(string email, string password)
        {
            // TODO: Call Supabase signup; capture providerUserId from response
            await SecureStorage.SetAsync(TokenKey, "demo_registered_token");

            var profile = new UserProfile
            {
                Email = email,
                ProviderUserId = "demo-provider-id"
            };
            await _db.UpsertUserProfileAsync(profile);
            return true;
        }

        /// <summary>
        /// Logs in using email and password.
        /// </summary>
        public async Task<bool> LoginWithPasswordAsync(string email, string password)
        {
            // TODO: Supabase password login; capture providerUserId
            await SecureStorage.SetAsync(TokenKey, "demo_password_token");

            var profile = new UserProfile
            {
                Email = email,
                ProviderUserId = "demo-provider-id"
            };
            await _db.UpsertUserProfileAsync(profile);
            return true;
        }

        /// <summary>
        /// Starts a passwordless login by emailing a magic link.
        /// </summary>
        public async Task<bool> SendMagicLinkAsync(string email)
        {
            // TODO: Supabase magic link send
            await Task.Delay(100);
            return true;
        }

        /// <summary>
        /// Completes magic-link login after the app is opened from a deep link.
        /// </summary>
        public async Task<bool> CompleteMagicLinkAsync(string token)
        {
            // TODO: Validate token and get email/providerUserId
            await SecureStorage.SetAsync(TokenKey, "demo_magic_token");

            var profile = new UserProfile
            {
                Email = "magic@example.com",
                ProviderUserId = "demo-provider-id"
            };
            await _db.UpsertUserProfileAsync(profile);
            return true;
        }

        /// <summary>
        /// Signs in with Google (device account picker or web flow).
        /// </summary>
        public async Task<bool> LoginWithGoogleAsync()
        {
            // TODO: Google OAuth via Supabase; get email/providerUserId
            await SecureStorage.SetAsync(TokenKey, "demo_google_token");

            var profile = new UserProfile
            {
                Email = "googleuser@example.com",
                ProviderUserId = "demo-google-id"
            };
            await _db.UpsertUserProfileAsync(profile);
            return true;
        }

        /// <summary>
        /// Signs the current user out and clears secure credentials.
        /// </summary>
        public async Task<bool> SignOutAsync()
        {
            SecureStorage.Remove(TokenKey);
            // Optionally clear local profile (or keep last signed-in user).
            // var profile = await _db.GetUserProfileAsync(); // decide your UX
            await Task.Delay(50);
            return true;
        }

        /// <summary>
        /// Gets whether a user session is currently active.
        /// </summary>
        public async Task<bool> IsSignedInAsync()
        {
            var token = await SecureStorage.GetAsync(TokenKey);
            return !string.IsNullOrEmpty(token);
        }

        /// <summary>
        /// Links any locally created data (guest mode) to the newly signed-in account.
        /// </summary>
        public async Task<bool> LinkGuestDataAsync(string localAnonymousId)
        {
            // TODO: Upsert local rows to provider user id if needed.
            await Task.Delay(50);
            SecureStorage.Remove(GuestIdKey);
            return true;
        }
    }
}
