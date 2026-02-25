//
//  FILE            : IAuthService.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-19
//  DESCRIPTION     :
//      Defines the contract for all authentication service implementations
//      within the CollectIQ mobile application. Supports registration,
//      login, sign-out, and user session verification.
//
using System.Threading.Tasks;
using CollectIQ.Enums;
using CollectIQ.Models;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Defines authentication operations used by UI components.
    /// </summary>
    public interface IAuthService
    {
        Task<bool> RegisterAsync(string email, string password);
        Task<bool> LoginAsync(string email, string password);
        Task<bool> SignOutAsync();
        Task<bool> IsSignedInAsync();
        Task<string?> GetCurrentUserEmailAsync();

        // ============================================================
        //  OPTIONAL AUTH FLOWS
        // ============================================================

        /// <summary>
        /// Signs the user in as a guest (no password).
        /// </summary>
        Task<bool> SignInGuestAsync();

        /// <summary>
        /// Attempts to sign in using an OAuth provider.
        /// NOTE: Requires platform-specific configuration.
        /// </summary>
        Task<bool> SignInWithProviderAsync(AuthProvider provider);

        /// <summary>
        /// Gets the current signed-in user profile (if any).
        /// </summary>
        Task<UserProfile?> GetCurrentUserProfileAsync();
    }
}
