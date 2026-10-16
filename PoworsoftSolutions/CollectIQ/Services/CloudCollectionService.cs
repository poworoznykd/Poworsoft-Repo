using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CollectIQ.Interfaces;
using CollectIQ.Models;
using CollectIQ.Models.Cloud;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CollectIQ.Services
{
    /// <summary>
    /// Performs the first controlled write/read verification against the Supabase collections table.
    /// </summary>
    public sealed class CloudCollectionService : ICloudCollectionService
    {
        private readonly ISupabaseSessionService sessionService;
        private readonly HttpClient httpClient = new HttpClient();

        public CloudCollectionService(ISupabaseSessionService sessionService)
        {
            this.sessionService = sessionService;
        }

        public async Task<CloudCollectionSyncResult> UpsertAndVerifyCollectionAsync(CardCollection collection)
        {
            if (collection == null)
            {
                return Failed("No local collection was supplied.");
            }

            if (string.IsNullOrWhiteSpace(collection.Id))
            {
                return Failed("The local collection does not have an ID.");
            }

            string? accessToken = await sessionService.GetAccessTokenAsync();
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return Failed("No Supabase session was found. Test the cloud connection first.");
            }

            CloudCollectionSyncResult result = await UpsertAndVerifyWithTokenAsync(collection, accessToken);

            if (result.IsSuccess || !result.Message.StartsWith("AUTH_EXPIRED:", StringComparison.Ordinal))
            {
                return result;
            }

            string? refreshedToken = await sessionService.RefreshAccessTokenAsync();
            if (string.IsNullOrWhiteSpace(refreshedToken))
            {
                return Failed("The Supabase session expired and could not be refreshed. Test the cloud connection again.");
            }

            return await UpsertAndVerifyWithTokenAsync(collection, refreshedToken);
        }

        private async Task<CloudCollectionSyncResult> UpsertAndVerifyWithTokenAsync(
            CardCollection collection,
            string accessToken)
        {
            string baseUrl = SocialAuthSettings.GetNormalizedSupabaseUrl();

            string authUserId = await GetAuthenticatedUserIdAsync(baseUrl, accessToken);
            if (authUserId == "__AUTH_EXPIRED__")
            {
                return Failed("AUTH_EXPIRED: Supabase access token expired.");
            }

            if (string.IsNullOrWhiteSpace(authUserId))
            {
                return Failed("Supabase Auth did not return a user ID.");
            }

            JObject cloudCollection = new JObject
            {
                ["id"] = collection.Id,
                ["owner_id"] = authUserId,
                ["name"] = collection.Name ?? "My Collection",
                ["description"] = collection.Description ?? string.Empty,
                ["visibility"] = NormalizeVisibility(collection.Visibility),
                ["is_default"] = collection.IsDefault,
                ["is_deleted"] = collection.IsDeleted,
                ["created_at"] = ToUtcIso(collection.CreatedUtc),
                ["updated_at"] = ToUtcIso(collection.UpdatedUtc),
                ["deleted_at"] = collection.IsDeleted ? ToUtcIso(collection.UpdatedUtc) : JValue.CreateNull()
            };

            string upsertUrl = $"{baseUrl}/rest/v1/collections?on_conflict=id";
            using HttpRequestMessage upsertRequest = BuildRequest(HttpMethod.Post, upsertUrl, accessToken);
            upsertRequest.Headers.TryAddWithoutValidation("Prefer", "resolution=merge-duplicates,return=representation");
            upsertRequest.Content = new StringContent(
                new JArray(cloudCollection).ToString(Formatting.None),
                Encoding.UTF8,
                "application/json");

            using HttpResponseMessage upsertResponse = await httpClient.SendAsync(upsertRequest);
            string upsertBody = await upsertResponse.Content.ReadAsStringAsync();

            if (upsertResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                return Failed("AUTH_EXPIRED: Supabase access token expired.");
            }

            if (!upsertResponse.IsSuccessStatusCode)
            {
                return Failed($"Collection upload returned HTTP {(int)upsertResponse.StatusCode}: {TrimMessage(upsertBody)}");
            }

            string encodedId = Uri.EscapeDataString(collection.Id);
            string readUrl =
                $"{baseUrl}/rest/v1/collections?id=eq.{encodedId}" +
                "&select=id,owner_id,name,description,visibility,is_default,is_deleted";

            using HttpRequestMessage readRequest = BuildRequest(HttpMethod.Get, readUrl, accessToken);
            using HttpResponseMessage readResponse = await httpClient.SendAsync(readRequest);
            string readBody = await readResponse.Content.ReadAsStringAsync();

            if (readResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                return Failed("AUTH_EXPIRED: Supabase access token expired.");
            }

            if (!readResponse.IsSuccessStatusCode)
            {
                return Failed($"Collection read-back returned HTTP {(int)readResponse.StatusCode}: {TrimMessage(readBody)}");
            }

            JArray rows = JArray.Parse(readBody);
            if (rows.Count != 1)
            {
                return Failed($"Expected one cloud collection after upload but found {rows.Count}.");
            }

            JObject row = (JObject)rows[0]!;
            string cloudId = row.Value<string>("id") ?? string.Empty;
            string cloudOwnerId = row.Value<string>("owner_id") ?? string.Empty;
            string cloudName = row.Value<string>("name") ?? string.Empty;

            if (!string.Equals(cloudId, collection.Id, StringComparison.Ordinal))
            {
                return Failed("The cloud collection ID does not match the local collection ID.");
            }

            if (!string.Equals(cloudOwnerId, authUserId, StringComparison.OrdinalIgnoreCase))
            {
                return Failed("The cloud collection owner does not match the authenticated Supabase user.");
            }

            if (!string.Equals(cloudName, collection.Name ?? "My Collection", StringComparison.Ordinal))
            {
                return Failed("The cloud collection name does not match the local collection name.");
            }

            return new CloudCollectionSyncResult
            {
                IsSuccess = true,
                CollectionId = cloudId,
                CollectionName = cloudName,
                OwnerUserId = cloudOwnerId,
                Message = $"Uploaded and verified ✓  {cloudName}"
            };
        }

        private async Task<string> GetAuthenticatedUserIdAsync(string baseUrl, string accessToken)
        {
            using HttpRequestMessage request = BuildRequest(
                HttpMethod.Get,
                $"{baseUrl}/auth/v1/user",
                accessToken);

            using HttpResponseMessage response = await httpClient.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return "__AUTH_EXPIRED__";
            }

            if (!response.IsSuccessStatusCode)
            {
                return string.Empty;
            }

            JObject user = JObject.Parse(body);
            return user.Value<string>("id") ?? string.Empty;
        }

        private static HttpRequestMessage BuildRequest(HttpMethod method, string url, string accessToken)
        {
            HttpRequestMessage request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("apikey", SocialAuthSettings.SupabaseAnonKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return request;
        }

        private static string NormalizeVisibility(string? visibility)
        {
            if (string.Equals(visibility, CollectionVisibility.Public, StringComparison.OrdinalIgnoreCase))
            {
                return CollectionVisibility.Public;
            }

            if (string.Equals(visibility, CollectionVisibility.Shared, StringComparison.OrdinalIgnoreCase))
            {
                return CollectionVisibility.Shared;
            }

            return CollectionVisibility.Private;
        }

        private static string ToUtcIso(DateTime value)
        {
            DateTime utc = value.Kind == DateTimeKind.Utc
                ? value
                : value.ToUniversalTime();

            return utc.ToString("O", CultureInfo.InvariantCulture);
        }

        private static CloudCollectionSyncResult Failed(string message)
        {
            return new CloudCollectionSyncResult
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
            return normalized.Length <= 220 ? normalized : normalized[..220] + "…";
        }
    }
}
