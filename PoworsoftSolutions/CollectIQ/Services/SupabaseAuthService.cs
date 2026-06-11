//
//  FILE            : SupabaseAuthService.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2026-06-09
//  DESCRIPTION     :
//      Implements social sign-in through Supabase Auth. The mobile app uses
//      MAUI WebAuthenticator to open the broker login page and receives only
//      the signed-in user's session. Provider secrets remain in Supabase, not
//      inside the app.
//

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using CollectIQ.Enums;
using CollectIQ.Interfaces;
using CollectIQ.Models.Auth;
using Microsoft.Maui.Authentication;
using Newtonsoft.Json.Linq;

namespace CollectIQ.Services
{
    /// <summary>
    /// Provides Google/Facebook social sign-in through Supabase Auth.
    /// </summary>
    public sealed class SupabaseAuthService : ISocialAuthService
    {
        private const string SupabaseAccessTokenKey = "supabase_access_token";
        private const string SupabaseRefreshTokenKey = "supabase_refresh_token";

        private readonly HttpClient httpClient;

        /// <summary>
        /// Initializes a new instance of the SupabaseAuthService class.
        /// </summary>
        public SupabaseAuthService()
        {
            this.httpClient = new HttpClient();
        }

        /// <summary>
        /// Starts a social sign-in flow for the requested provider through Supabase.
        /// </summary>
        /// <param name="provider">The provider to sign in with.</param>
        /// <returns>The normalized social user result.</returns>
        public async Task<SocialAuthUser> SignInAsync(AuthProvider provider)
        {
            if (provider != AuthProvider.Google && provider != AuthProvider.Facebook)
            {
                return SocialAuthUser.Failed(provider, "This provider is not supported yet.");
            }

            if (!SocialAuthSettings.IsConfigured())
            {
                return SocialAuthUser.Failed(
                    provider,
                    "Supabase Auth is not configured yet. Add the Supabase URL and anon key in SocialAuthSettings.cs.");
            }

            string providerName = SocialAuthSettings.GetSupabaseProviderName(provider);

            if (string.IsNullOrWhiteSpace(providerName))
            {
                return SocialAuthUser.Failed(provider, "Unable to resolve provider name.");
            }

            try
            {
                string codeVerifier = CreateCodeVerifier();
                string codeChallenge = CreateCodeChallenge(codeVerifier);
                string authorizeUrl = BuildAuthorizeUrl(providerName, codeChallenge);

                Debug.WriteLine($"[CollectIQ AUTH] Starting {provider} sign-in through Supabase.");
                Debug.WriteLine($"[CollectIQ AUTH] Callback: {SocialAuthSettings.CallbackUrl}");
                Debug.WriteLine($"[CollectIQ AUTH] Authorize URL: {authorizeUrl}");

                WebAuthenticatorResult result = await WebAuthenticator.Default.AuthenticateAsync(
                    new Uri(authorizeUrl),
                    new Uri(SocialAuthSettings.CallbackUrl));

                Dictionary<string, string> values = NormalizeWebAuthenticatorValues(result);

                if (values.TryGetValue("error", out string? error) && !string.IsNullOrWhiteSpace(error))
                {
                    string description = values.TryGetValue("error_description", out string? errorDescription)
                        ? errorDescription
                        : error;

                    Debug.WriteLine($"[CollectIQ AUTH] Provider error: {description}");
                    return SocialAuthUser.Failed(provider, description);
                }

                string accessToken = GetValue(values, "access_token");
                string refreshToken = GetValue(values, "refresh_token");

                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    string code = GetValue(values, "code");

                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        Debug.WriteLine("[CollectIQ AUTH] Supabase returned an auth code. Exchanging code for session.");
                        (accessToken, refreshToken) = await ExchangeCodeForSessionAsync(code, codeVerifier);
                    }
                }

                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    string returnedKeys = string.Join(", ", values.Keys.OrderBy(key => key));
                    Debug.WriteLine($"[CollectIQ AUTH] No access_token returned by Supabase callback. Returned keys: {returnedKeys}");
                    return SocialAuthUser.Failed(
                        provider,
                        $"The sign-in callback returned without an access token. Returned keys: {returnedKeys}. Check Supabase URL configuration and redirect URLs.");
                }

                await SecureStorage.SetAsync(SupabaseAccessTokenKey, accessToken);

                if (!string.IsNullOrWhiteSpace(refreshToken))
                {
                    await SecureStorage.SetAsync(SupabaseRefreshTokenKey, refreshToken);
                }

                SocialAuthUser user = await GetSupabaseUserAsync(provider, accessToken, refreshToken);

                if (!user.IsSuccess)
                {
                    return user;
                }

