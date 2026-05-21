//
//  FILE            : SocialAuthSettings.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2026-02-24
//  DESCRIPTION     :
//      Centralizes the OAuth settings and helper methods used by
//      LocalAuthService for Google/Facebook sign-in.
//
//      NOTE:
//      Google is implemented using Authorization Code + PKCE.
//      Facebook will be implemented later (Step 5).
//
using CollectIQ.Enums;
using Microsoft.Maui.Authentication;
using System;

namespace CollectIQ.Services
{
    public static class SocialAuthSettings
    {
        // ============================================================
        //  REDIRECT / CALLBACK
        // ============================================================
        // Must match platform configuration (Android intent-filter / iOS URL scheme)
        // Example: collectiq://auth
        public const string CallbackUrl = "collectiq://auth";

        // ============================================================
        //  CLIENT IDS (FILL THESE IN)
        // ============================================================
        // IMPORTANT:
        // - For Google, use your "OAuth Client ID" for the platform you are testing.
        // - For development, you can start with Android first.
        // - We are NOT changing interfaces; you just paste IDs here for now.
        public const string GoogleClientId = "1067559897702-0rbah2qr5n4uq1rcs2qbs60d1gpprrij.apps.googleusercontent.com";
        public const string FacebookClientId = "";

        // ============================================================
        //  ENDPOINTS
        // ============================================================
        public const string GoogleAuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        public const string GoogleTokenEndpoint = "https://oauth2.googleapis.com/token";
        public const string GoogleUserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";

        // ============================================================
        //  SCOPES
        // ============================================================
        // Use OpenID scopes so we can reliably get email/profile
        private const string GoogleScopes = "openid email profile";

        // ============================================================
        //  URL BUILDERS
        // ============================================================
        public static (string authUrl, string callbackUrl) GetAuthUrls(AuthProvider provider)
        {
            // For Step 4, we will only make Google work.
            // Facebook remains scaffolded and will be implemented in Step 5.

            if (provider == AuthProvider.Google)
            {
                // LocalAuthService will build the final URL because it must inject PKCE parameters.
                // We still validate required settings here.
                if (string.IsNullOrWhiteSpace(GoogleClientId))
                {
                    return (string.Empty, string.Empty);
                }

                // Placeholder; LocalAuthService will create the final authorize URL with PKCE.
                return ("__GOOGLE_PKCE__", CallbackUrl);
            }

            if (provider == AuthProvider.Facebook)
            {
                // Not implemented until Step 5
                return (string.Empty, string.Empty);
            }

            return (string.Empty, string.Empty);
        }

        public static string BuildGoogleAuthorizeUrl(string codeChallenge, string state)
        {
            string clientId = Uri.EscapeDataString(GoogleClientId);
            string redirect = Uri.EscapeDataString(CallbackUrl);
            string scope = Uri.EscapeDataString(GoogleScopes);
            string challenge = Uri.EscapeDataString(codeChallenge);
            string safeState = Uri.EscapeDataString(state);

            // Authorization Code + PKCE
            return
                $"{GoogleAuthorizeEndpoint}" +
                $"?client_id={clientId}" +
                $"&redirect_uri={redirect}" +
                $"&response_type=code" +
                $"&scope={scope}" +
                $"&code_challenge={challenge}" +
                $"&code_challenge_method=S256" +
                $"&state={safeState}";
        }

        // ============================================================
        //  RESULT PARSERS
        // ============================================================
        public static string TryGetAuthCode(WebAuthenticatorResult result)
        {
            if (result == null)
            {
                return string.Empty;
            }

            // With response_type=code, WebAuthenticatorResult usually contains "code"
            if (result.Properties.TryGetValue("code", out string? code) && !string.IsNullOrWhiteSpace(code))
            {
                return code;
            }

            return string.Empty;
        }

        public static string TryGetState(WebAuthenticatorResult result)
        {
            if (result == null)
            {
                return string.Empty;
            }

            if (result.Properties.TryGetValue("state", out string? state) && !string.IsNullOrWhiteSpace(state))
            {
                return state;
            }

            return string.Empty;
        }

        public static string TryGetEmail(WebAuthenticatorResult result)
        {
            if (result == null)
            {
                return string.Empty;
            }

            // Some providers may include email directly (rare).
            if (result.Properties.TryGetValue("email", out string? email) && !string.IsNullOrWhiteSpace(email))
            {
                return email;
            }

            // Some may use different keys (still not reliable for Google/Facebook).
            if (result.Properties.TryGetValue("preferred_username", out string? user) && !string.IsNullOrWhiteSpace(user))
            {
                return user;
            }

            return string.Empty;
        }
    }
}