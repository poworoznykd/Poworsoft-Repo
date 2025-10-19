//
//  FILE            : IAuthService.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-19
//  DESCRIPTION     :
//      Defines the contract for local authentication services.
//      This interface supports user registration, login, logout,
//      and session management using local SQLite and SecureStorage.
//
using System.Threading.Tasks;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Provides authentication and session management methods
    /// for local user accounts.
    /// </summary>
    public interface IAuthService
    {
        /// <summary>
        /// Registers a new account using email and password.
        /// </summary>
        /// <param name="email">The email for the new account.</param>
        /// <param name="password">The password for the new account.</param>
        /// <returns>True if registration succeeds; otherwise, false.</returns>
        Task<bool> RegisterAsync(string email, string password);

        /// <summary>
        /// Logs in with email and password.
        /// </summary>
        /// <param name="email">User email.</param>
        /// <param name="password">User password.</param>
        /// <returns>True if login succeeds; otherwise, false.</returns>
        Task<bool> LoginWithPasswordAsync(string email, string password);

        /// <summary>
        /// Signs out the current user.
        /// </summary>
        Task<bool> SignOutAsync();

        /// <summary>
        /// Determines if a user is currently signed in.
        /// </summary>
        Task<bool> IsSignedInAsync();

        /// <summary>
        /// Returns the email of the signed-in user (if any).
        /// </summary>
        Task<string?> GetCurrentUserEmailAsync();
    }
}
