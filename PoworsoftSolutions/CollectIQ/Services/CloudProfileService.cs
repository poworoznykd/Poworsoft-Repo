using System.Net;
using System.Net.Http.Headers;
using CollectIQ.Interfaces;
using CollectIQ.Models.Cloud;
using Newtonsoft.Json.Linq;

namespace CollectIQ.Services
{
    /// <summary>
    /// Provides the first read-only authenticated bridge between CollectIQ and Supabase PostgreSQL.
    /// </summary>
    public sealed class CloudProfileService : ICloudProfileService
    {
        private readonly ISupabaseSessionService sessionService;
        private readonly HttpClient httpClient = new HttpClient();

        public CloudProfileService(ISupabaseSessionService sessionService)
        {
            this.sessionService = sessionService;
        }

        public async Task<CloudConnectionResult> TestConnectionAsync(string? expectedProviderUserId = null)
        {
            string? accessToken = await sessionService.GetAccessTokenAsync();

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return Failed("No Supabase session was found. Sign in with Google again, then retry.");
            }

            CloudConnectionResult result = await TestWithTokenAsync(accessToken, expectedProviderUserId);

            if (result.IsSuccess || !result.Message.StartsWith("AUTH_EXPIRED:", StringComparison.Ordinal))
            {
                return result;
            }

            string? refreshedToken = await sessionService.RefreshAccessTokenAsync();

            if (string.IsNullOrWhiteSpace(refreshedToken))
            {
                return Failed("The Supabase session expired and could not be refreshed. Sign in with Google again.");
            }

            return await TestWithTokenAsync(refreshedToken, expectedProviderUserId);
        }

        private async Task<CloudConnectionResult> TestWithTokenAsync(
            string accessToken,
            string? expectedProviderUserId)
        {
            string baseUrl = SocialAuthSettings.GetNormalizedSupabaseUrl();

            using HttpRequestMessage userRequest = BuildRequest(
                HttpMethod.Get,
                $"{baseUrl}/auth/v1/user",
                accessToken);

            using HttpResponseMessage userResponse = await httpClient.SendAsync(userRequest);
            string userBody = await userResponse.Content.ReadAsStringAsync();

            if (userResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                return Failed("AUTH_EXPIRED: Supabase access token expired.");
            }

            if (!userResponse.IsSuccessStatusCode)
            {
                return Failed($"Supabase Auth returned HTTP {(int)userResponse.StatusCode}.");
            }

            JObject authUser = JObject.Parse(userBody);
            string authUserId = authUser.Value<string>("id") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(authUserId))
            {
                return Failed("Supabase Auth did not return a user ID.");
            }

            if (!string.IsNullOrWhiteSpace(expectedProviderUserId) &&
                !string.Equals(authUserId, expectedProviderUserId, StringComparison.OrdinalIgnoreCase))
            {
                return Failed("The local CollectIQ profile is linked to a different Supabase user.");
            }

            string encodedUserId = Uri.EscapeDataString(authUserId);
            string profileUrl = $"{baseUrl}/rest/v1/profiles?id=eq.{encodedUserId}&select=id,display_name";

            using HttpRequestMessage profileRequest = BuildRequest(HttpMethod.Get, profileUrl, accessToken);
            profileRequest.Headers.Add("Accept-Profile", "public");

            using HttpResponseMessage profileResponse = await httpClient.SendAsync(profileRequest);
            string profileBody = await profileResponse.Content.ReadAsStringAsync();

            if (profileResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                return Failed("AUTH_EXPIRED: Supabase access token expired.");
            }

            if (!profileResponse.IsSuccessStatusCode)
            {
                return Failed($"Supabase database returned HTTP {(int)profileResponse.StatusCode}: {TrimMessage(profileBody)}");
            }

            JArray profiles = JArray.Parse(profileBody);

            if (profiles.Count != 1)
            {
                return Failed($"Expected one cloud profile for the signed-in user but found {profiles.Count}.");
            }

            JObject profile = (JObject)profiles[0]!;
            string profileId = profile.Value<string>("id") ?? string.Empty;
            string displayName = profile.Value<string>("display_name") ?? string.Empty;

            if (!string.Equals(profileId, authUserId, StringComparison.OrdinalIgnoreCase))
            {
                return Failed("The cloud profile ID does not match the authenticated Supabase user.");
            }

            return new CloudConnectionResult
            {
                IsSuccess = true,
                AuthUserId = authUserId,
                DisplayName = displayName,
                Message = "Authenticated cloud connection is working."
            };
        }

        private static HttpRequestMessage BuildRequest(HttpMethod method, string url, string accessToken)
        {
            HttpRequestMessage request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("apikey", SocialAuthSettings.SupabaseAnonKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return request;
        }

        private static CloudConnectionResult Failed(string message)
        {
            return new CloudConnectionResult
            {
                IsSuccess = false,
                Message = message
            };
        }

        private static string TrimMessage(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "No response body";
            }

            string normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return normalized.Length <= 180 ? normalized : normalized[..180] + "…";
        }
    }
}
