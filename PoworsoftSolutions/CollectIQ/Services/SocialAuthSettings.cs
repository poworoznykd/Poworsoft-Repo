//
//  FILE            : SocialAuthSettings.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2026-03-04
//  UPDATED         : 2026-06-08
//  DESCRIPTION     :
//      Centralizes OAuth settings and URL builders for Google and Facebook
//      sign-in. These settings support MAUI WebAuthenticator and the local
//      CollectIQ account/profile/cache model.
//
//      SECURITY NOTE:
//      These values are client identifiers only. Do not place OAuth client
//      secrets in the mobile application. Production social login should move
//      token exchange and account linking behind the CollectIQ API.
//

using CollectIQ.Enums;
using Microsoft.Maui.Authentication;

namespace CollectIQ.Services
{
    /// <summary>
    /// Provides social authentication settings and helper methods.
    /// </summary>
    public static class SocialAuthSettings
    {
        #region Callback Settings

        /// <summary>
        /// OAuth callback URL registered with the MAUI Android callback activity.
        /// </summary>
        public const string CallbackUrl = "collectiq://auth";

        #endregion

        #region Client IDs

        /// <summary>
        /// Google OAuth client ID used for development sign-in.
        /// </summary>
        public const string GoogleClientId = "1095978040175-jc2ma0aj0hubrtq8kukasuI2cknkr88m.apps.googleusercontent.com";

        /// <summary>
        /// Facebook app/client ID. Fill this in from Meta Developer Console when ready.
        /// </summary>
        public const string FacebookClientId = "";

        #endregion

        #region Provider Endpoints

        /// <summary>
        /// Google authorization endpoint.
        /// </summary>
        public const string GoogleAuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";

        /// <summary>
        /// Google token endpoint.
        /// </summary>
        public const string GoogleTokenEndpoint = "https://oauth2.googleapis.com/token";

        /// <summary>
        /// Google OpenID userinfo endpoint.
        /// </summary>
        public const string GoogleUserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";

        /// <summary>
        /// Facebook OAuth authorization endpoint.
        /// </summary>
        public const string FacebookAuthorizeEndpoint = "https://www.facebook.com/dialog/oauth";

        /// <summary>
        /// Facebook user profile endpoint.
        /// </summary>
        public const string FacebookUserInfoEndpoint = "https://graph.facebook.com/me";

        private const string GoogleScopes = "openid email profile";
        private const string FacebookScopes = "email,public_profile";

        #endregion

        #region URL Builders

        /// <summary>
        /// Builds the Google authorization URL using authorization code plus PKCE.
        /// </summary>
        /// <param name="codeChallenge">The PKCE code challenge.</param>
        /// <param name="state">The anti-forgery state value.</param>
        /// <returns>The Google authorization URL.</returns>
        public static string BuildGoogleAuthorizeUrl(string codeChallenge, string state)
        {
            string clientId = Uri.EscapeDataString(GoogleClientId);
            string redirect = Uri.EscapeDataString(CallbackUrl);
            string scope = Uri.EscapeDataString(GoogleScopes);
            string challenge = Uri.EscapeDataString(codeChallenge);
            string safeState = Uri.EscapeDataString(state);

            return
                $"{GoogleAuthorizeEndpoint}" +
                $"?client_id={clientId}" +
                $"&redirect_uri={redirect}" +
                $"&response_type=code" +
                $"&scope={scope}" +
                $"&code_challenge={challenge}" +
                $"&code_challenge_method=S256" +
                $"&state={safeState}" +
                $"&prompt=select_account";
        }

        /// <summary>
        /// Builds the Facebook authorization URL.
        /// </summary>
        /// <param name="state">The anti-forgery state value.</param>
        /// <returns>The Facebook authorization URL.</returns>
        public static string BuildFacebookAuthorizeUrl(string state)
        {
            string clientId = Uri.EscapeDataString(FacebookClientId);
            string redirect = Uri.EscapeDataString(CallbackUrl);
            string scope = Uri.EscapeDataString(FacebookScopes);
            string safeState = Uri.EscapeDataString(state);

            return
                $"{FacebookAuthorizeEndpoint}" +
                $"?client_id={clientId}" +
                $"&redirect_uri={redirect}" +
                $"&response_type=token" +
                $"&scope={scope}" +
                $"&state={safeState}";
        }

        #endregion

        #region Result Parsers

        /// <summary>
        /// Gets an authorization code from a WebAuthenticator result.
        /// </summary>
        /// <param name="result">The WebAuthenticator result.</param>
        /// <returns>The authorization code, or an empty string.</returns>
        public static string TryGetAuthCode(WebAuthenticatorResult result)
        {
            return TryGetProperty(result, "code");
        }

        /// <summary>
        /// Gets an access token from a WebAuthenticator result.
        /// </summary>
        /// <param name="result">The WebAuthenticator result.</param>
        /// <returns>The access token, or an empty string.</returns>
        public static string TryGetAccessToken(WebAuthenticatorResult result)
        {
            return TryGetProperty(result, "access_token");
        }

        /// <summary>
        /// Gets the OAuth state from a WebAuthenticator result.
        /// </summary>
        /// <param name="result">The WebAuthenticator result.</param>
        /// <returns>The state value, or an empty string.</returns>
        public static string TryGetState(WebAuthenticatorResult result)
        {
            return TryGetProperty(result, "state");
        }

        /// <summary>
        /// Gets a direct email value from a WebAuthenticator result if one exists.
        /// </summary>
        /// <param name="result">The WebAuthenticator result.</param>
        /// <returns>The email, or an empty string.</returns>
        public static string TryGetEmail(WebAuthenticatorResult result)
        {
            string email = TryGetProperty(result, "email");

            if (!string.IsNullOrWhiteSpace(email))
            {
                return email;
            }

            return TryGetProperty(result, "preferred_username");
        }

        /// <summary>
        /// Gets a named property from a WebAuthenticator result.
        /// </summary>
        /// <param name="result">The WebAuthenticator result.</param>
        /// <param name="key">The property key.</param>
        /// <returns>The property value, or an empty string.</returns>
        private static string TryGetProperty(WebAuthenticatorResult result, string key)
        {
            if (result == null || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            if (result.Properties.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return string.Empty;
        }

        #endregion
    }
}
