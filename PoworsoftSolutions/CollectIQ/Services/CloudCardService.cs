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
    /// Performs the controlled first card upload and verifies every card by reading it back.
    /// Card image files are intentionally not uploaded here; Supabase Storage is a later step.
    /// </summary>
    public sealed class CloudCardService : ICloudCardService
    {
        private readonly ISupabaseSessionService sessionService;
        private readonly ICloudCardImageStorageService imageStorageService;
        private readonly HttpClient httpClient = new HttpClient();

        public CloudCardService(
            ISupabaseSessionService sessionService,
            ICloudCardImageStorageService imageStorageService)
        {
            this.sessionService = sessionService;
            this.imageStorageService = imageStorageService;
        }

        public async Task<CloudCardSyncResult> UpsertAndVerifyCardsAsync(
            CardCollection collection,
            IReadOnlyCollection<Card> cards)
        {
            if (collection == null || string.IsNullOrWhiteSpace(collection.Id))
            {
                return Failed("No valid local collection was supplied.");
            }

            List<Card> localCards = cards?
                .Where(card =>
                    card != null &&
                    !card.IsDeleted &&
                    string.Equals(card.CollectionId, collection.Id, StringComparison.Ordinal))
                .ToList()
                ?? new List<Card>();

            if (localCards.Count == 0)
            {
                return Failed($"'{collection.Name}' does not contain any local cards to upload.");
            }

            string? accessToken = await sessionService.GetValidAccessTokenAsync();
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return Failed("The Supabase session could not be restored. Test the cloud connection or sign in with Google again.");
            }

            CloudCardSyncResult result =
                await UpsertAndVerifyWithTokenAsync(
                    collection,
                    localCards,
                    accessToken);

            if (result.IsSuccess ||
                !result.Message.StartsWith("AUTH_EXPIRED:", StringComparison.Ordinal))
            {
                return result;
            }

            string? refreshedToken =
                await sessionService.RefreshAccessTokenAsync();

            if (string.IsNullOrWhiteSpace(refreshedToken))
            {
                return Failed("The Supabase session expired and could not be refreshed. Sign in with Google again.");
            }

            return await UpsertAndVerifyWithTokenAsync(
                collection,
                localCards,
                refreshedToken);
        }

        private async Task<CloudCardSyncResult> UpsertAndVerifyWithTokenAsync(
            CardCollection collection,
            List<Card> localCards,
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

            if (!await CollectionExistsForUserAsync(
                    baseUrl,
                    accessToken,
                    collection.Id,
                    authUserId))
            {
                return Failed("The selected collection is not present in Supabase for the signed-in user. Run the collection upload test first.");
            }

            // Keep this first write controlled. A batch of 100 is small enough for the
            // test while still handling normal collections without one request per card.
            const int batchSize = 100;

            for (int offset = 0; offset < localCards.Count; offset += batchSize)
            {
                List<Card> batch = localCards
                    .Skip(offset)
                    .Take(batchSize)
                    .ToList();

                JArray payload = new JArray();

                foreach (Card card in batch)
                {
                    CloudCardImageRefs cloudImageRefs =
                        await imageStorageService.PrepareUploadAsync(
                            card,
                            authUserId,
                            accessToken);

                    payload.Add(
                        BuildCloudCard(
                            card,
                            authUserId,
                            cloudImageRefs));
                }

                string upsertUrl = $"{baseUrl}/rest/v1/cards?on_conflict=id";

                using HttpRequestMessage request =
                    BuildRequest(HttpMethod.Post, upsertUrl, accessToken);

                request.Headers.TryAddWithoutValidation(
                    "Prefer",
                    "resolution=merge-duplicates,return=minimal");

                request.Content = new StringContent(
                    payload.ToString(Formatting.None),
                    Encoding.UTF8,
                    "application/json");

                using HttpResponseMessage response =
                    await httpClient.SendAsync(request);

                string body = await response.Content.ReadAsStringAsync();

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return Failed("AUTH_EXPIRED: Supabase access token expired.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    return Failed(
                        $"Card upload returned HTTP {(int)response.StatusCode}: {TrimMessage(body)}");
                }
            }

            string encodedCollectionId = Uri.EscapeDataString(collection.Id);
            string readUrl =
                $"{baseUrl}/rest/v1/cards" +
                $"?collection_id=eq.{encodedCollectionId}" +
                "&is_deleted=eq.false" +
                "&select=id,collection_id,owner_id";

            using HttpRequestMessage readRequest =
                BuildRequest(HttpMethod.Get, readUrl, accessToken);

            using HttpResponseMessage readResponse =
                await httpClient.SendAsync(readRequest);

            string readBody =
                await readResponse.Content.ReadAsStringAsync();

            if (readResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                return Failed("AUTH_EXPIRED: Supabase access token expired.");
            }

            if (!readResponse.IsSuccessStatusCode)
            {
                return Failed(
                    $"Card read-back returned HTTP {(int)readResponse.StatusCode}: {TrimMessage(readBody)}");
            }

            JArray rows = JArray.Parse(readBody);

            HashSet<string> localIds = localCards
                .Select(card => card.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal);

            HashSet<string> cloudIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (JObject row in rows.OfType<JObject>())
            {
                string id = row.Value<string>("id") ?? string.Empty;
                string collectionId =
                    row.Value<string>("collection_id") ?? string.Empty;
                string ownerId =
                    row.Value<string>("owner_id") ?? string.Empty;

                if (!string.Equals(
                        collectionId,
                        collection.Id,
                        StringComparison.Ordinal))
                {
                    return Failed("A cloud card was returned with the wrong collection ID.");
                }

                if (!string.Equals(
                        ownerId,
                        authUserId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Failed("A cloud card was returned with the wrong owner ID.");
                }

                if (!string.IsNullOrWhiteSpace(id))
                {
                    cloudIds.Add(id);
                }
            }

            List<string> missingIds =
                localIds.Where(id => !cloudIds.Contains(id)).ToList();

            if (missingIds.Count > 0)
            {
                return Failed(
                    $"Cloud verification is missing {missingIds.Count} of {localIds.Count} local card IDs.");
            }

            if (cloudIds.Count != localIds.Count)
            {
                return Failed(
                    $"Card IDs verified, but the collection has {cloudIds.Count} active cloud cards and {localIds.Count} active local cards.");
            }

            return new CloudCardSyncResult
            {
                IsSuccess = true,
                CollectionId = collection.Id,
                LocalCardCount = localIds.Count,
                CloudCardCount = cloudIds.Count,
                Message =
                    $"Uploaded and verified ✓  {cloudIds.Count} card{(cloudIds.Count == 1 ? string.Empty : "s")} in {collection.Name}"
            };
        }

        private async Task<bool> CollectionExistsForUserAsync(
            string baseUrl,
            string accessToken,
            string collectionId,
            string authUserId)
        {
            string encodedId = Uri.EscapeDataString(collectionId);
            string encodedOwner = Uri.EscapeDataString(authUserId);

            string url =
                $"{baseUrl}/rest/v1/collections" +
                $"?id=eq.{encodedId}" +
                $"&owner_id=eq.{encodedOwner}" +
                "&select=id";

            using HttpRequestMessage request =
                BuildRequest(HttpMethod.Get, url, accessToken);

            using HttpResponseMessage response =
                await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            string body =
                await response.Content.ReadAsStringAsync();

            JArray rows = JArray.Parse(body);
            return rows.Count == 1;
        }

        private async Task<string> GetAuthenticatedUserIdAsync(
            string baseUrl,
            string accessToken)
        {
            using HttpRequestMessage request =
                BuildRequest(
                    HttpMethod.Get,
                    $"{baseUrl}/auth/v1/user",
                    accessToken);

            using HttpResponseMessage response =
                await httpClient.SendAsync(request);

            string body =
                await response.Content.ReadAsStringAsync();

            if (response.StatusCode == HttpStatusCode.Unauthorized ||
                response.StatusCode == HttpStatusCode.Forbidden)
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

        private static JObject BuildCloudCard(
            Card card,
            string authUserId,
            CloudCardImageRefs cloudImageRefs)
        {
            return new JObject
            {
                ["id"] = card.Id,
                ["collection_id"] = card.CollectionId,
                ["owner_id"] = authUserId,

                ["title"] = card.Title ?? string.Empty,
                ["year"] = card.Year.HasValue
                    ? new JValue(card.Year.Value)
                    : JValue.CreateNull(),
                ["card_set"] = card.Set ?? string.Empty,
                ["card_number"] = card.Number ?? string.Empty,

                ["purchase_price"] = card.PurchasePrice.HasValue
                    ? new JValue(card.PurchasePrice.Value)
                    : JValue.CreateNull(),
                ["estimated_value"] = card.EstimatedValue.HasValue
                    ? new JValue(card.EstimatedValue.Value)
                    : JValue.CreateNull(),

                ["front_image_url"] =
                    string.IsNullOrWhiteSpace(cloudImageRefs.Front)
                        ? JValue.CreateNull()
                        : new JValue(cloudImageRefs.Front),
                ["front_thumbnail_url"] =
                    string.IsNullOrWhiteSpace(cloudImageRefs.FrontThumbnail)
                        ? JValue.CreateNull()
                        : new JValue(cloudImageRefs.FrontThumbnail),
                ["back_image_url"] =
                    string.IsNullOrWhiteSpace(cloudImageRefs.Back)
                        ? JValue.CreateNull()
                        : new JValue(cloudImageRefs.Back),
                ["front_overlay_image_url"] =
                    string.IsNullOrWhiteSpace(cloudImageRefs.FrontOverlay)
                        ? JValue.CreateNull()
                        : new JValue(cloudImageRefs.FrontOverlay),
                ["back_overlay_image_url"] =
                    string.IsNullOrWhiteSpace(cloudImageRefs.BackOverlay)
                        ? JValue.CreateNull()
                        : new JValue(cloudImageRefs.BackOverlay),

                ["sport_value"] = card.SportValue,
                ["parallel"] = card.Parallel ?? string.Empty,
                ["subset"] = card.Subset ?? string.Empty,
                ["serial_number"] = card.SerialNumber ?? string.Empty,

                ["player_json"] = ParseJsonObject(card.PlayerJson),
                ["team_json"] = ParseJsonObject(card.TeamJson),
                ["grading_json"] = ParseJsonObject(card.GradingJson),
                ["highlight_json"] = ParseJsonObject(card.HighlightJson),

                ["is_deleted"] = card.IsDeleted,
                ["created_at"] = card.CreatedUtc,
                ["updated_at"] = card.UpdatedUtc,
                ["deleted_at"] = card.IsDeleted
                    ? new JValue(card.UpdatedUtc)
                    : JValue.CreateNull()
            };
        }

        private static JToken ParseJsonObject(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new JObject();
            }

            try
            {
                JToken token = JToken.Parse(json);
                return token.Type == JTokenType.Object
                    ? token
                    : new JObject();
            }
            catch
            {
                return new JObject();
            }
        }

        private static HttpRequestMessage BuildRequest(
            HttpMethod method,
            string url,
            string accessToken)
        {
            HttpRequestMessage request =
                new HttpRequestMessage(method, url);

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    accessToken);

            request.Headers.Add(
                "apikey",
                SocialAuthSettings.SupabaseAnonKey);

            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue(
                    "application/json"));

            return request;
        }

        private static CloudCardSyncResult Failed(string message)
        {
            return new CloudCardSyncResult
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

            string normalized =
                value.Replace('\r', ' ')
                    .Replace('\n', ' ')
                    .Trim();

            return normalized.Length <= 260
                ? normalized
                : normalized[..260] + "…";
        }
    }
}
