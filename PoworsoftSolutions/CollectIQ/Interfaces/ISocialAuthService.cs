//
//  FILE            : ISocialAuthService.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2026-06-09
//  DESCRIPTION     :
//      Defines the social authentication broker contract used by CollectIQ.
//      The mobile app should not manually implement provider-specific OAuth
//      details for Google/Facebook. Instead, it should authenticate through an
//      auth broker such as Supabase and cache only the signed-in user's data.
//

using CollectIQ.Enums;
using CollectIQ.Models.Auth;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Defines operations for signing in through external social providers.
    /// </summary>
    public interface ISocialAuthService
    {
        /// <summary>
        /// Starts a social sign-in flow for the requested provider.
        /// </summary>
        /// <param name="provider">The social provider to use.</param>
        /// <returns>The normalized signed-in social user, or a failed result.</returns>
        Task<SocialAuthUser> SignInAsync(AuthProvider provider);
    }
}
