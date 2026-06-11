//
//  FILE            : SocialAuthSettings.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2026-06-09
//  DESCRIPTION     :
//      Centralizes social authentication settings for the CollectIQ mobile app.
//      This version uses an authentication broker approach. Google/Facebook
//      provider secrets belong in Supabase or a future CollectIQ API, not in
//      the mobile app.
//

using CollectIQ.Enums;

namespace CollectIQ.Services
{
    /// <summary>
    /// Stores social authentication configuration for CollectIQ.
    /// </summary>
    public static class SocialAuthSettings
    {
        #region Supabase Broker Settings

        public const string SupabaseUrl = "https://ojijtosqpgcnaflgqdye.supabase.co";

        public const string SupabaseAnonKey = "sb_publishable_vuJiCGm8OGIrJafaIA9QKA_ACwbtPl8";

        public const string CallbackUrl = "collectiq://auth";

        #endregion

        #region Supabase Endpoint Helpers

        /// <summary>
        /// Gets the normalized Supabase base URL without trailing slashes.
        /// </summary>
        /// <returns>The normalized Supabase project URL.</returns>
        public static string GetNormalizedSupabaseUrl()
        {
            return SupabaseUrl.Trim().TrimEnd('/');
        }

        /// <summary>
        /// Gets the Supabase OAuth authorize endpoint.
        /// </summary>
        /// <returns>The Supabase OAuth authorize endpoint.</returns>
        public static string GetAuthorizeEndpoint()
        {
            return $"{GetNormalizedSupabaseUrl()}/auth/v1/authorize";
        }

        /// <summary>
        /// Gets the Supabase token endpoint.
        /// </summary>
        /// <returns>The Supabase token endpoint.</returns>
        public static string GetTokenEndpoint()
        {
            return $"{GetNormalizedSupabaseUrl()}/auth/v1/token";
        }

        /// <summary>
        /// Gets the Supabase user endpoint.
        /// </summary>
        /// <returns>The Supabase user endpoint.</returns>
        public static string GetUserEndpoint()
        {
            return $"{GetNormalizedSupabaseUrl()}/auth/v1/user";
        }

        #endregion

        #region Provider Names

        /// <summary>
        /// Gets the Supabase provider name for a CollectIQ auth provider.
        /// </summary>
        /// <param name="provider">The CollectIQ auth provider.</param>
        /// <returns>The Supabase provider name.</returns>
        public static string GetSupabaseProviderName(AuthProvider provider)
        {
            return provider switch
            {
                AuthProvider.Google => "google",
                AuthProvider.Facebook => "facebook",
                _ => string.Empty
            };
        }

        #endregion

        #region Validation

        /// <summary>
        /// Gets whether Supabase broker settings are configured enough to start social login.
        /// </summary>
        /// <returns>True when Supabase settings are configured; otherwise false.</returns>
        public static bool IsConfigured()
        {
            return !string.IsNullOrWhiteSpace(SupabaseUrl) &&
                   !string.IsNullOrWhiteSpace(SupabaseAnonKey) &&
                   !string.IsNullOrWhiteSpace(CallbackUrl) &&
                   SupabaseUrl.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase) &&
                   SupabaseUrl.Contains(".supabase.co", System.StringComparison.OrdinalIgnoreCase) &&
                   !SupabaseUrl.Contains("/auth/v1/callback", System.StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}