                Debug.WriteLine($"[CollectIQ AUTH] {provider} sign-in succeeded for {user.Email}.");
                return user;
            }
            catch (TaskCanceledException)
            {
                return SocialAuthUser.Failed(provider, "Sign-in was cancelled.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CollectIQ AUTH] Social sign-in failed: {ex}");
                return SocialAuthUser.Failed(provider, ex.Message);
            }
        }

        /// <summary>
        /// Builds the Supabase authorize URL for a provider.
        /// </summary>
        /// <param name="providerName">The Supabase provider name.</param>
        /// <returns>The authorize URL.</returns>
        private static string BuildAuthorizeUrl(string providerName, string codeChallenge)
        {
            string baseUrl = SocialAuthSettings.GetNormalizedSupabaseUrl();
            string redirectTo = Uri.EscapeDataString(SocialAuthSettings.CallbackUrl);
            string provider = Uri.EscapeDataString(providerName);
            string challenge = Uri.EscapeDataString(codeChallenge);

            return $"{baseUrl}/auth/v1/authorize?provider={provider}&redirect_to={redirectTo}&code_challenge={challenge}&code_challenge_method=s256";
        }

        /// <summary>
        /// Exchanges a Supabase PKCE auth code for a session.
        /// </summary>
        /// <param name="authCode">The auth code returned to the app callback.</param>
        /// <param name="codeVerifier">The PKCE code verifier created before opening the browser.</param>
        /// <returns>The Supabase access and refresh tokens.</returns>
        private async Task<(string AccessToken, string RefreshToken)> ExchangeCodeForSessionAsync(
            string authCode,
            string codeVerifier)
        {
            string baseUrl = SocialAuthSettings.GetNormalizedSupabaseUrl();
            string requestUrl = $"{baseUrl}/auth/v1/token?grant_type=pkce";

            JObject payload = new JObject
            {
                ["auth_code"] = authCode,
                ["code_verifier"] = codeVerifier
            };

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Headers.Add("apikey", SocialAuthSettings.SupabaseAnonKey);
            request.Content = new StringContent(payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await httpClient.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            Debug.WriteLine($"[CollectIQ AUTH] Supabase token exchange status: {(int)response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[CollectIQ AUTH] Supabase token exchange failed: {body}");
                return (string.Empty, string.Empty);
            }

            JObject json = JObject.Parse(body);
            string accessToken = json.Value<string>("access_token") ?? string.Empty;
            string refreshToken = json.Value<string>("refresh_token") ?? string.Empty;

            return (accessToken, refreshToken);
        }

        /// <summary>
        /// Creates a PKCE code verifier.
        /// </summary>
        /// <returns>The code verifier.</returns>
        private static string CreateCodeVerifier()
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(64);
            return Base64UrlEncode(bytes);
        }

        /// <summary>
        /// Creates a PKCE SHA-256 code challenge from the verifier.
        /// </summary>
        /// <param name="codeVerifier">The verifier.</param>
        /// <returns>The code challenge.</returns>
        private static string CreateCodeChallenge(string codeVerifier)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(codeVerifier);
            byte[] hash = SHA256.HashData(bytes);
            return Base64UrlEncode(hash);
        }

        /// <summary>
        /// Encodes bytes as Base64 URL without padding.
        /// </summary>
        /// <param name="bytes">The bytes to encode.</param>
        /// <returns>The encoded string.</returns>
        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        /// <summary>
        /// Loads the signed-in user's Supabase identity using the returned access token.
        /// </summary>
        /// <param name="provider">The CollectIQ provider.</param>
        /// <param name="accessToken">The Supabase access token.</param>
        /// <param name="refreshToken">The Supabase refresh token.</param>
        /// <returns>The normalized user result.</returns>
        private async Task<SocialAuthUser> GetSupabaseUserAsync(
            AuthProvider provider,
            string accessToken,
            string refreshToken)
        {
            string baseUrl = SocialAuthSettings.GetNormalizedSupabaseUrl();
            string requestUrl = $"{baseUrl}/auth/v1/user";

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("apikey", SocialAuthSettings.SupabaseAnonKey);

            using HttpResponseMessage response = await httpClient.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[CollectIQ AUTH] Supabase user lookup failed: {(int)response.StatusCode} {body}");
                return SocialAuthUser.Failed(provider, $"Unable to load signed-in user. Status: {(int)response.StatusCode}");
            }

            JObject json = JObject.Parse(body);

            string providerUserId = json.Value<string>("id") ?? string.Empty;
            string email = json.Value<string>("email") ?? string.Empty;
            JObject? metadata = json["user_metadata"] as JObject;

            string displayName = metadata?.Value<string>("full_name") ??
                                 metadata?.Value<string>("name") ??
                                 email;

            string avatarUrl = metadata?.Value<string>("avatar_url") ??
                               metadata?.Value<string>("picture") ??
                               string.Empty;

            if (string.IsNullOrWhiteSpace(providerUserId) || string.IsNullOrWhiteSpace(email))
            {
                return SocialAuthUser.Failed(provider, "The provider did not return a usable user identity.");
            }

            return new SocialAuthUser
            {
                IsSuccess = true,
                Provider = provider,
                ProviderUserId = providerUserId,
                Email = email,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? email : displayName,
                AvatarUrl = avatarUrl,
                AccessToken = accessToken,
                RefreshToken = refreshToken
            };
        }

        /// <summary>
        /// Normalizes values returned by WebAuthenticator.
        /// </summary>
        /// <param name="result">The WebAuthenticator result.</param>
        /// <returns>A case-insensitive dictionary of returned values.</returns>
        private static Dictionary<string, string> NormalizeWebAuthenticatorValues(WebAuthenticatorResult result)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, string> item in result.Properties)
            {
                values[item.Key] = item.Value;
                Debug.WriteLine($"[CollectIQ AUTH] Callback value: {item.Key}");
            }

            return values;
        }

        /// <summary>
        /// Gets a value from a dictionary by key.
        /// </summary>
        /// <param name="values">The values dictionary.</param>
        /// <param name="key">The key to read.</param>
        /// <returns>The value, or an empty string.</returns>
        private static string GetValue(Dictionary<string, string> values, string key)
        {
            return values.TryGetValue(key, out string? value) ? value : string.Empty;
        }
    }

    /// <summary>
    /// Fallback social authentication service used when old code directly creates LocalAuthService.
    /// </summary>
    public sealed class NoOpSocialAuthService : ISocialAuthService
    {
        /// <summary>
        /// Returns a controlled failure for social login when the real broker is not available.
        /// </summary>
        /// <param name="provider">The provider requested.</param>
        /// <returns>A failed social authentication result.</returns>
        public Task<SocialAuthUser> SignInAsync(AuthProvider provider)
        {
            return Task.FromResult(SocialAuthUser.Failed(provider, "Social authentication service is not available."));
        }
    }
}
