//
//  FILE            : SocialAuthSettings.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2026-02-24
//  DESCRIPTION     :
//      Centralizes the OAuth settings and helper methods used by
//      LocalAuthService for Google/Facebook sign-in.
//
//      IMPORTANT:
//      This file is intentionally a *scaffold* that compiles.
//      To make real social sign-in work, you must:
//        1) Create OAuth apps in Google/Facebook developer consoles
//        2) Fill in the Client IDs and optional scopes
//        3) Configure the redirect URI scheme on each platform
//           (Android intent-filter / iOS URL schemes)
//
using CollectIQ.Enums;
using Microsoft.Maui.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CollectIQ.Services
{
    public static class SocialAuthSettings
    {
        // ============================================================
        //  REDIRECT URI
        // ============================================================
        // This must match your platform configuration.
        // Example scheme: "collectiq"
        // Callback URL:   "collectiq://auth"
        private const string CallbackUrl = "collectiq://auth";

        // ============================================================
        //  CLIENT IDS (FILL THESE IN)
        // ============================================================
        // Use the correct client id for the platform you are testing.
        // If you want, split these into Android/iOS/Windows values.
        private const string GoogleClientId = "";
        private const string FacebookClientId = "";

        // ============================================================
        //  SCOPES
        // ============================================================
        private static readonly string[] DefaultScopes = new[] { "email" };

        public static (string authUrl, string callbackUrl) GetAuthUrls(AuthProvider provider)
        {
            if (provider == AuthProvider.Google)
            {
                if (string.IsNullOrWhiteSpace(GoogleClientId))
                {
                    return (string.Empty, string.Empty);
                }

                string scope = Uri.EscapeDataString(string.Join(" ", DefaultScopes));
                string redirect = Uri.EscapeDataString(CallbackUrl);
                string clientId = Uri.EscapeDataString(GoogleClientId);

                // Google OAuth 2.0 Authorization Endpoint
                string authUrl =
                    $"https://accounts.google.com/o/oauth2/v2/auth" +
                    $"?client_id={clientId}" +
                    $"&redirect_uri={redirect}" +
                    $"&response_type=token" +
                    $"&scope={scope}";

                return (authUrl, CallbackUrl);
            }

            if (provider == AuthProvider.Facebook)
            {
                if (string.IsNullOrWhiteSpace(FacebookClientId))
                {
                    return (string.Empty, string.Empty);
                }

                string scope = Uri.EscapeDataString(string.Join(",", DefaultScopes));
                string redirect = Uri.EscapeDataString(CallbackUrl);
                string clientId = Uri.EscapeDataString(FacebookClientId);

                // Facebook OAuth Authorization Endpoint
                string authUrl =
                    $"https://www.facebook.com/v19.0/dialog/oauth" +
                    $"?client_id={clientId}" +
                    $"&redirect_uri={redirect}" +
                    $"&response_type=token" +
                    $"&scope={scope}";

                return (authUrl, CallbackUrl);
            }

            return (string.Empty, string.Empty);
        }

        public static string TryGetEmail(WebAuthenticatorResult result)
        {
            // Different providers return different fields; keep it defensive.
            // If this doesn't return an email, you likely need to call the
            // provider "userinfo" endpoint using the access token.

            if (result == null)
            {
                return string.Empty;
            }

            if (result.Properties.TryGetValue("email", out string? email) && !string.IsNullOrWhiteSpace(email))
            {
                return email;
            }

            if (result.Properties.TryGetValue("user_email", out string? userEmail) && !string.IsNullOrWhiteSpace(userEmail))
            {
                return userEmail;
            }

            return string.Empty;
        }
    }
}
