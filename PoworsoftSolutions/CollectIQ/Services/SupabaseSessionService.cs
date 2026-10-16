using System.Net.Http.Headers;
using System.Text;
using CollectIQ.Interfaces;
using Newtonsoft.Json.Linq;

namespace CollectIQ.Services
{
    /// <summary>
    /// Reads and refreshes the Supabase session created by the existing social-authentication flow.
    /// </summary>
    public sealed class SupabaseSessionService : ISupabaseSessionService
    {
        private const string AccessTokenKey = "supabase_access_token";
        private const string RefreshTokenKey = "supabase_refresh_token";

        private readonly HttpClient httpClient = new HttpClient();

        public Task<string?> GetAccessTokenAsync()
        {
            return SecureStorage.GetAsync(AccessTokenKey);
        }

        public async Task<string?> RefreshAccessTokenAsync()
        {
            string? refreshToken = await SecureStorage.GetAsync(RefreshTokenKey);

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return null;
            }

            string requestUrl = $"{SocialAuthSettings.GetNormalizedSupabaseUrl()}/auth/v1/token?grant_type=refresh_token";

            JObject payload = new JObject
            {
                ["refresh_token"] = refreshToken
            };

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Headers.Add("apikey", SocialAuthSettings.SupabaseAnonKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent(
                payload.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json");

            using HttpResponseMessage response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string body = await response.Content.ReadAsStringAsync();
            JObject json = JObject.Parse(body);

            string accessToken = json.Value<string>("access_token") ?? string.Empty;
            string newRefreshToken = json.Value<string>("refresh_token") ?? refreshToken;

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return null;
            }

            await SecureStorage.SetAsync(AccessTokenKey, accessToken);

            if (!string.IsNullOrWhiteSpace(newRefreshToken))
            {
                await SecureStorage.SetAsync(RefreshTokenKey, newRefreshToken);
            }

            return accessToken;
        }
    }
}
