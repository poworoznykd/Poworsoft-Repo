using System.Threading.Tasks;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Provides authentication operations such as login, registration, magic link, and sign-out.
    /// </summary>
    public interface IAuthService
    {
        /// <summary>
        /// Registers a new account using email and password.
        /// </summary>
        /// <param name="email">The email address of the user.</param>
        /// <param name="password">The desired account password.</param>
        /// <returns>True if the registration succeeded; otherwise, false.</returns>
        Task<bool> RegisterAsync(string email, string password);

        /// <summary>
        /// Logs in using email and password.
        /// </summary>
        /// <param name="email">The email address of the user.</param>
        /// <param name="password">The account password.</param>
        /// <returns>True if the login succeeded; otherwise, false.</returns>
        Task<bool> LoginWithPasswordAsync(string email, string password);

        /// <summary>
        /// Starts a passwordless login by emailing a magic link.
        /// </summary>
        /// <param name="email">The email to send the magic link to.</param>
        /// <returns>True if the request was sent; otherwise, false.</returns>
        Task<bool> SendMagicLinkAsync(string email);

        /// <summary>
        /// Completes magic-link login after the app is opened from a deep link.
        /// </summary>
        /// <param name="token">The one-time token from the deep link.</param>
        /// <returns>True if the link was valid and the user is now signed in; otherwise, false.</returns>
        Task<bool> CompleteMagicLinkAsync(string token);

        /// <summary>
        /// Signs in with Google (device account picker or web flow).
        /// </summary>
        /// <returns>True if the login succeeded; otherwise, false.</returns>
        Task<bool> LoginWithGoogleAsync();

        /// <summary>
        /// Signs the current user out and clears secure credentials.
        /// </summary>
        /// <returns>True if sign-out completed successfully; otherwise, false.</returns>
        Task<bool> SignOutAsync();

        /// <summary>
        /// Gets whether a user session is currently active.
        /// </summary>
        /// <returns>True if a user is signed in; otherwise, false.</returns>
        Task<bool> IsSignedInAsync();

        /// <summary>
        /// Links any locally created data (guest mode) to the newly signed-in account.
        /// </summary>
        /// <param name="localAnonymousId">The locally generated guest identifier.</param>
        /// <returns>True if the link succeeded; otherwise, false.</returns>
        Task<bool> LinkGuestDataAsync(string localAnonymousId);
    }
}
