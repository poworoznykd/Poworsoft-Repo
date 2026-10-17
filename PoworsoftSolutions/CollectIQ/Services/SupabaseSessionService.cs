using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CollectIQ.Interfaces;
using Newtonsoft.Json.Linq;

namespace CollectIQ.Services
{
    /// <summary>
    /// Reads, validates and refreshes the Supabase session created by the
    /// existing social-authentication flow.
    /// </summary>
    public sealed class SupabaseSessionService : ISupabaseSessionService
    {
        private const string AccessTokenKey = "supabase_access_token";
        private const string RefreshTokenKey = "supabase_refresh_token";

        private readonly HttpClient httpClient = new HttpClient();
        private readonly SemaphoreSlim refreshLock = new SemaphoreSlim(1, 1);

        public Task<string?> GetAccessTokenAsync()
        {
            return SecureStorage.GetAsync(AccessTokenKey);
        }

        public async Task<string?> GetValidAccessTokenAsync()
        {
            string? accessToken = await GetAccessTokenAsync();

            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                SessionValidationResult validation =
                    await ValidateAccessTokenAsync(accessToken);

                if (validation.IsValid)
                {
                    return accessToken;
                }

                // Supabase Auth can report an expired/invalid user session as
                // either 401 or 403. Both mean "refresh once" for this endpoint.
                if (!validation.ShouldRefresh)
                {
                    return null;
                }
            }

            return await RefreshAccessTokenAsync();
        }

        public async Task<string?> GetAuthenticatedUserIdAsync()
        {
            string? accessToken = await GetValidAccessTokenAsync();

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return null;
            }

            SessionValidationResult validation =
                await ValidateAccessTokenAsync(accessToken);

            if (validation.IsValid)
            {
                return validation.UserId;
            }

            // A rare race is possible when the token expires between validation
            // calls. Refresh one final time and validate the new token.
            if (validation.ShouldRefresh)
            {
                string? refreshedToken = await RefreshAccessTokenAsync();

                if (!string.IsNullOrWhiteSpace(refreshedToken))
                {
                    SessionValidationResult retry =
                        await ValidateAccessTokenAsync(refreshedToken);

                    if (retry.IsValid)
                    {
                        return retry.UserId;
                    }
                }
            }

            return null;
        }

        public async Task<string?> RefreshAccessTokenAsync()
        {
            await refreshLock.WaitAsync();

            try
            {
                string? refreshToken =
                    await SecureStorage.GetAsync(RefreshTokenKey);

                if (string.IsNullOrWhiteSpace(refreshToken))
                {
                    Debug.WriteLine("[CollectIQ Auth] No Supabase refresh token is stored.");
                    return null;
                }

                string requestUrl =
                    $"{SocialAuthSettings.GetNormalizedSupabaseUrl()}/auth/v1/token?grant_type=refresh_token";

                JObject payload = new JObject
                {
                    ["refresh_token"] = refreshToken
                };

                using HttpRequestMessage request =
                    new HttpRequestMessage(HttpMethod.Post, requestUrl);

                request.Headers.Add(
                    "apikey",
                    SocialAuthSettings.SupabaseAnonKey);

                request.Headers.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));

                request.Content = new StringContent(
                    payload.ToString(Newtonsoft.Json.Formatting.None),
                    Encoding.UTF8,
                    "application/json");

                using HttpResponseMessage response =
                    await httpClient.SendAsync(request);

                string body =
                    await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine(
                        $"[CollectIQ Auth] Supabase refresh failed: {(int)response.StatusCode} {Trim(body)}");
                    return null;
                }

                JObject json = JObject.Parse(body);

                string accessToken =
                    json.Value<string>("access_token") ?? string.Empty;

                string newRefreshToken =
                    json.Value<string>("refresh_token") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    Debug.WriteLine("[CollectIQ Auth] Refresh response did not contain an access token.");
                    return null;
                }

                await SecureStorage.SetAsync(
                    AccessTokenKey,
                    accessToken);

                // Supabase refresh tokens rotate. Persist the replacement token
                // whenever one is returned; otherwise retain the current token.
                if (!string.IsNullOrWhiteSpace(newRefreshToken))
                {
                    await SecureStorage.SetAsync(
                        RefreshTokenKey,
                        newRefreshToken);
                }

                Debug.WriteLine("[CollectIQ Auth] Supabase session refreshed successfully.");
                return accessToken;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CollectIQ Auth] Supabase refresh exception: {ex}");
                return null;
            }
            finally
            {
                refreshLock.Release();
            }
        }

        private async Task<SessionValidationResult> ValidateAccessTokenAsync(
            string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return SessionValidationResult.RefreshRequired();
            }

            string requestUrl =
                $"{SocialAuthSettings.GetNormalizedSupabaseUrl()}/auth/v1/user";

            using HttpRequestMessage request =
                new HttpRequestMessage(HttpMethod.Get, requestUrl);

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    accessToken);

            request.Headers.Add(
                "apikey",
                SocialAuthSettings.SupabaseAnonKey);

            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            try
            {
                using HttpResponseMessage response =
                    await httpClient.SendAsync(request);

                string body =
                    await response.Content.ReadAsStringAsync();

                if (response.StatusCode == HttpStatusCode.Unauthorized ||
                    response.StatusCode == HttpStatusCode.Forbidden)
                {
                    Debug.WriteLine(
                        $"[CollectIQ Auth] Stored Supabase access token rejected with {(int)response.StatusCode}; refresh required.");
                    return SessionValidationResult.RefreshRequired();
                }

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine(
                        $"[CollectIQ Auth] Supabase session validation returned {(int)response.StatusCode}: {Trim(body)}");
                    return SessionValidationResult.Unrecoverable();
                }

                JObject json = JObject.Parse(body);
                string userId =
                    json.Value<string>("id") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(userId))
                {
                    return SessionValidationResult.Unrecoverable();
                }

                return SessionValidationResult.Valid(userId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CollectIQ Auth] Session validation exception: {ex}");
                return SessionValidationResult.Unrecoverable();
            }
        }

        private static string Trim(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized =
                value.Replace('\r', ' ')
                    .Replace('\n', ' ')
                    .Trim();

            return normalized.Length <= 220
                ? normalized
                : normalized[..220] + "…";
        }

        private sealed class SessionValidationResult
        {
            public bool IsValid { get; private init; }
            public bool ShouldRefresh { get; private init; }
            public string UserId { get; private init; } = string.Empty;

            public static SessionValidationResult Valid(string userId)
            {
                return new SessionValidationResult
                {
                    IsValid = true,
                    UserId = userId
                };
            }

            public static SessionValidationResult RefreshRequired()
            {
                return new SessionValidationResult
                {
                    ShouldRefresh = true
                };
            }

            public static SessionValidationResult Unrecoverable()
            {
                return new SessionValidationResult();
            }
        }
    }
}
