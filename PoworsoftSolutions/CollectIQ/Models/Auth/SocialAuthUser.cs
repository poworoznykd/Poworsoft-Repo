//
//  FILE            : SocialAuthUser.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2026-06-09
//  DESCRIPTION     :
//      Represents the normalized user identity returned by an external
//      authentication broker such as Supabase Auth. This keeps Google,
//      Facebook, and future provider details out of the local account model.
//

using CollectIQ.Enums;

namespace CollectIQ.Models.Auth
{
    /// <summary>
    /// Represents a user identity returned from a social authentication provider.
    /// </summary>
    public sealed class SocialAuthUser
    {
        /// <summary>
        /// Gets or sets whether the social sign-in completed successfully.
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// Gets or sets the provider used to authenticate the user.
        /// </summary>
        public AuthProvider Provider { get; set; } = AuthProvider.Unknown;

        /// <summary>
        /// Gets or sets the unique user ID returned by the auth broker/provider.
        /// </summary>
        public string ProviderUserId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the user's email address returned by the auth provider.
        /// </summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the user's display name returned by the auth provider.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the user's avatar URL returned by the auth provider.
        /// </summary>
        public string AvatarUrl { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the access token returned by the broker.
        /// </summary>
        public string AccessToken { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the refresh token returned by the broker.
        /// </summary>
        public string RefreshToken { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets an error message when social authentication fails.
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// Creates a failed social authentication result.
        /// </summary>
        /// <param name="provider">The provider that failed.</param>
        /// <param name="message">The failure message.</param>
        /// <returns>A failed social auth user result.</returns>
        public static SocialAuthUser Failed(AuthProvider provider, string message)
        {
            return new SocialAuthUser
            {
                Provider = provider,
                IsSuccess = false,
                ErrorMessage = message
            };
        }
    }
}
