using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CollectIQ.Interfaces;
using CollectIQ.Models;
using CollectIQ.Models.Cloud;
using CollectIQ.Services.Session;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SQLite;

namespace CollectIQ.Services
{
    /// <summary>
    /// Synchronizes the signed-in user's owned collections and cards between
    /// SQLite and Supabase.
    ///
    /// Local writes are always committed to SQLite first and pushed through the
    /// existing outbox. Cloud pulls use server_updated_at as the incremental
    /// cursor and never overwrite a local entity that still has unresolved
    /// outbox work.
    /// </summary>
    public sealed class SyncService : ISyncService
    {
        private const string SyncEntityCollection = nameof(CardCollection);
        private const string SyncEntityCard = nameof(Card);
        private const string PullCursorPrefix = "collectiq_cloud_pull_cursor_";

        private readonly IDatabase database;
        private readonly ISupabaseSessionService sessionService;
        private readonly ICloudCardImageStorageService imageStorageService;
        private readonly HttpClient httpClient = new HttpClient();
        private readonly SemaphoreSlim syncLock = new SemaphoreSlim(1, 1);

        public string LastPullSummary { get; private set; } =
            "Cloud pull has not run yet.";

        public SyncService(
            IDatabase database,
            ISupabaseSessionService sessionService,
            ICloudCardImageStorageService imageStorageService)
        {
            this.database = database;
            this.sessionService = sessionService;
            this.imageStorageService = imageStorageService;
        }

        public Task ResetPullCursorAsync()
        {
            string? providerUserId =
                UserSession.CurrentUser?.ProviderUserId;

            if (!string.IsNullOrWhiteSpace(providerUserId))
            {
                Preferences.Remove(
                    GetPullCursorKey(providerUserId));
            }

            LastPullSummary =
                "Cloud pull cursor cleared. The next sync will perform a full pull.";

            return Task.CompletedTask;
        }

        public async Task SyncAsync()
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                LastPullSummary =
                    "Offline — cloud pull deferred.";
                return;
            }

            if (!await syncLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                string localAccountId =
                    UserSession.CurrentUserAccountId;

                UserProfile? currentProfile =
                    UserSession.CurrentUser;

                if (string.IsNullOrWhiteSpace(localAccountId) ||
                    currentProfile == null ||
                    string.IsNullOrWhiteSpace(currentProfile.ProviderUserId))
                {
                    LastPullSummary =
                        "Cloud pull skipped because no linked CollectIQ profile is active.";
                    return;
                }

                string? accessToken =
                    await sessionService.GetValidAccessTokenAsync();

                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    LastPullSummary =
                        "Cloud pull skipped because the Supabase session could not be restored.";
                    return;
                }

                string? authenticatedUserId =
                    await sessionService.GetAuthenticatedUserIdAsync();

                if (string.IsNullOrWhiteSpace(authenticatedUserId) ||
                    !string.Equals(
                        authenticatedUserId,
                        currentProfile.ProviderUserId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    LastPullSummary =
                        "Cloud pull stopped because the Supabase identity does not match the local profile.";
                    return;
                }

                SQLiteAsyncConnection connection =
                    await database.GetConnectionAsync();

                await PushPendingChangesAsync(
                    connection,
                    localAccountId,
                    authenticatedUserId,
                    accessToken);

                // A push retry can rotate the Supabase session. Always reacquire
                // the validated access token before beginning the pull.
                accessToken =
                    await sessionService.GetValidAccessTokenAsync();

                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    LastPullSummary =
                        "Push completed, but the Supabase session was unavailable for pull.";
                    return;
                }

                await PullOwnedCloudDataAsync(
                    connection,
                    localAccountId,
                    authenticatedUserId,
                    accessToken);
            }
            catch (Exception ex)
            {
                LastPullSummary =
                    $"Cloud pull stopped: {ex.Message}";

                Debug.WriteLine(
                    $"[CollectIQ Sync] Sync stopped: {ex}");
            }
            finally
            {
                syncLock.Release();
            }
        }

        private async Task PushPendingChangesAsync(
            SQLiteAsyncConnection connection,
            string localAccountId,
            string authenticatedUserId,
            string accessToken)
        {
            List<SyncQueueItem> pending =
                await database.GetPendingSyncItemsAsync(500);

            List<SyncQueueItem> supported = pending
                .Where(item =>
                    string.Equals(
                        item.UserAccountId,
                        localAccountId,
                        StringComparison.Ordinal) &&
                    (item.EntityType == SyncEntityCollection ||
                     item.EntityType == SyncEntityCard))
                .OrderBy(item =>
                    item.EntityType == SyncEntityCollection ? 0 : 1)
                .ThenBy(item => item.CreatedUtc)
                .ToList();

            foreach (SyncQueueItem item in supported)
            {
                try
                {
                    bool succeeded =
                        await PushQueueItemAsync(
                            connection,
                            item,
                            localAccountId,
                            authenticatedUserId,
                            accessToken);

                    if (succeeded)
                    {
                        await database.MarkSyncItemSucceededAsync(
                            item.Id);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    string? refreshedToken =
                        await sessionService.RefreshAccessTokenAsync();

                    if (string.IsNullOrWhiteSpace(refreshedToken))
                    {
                        await database.MarkSyncItemFailedAsync(
                            item.Id,
                            "Supabase session expired and could not be refreshed.");

                        return;
                    }

                    accessToken = refreshedToken;

                    try
                    {
                        string? refreshedUserId =
                            await sessionService.GetAuthenticatedUserIdAsync();

                        if (string.IsNullOrWhiteSpace(refreshedUserId) ||
                            !string.Equals(
                                refreshedUserId,
                                authenticatedUserId,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            await database.MarkSyncItemFailedAsync(
                                item.Id,
                                "Supabase identity changed while refreshing the session.");

                            return;
                        }

                        bool retried =
                            await PushQueueItemAsync(
                                connection,
                                item,
                                localAccountId,
                                authenticatedUserId,
                                accessToken);

                        if (retried)
                        {
                            await database.MarkSyncItemSucceededAsync(
                                item.Id);
                        }
                    }
                    catch (Exception retryException)
                    {
                        await database.MarkSyncItemFailedAsync(
                            item.Id,
                            retryException.Message);
                    }
                }
                catch (Exception ex)
                {
                    await database.MarkSyncItemFailedAsync(
                        item.Id,
                        ex.Message);
                }
            }
        }

        private Task<bool> PushQueueItemAsync(
            SQLiteAsyncConnection connection,
            SyncQueueItem item,
            string localAccountId,
            string authenticatedUserId,
            string accessToken)
        {
            return item.EntityType switch
            {
                SyncEntityCollection => SyncCollectionAsync(
                    connection,
                    item,
                    localAccountId,
                    authenticatedUserId,
                    accessToken),

                SyncEntityCard => SyncCardAsync(
                    connection,
                    item,
                    localAccountId,
                    authenticatedUserId,
                    accessToken),

                _ => Task.FromResult(false)
            };
        }

        private async Task PullOwnedCloudDataAsync(
            SQLiteAsyncConnection connection,
            string localAccountId,
            string authenticatedUserId,
            string accessToken)
        {
            List<SyncQueueItem> unresolvedQueue =
                await connection.Table<SyncQueueItem>()
                    .Where(item =>
                        !item.IsDeleted &&
                        item.UserAccountId == localAccountId &&
                        item.Status != "Completed")
                    .ToListAsync();

            HashSet<string> pendingCollections =
                unresolvedQueue
                    .Where(item =>
                        item.EntityType == SyncEntityCollection)
                    .Select(item => item.EntityId)
                    .ToHashSet(StringComparer.Ordinal);

            HashSet<string> pendingCards =
                unresolvedQueue
                    .Where(item =>
                        item.EntityType == SyncEntityCard)
                    .Select(item => item.EntityId)
                    .ToHashSet(StringComparer.Ordinal);

            DateTime? cursor =
                ReadPullCursor(authenticatedUserId);

            CloudPullBatch<CardCollection> collectionBatch =
                await PullCollectionsAsync(
                    connection,
                    localAccountId,
                    authenticatedUserId,
                    accessToken,
                    pendingCollections,
                    cursor);

            if (collectionBatch.Items.Count > 0)
            {
                await database.ApplyCloudCollectionsAsync(
                    collectionBatch.Items);
            }

            List<CardCollection> localCollections =
                await connection.Table<CardCollection>()
                    .Where(row =>
                        row.OwnerUserAccountId == localAccountId)
                    .ToListAsync();

            HashSet<string> knownCollectionIds =
                localCollections
                    .Select(row => row.Id)
                    .ToHashSet(StringComparer.Ordinal);

            CloudPullBatch<Card> cardBatch =
                await PullCardsAsync(
                    connection,
                    authenticatedUserId,
                    accessToken,
                    pendingCards,
                    knownCollectionIds,
                    cursor);

            if (cardBatch.Items.Count > 0)
            {
                await database.ApplyCloudCardsAsync(
                    cardBatch.Items);
            }

            DateTime? newestCloudChange =
                MaxUtc(
                    collectionBatch.NewestServerUpdateUtc,
                    cardBatch.NewestServerUpdateUtc);

            if (newestCloudChange.HasValue)
            {
                SavePullCursor(
                    authenticatedUserId,
                    newestCloudChange.Value);
            }

            int protectedCount =
                collectionBatch.ProtectedByLocalPending +
                cardBatch.ProtectedByLocalPending;

            int staleSkipped =
                collectionBatch.SkippedAsStale +
                cardBatch.SkippedAsStale;

            string mode =
                cursor.HasValue
                    ? "incremental"
                    : "full";

            LastPullSummary =
                $"{mode} pull: applied {collectionBatch.Items.Count} collection row" +
                $"{(collectionBatch.Items.Count == 1 ? string.Empty : "s")} and " +
                $"{cardBatch.Items.Count} card row" +
                $"{(cardBatch.Items.Count == 1 ? string.Empty : "s")}" +
                $"{(protectedCount > 0 ? $"; protected {protectedCount} pending local change{(protectedCount == 1 ? string.Empty : "s")}" : string.Empty)}" +
                $"{(staleSkipped > 0 ? $"; skipped {staleSkipped} older cloud row{(staleSkipped == 1 ? string.Empty : "s")}" : string.Empty)}.";
        }

        private async Task<CloudPullBatch<CardCollection>> PullCollectionsAsync(
            SQLiteAsyncConnection connection,
            string localAccountId,
            string authenticatedUserId,
            string accessToken,
            HashSet<string> pendingCollections,
            DateTime? cursor)
        {
            string owner =
                Uri.EscapeDataString(
                    authenticatedUserId);

            string cursorFilter =
                BuildCursorFilter(cursor);

            string url =
                $"{SocialAuthSettings.GetNormalizedSupabaseUrl()}/rest/v1/collections" +
                $"?owner_id=eq.{owner}" +
                cursorFilter +
                "&select=id,owner_id,name,description,visibility,is_default,is_deleted,created_at,updated_at,deleted_at,server_updated_at" +
                "&order=server_updated_at.asc";

            JArray rows =
                await GetJsonArrayWithAuthRetryAsync(
                    url,
                    accessToken);

            CloudPullBatch<CardCollection> batch =
                new CloudPullBatch<CardCollection>();

            foreach (JObject row in rows.OfType<JObject>())
            {
                DateTime? serverUpdatedUtc =
                    ReadUtc(row["server_updated_at"]);

                batch.ObserveServerTimestamp(
                    serverUpdatedUtc);

                string id =
                    row.Value<string>("id") ??
                    string.Empty;

                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (pendingCollections.Contains(id))
                {
                    batch.ProtectedByLocalPending++;
                    continue;
                }

                string ownerId =
                    row.Value<string>("owner_id") ??
                    string.Empty;

                if (!string.Equals(
                        ownerId,
                        authenticatedUserId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                CardCollection? local =
                    await connection.Table<CardCollection>()
                        .Where(item => item.Id == id)
                        .FirstOrDefaultAsync();

                DateTime cloudEffectiveUtc =
                    MaxUtc(
                        ReadUtc(row["updated_at"]),
                        serverUpdatedUtc) ??
                    DateTime.UtcNow;

                if (local != null &&
                    local.UpdatedUtc != default &&
                    local.UpdatedUtc > cloudEffectiveUtc)
                {
                    batch.SkippedAsStale++;
                    continue;
                }

                batch.Items.Add(
                    new CardCollection
                    {
                        Id = id,
                        OwnerUserAccountId =
                            localAccountId,
                        Name =
                            row.Value<string>("name") ??
                            "My Collection",
                        Description =
                            row.Value<string>("description") ??
                            string.Empty,
                        Visibility =
                            NormalizeVisibility(
                                row.Value<string>("visibility")),
                        IsDefault =
                            row.Value<bool?>("is_default") ??
                            false,
                        IsDeleted =
                            row.Value<bool?>("is_deleted") ??
                            false,
                        CreatedUtc =
                            ReadUtc(row["created_at"]) ??
                            local?.CreatedUtc ??
                            cloudEffectiveUtc,
                        UpdatedUtc =
                            cloudEffectiveUtc
                    });
            }

            return batch;
        }

        private async Task<CloudPullBatch<Card>> PullCardsAsync(
            SQLiteAsyncConnection connection,
            string authenticatedUserId,
            string accessToken,
            HashSet<string> pendingCards,
            HashSet<string> knownCollectionIds,
            DateTime? cursor)
        {
            string owner =
                Uri.EscapeDataString(
                    authenticatedUserId);

            string cursorFilter =
                BuildCursorFilter(cursor);

            string url =
                $"{SocialAuthSettings.GetNormalizedSupabaseUrl()}/rest/v1/cards" +
                $"?owner_id=eq.{owner}" +
                cursorFilter +
                "&select=id,collection_id,owner_id,title,year,card_set,card_number,purchase_price,estimated_value,front_image_url,front_thumbnail_url,back_image_url,front_overlay_image_url,back_overlay_image_url,sport_value,parallel,subset,serial_number,player_json,team_json,grading_json,highlight_json,is_deleted,created_at,updated_at,deleted_at,server_updated_at" +
                "&order=server_updated_at.asc";

            JArray rows =
                await GetJsonArrayWithAuthRetryAsync(
                    url,
                    accessToken);

            CloudPullBatch<Card> batch =
                new CloudPullBatch<Card>();

            foreach (JObject row in rows.OfType<JObject>())
            {
                DateTime? serverUpdatedUtc =
                    ReadUtc(row["server_updated_at"]);

                batch.ObserveServerTimestamp(
                    serverUpdatedUtc);

                string id =
                    row.Value<string>("id") ??
                    string.Empty;

                string collectionId =
                    row.Value<string>("collection_id") ??
                    string.Empty;

                if (string.IsNullOrWhiteSpace(id) ||
                    string.IsNullOrWhiteSpace(collectionId))
                {
                    continue;
                }

                if (pendingCards.Contains(id))
                {
                    batch.ProtectedByLocalPending++;
                    continue;
                }

                if (!knownCollectionIds.Contains(
                        collectionId))
                {
                    continue;
                }

                string ownerId =
                    row.Value<string>("owner_id") ??
                    string.Empty;

                if (!string.Equals(
                        ownerId,
                        authenticatedUserId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Card? local =
                    await connection.Table<Card>()
                        .Where(card => card.Id == id)
                        .FirstOrDefaultAsync();

                DateTime cloudEffectiveUtc =
                    MaxUtc(
                        ReadUtc(row["updated_at"]),
                        serverUpdatedUtc) ??
                    DateTime.UtcNow;

                if (local != null &&
                    local.UpdatedUtc != default &&
                    local.UpdatedUtc > cloudEffectiveUtc)
                {
                    batch.SkippedAsStale++;
                    continue;
                }

                Card pulledCard =
                    new Card
                    {
                        Id = id,
                        CollectionId =
                            collectionId,
                        Title =
                            row.Value<string>("title") ??
                            string.Empty,
                        Year =
                            row.Value<int?>("year"),
                        Set =
                            row.Value<string>("card_set") ??
                            string.Empty,
                        Number =
                            row.Value<string>("card_number") ??
                            string.Empty,
                        PurchasePrice =
                            row.Value<decimal?>("purchase_price"),
                        EstimatedValue =
                            row.Value<decimal?>("estimated_value"),

                        // Cloud image storage comes next. Preserve the current
                        // device's paths while syncing metadata.
                        FrontImagePath =
                            local?.FrontImagePath,
                        FrontThumbnailPath =
                            local?.FrontThumbnailPath,
                        BackImagePath =
                            local?.BackImagePath ??
                            string.Empty,
                        FrontOverlayImagePath =
                            local?.FrontOverlayImagePath ??
                            string.Empty,
                        BackOverlayImagePath =
                            local?.BackOverlayImagePath ??
                            string.Empty,

                        SportValue =
                            row.Value<int?>("sport_value") ??
                            0,
                        Parallel =
                            row.Value<string>("parallel") ??
                            string.Empty,
                        Subset =
                            row.Value<string>("subset") ??
                            string.Empty,
                        SerialNumber =
                            row.Value<string>("serial_number") ??
                            string.Empty,

                        PlayerJson =
                            JsonObjectToString(
                                row["player_json"]),
                        TeamJson =
                            JsonObjectToString(
                                row["team_json"]),
                        GradingJson =
                            JsonObjectToString(
                                row["grading_json"]),
                        HighlightJson =
                            JsonObjectToString(
                                row["highlight_json"]),

                        IsDeleted =
                            row.Value<bool?>("is_deleted") ??
                            false,
                        CreatedUtc =
                            ReadUtc(row["created_at"]) ??
                            local?.CreatedUtc ??
                            cloudEffectiveUtc,
                        UpdatedUtc =
                            cloudEffectiveUtc
                    };

                CloudCardImageRefs cloudImageRefs =
                    CloudCardImageStorageService.FromRow(
                        row);

                await imageStorageService.PopulateLocalImagesFromCloudAsync(
                    pulledCard,
                    cloudImageRefs,
                    accessToken);

                batch.Items.Add(
                    pulledCard);
            }

            return batch;
        }

        private static string BuildCursorFilter(
            DateTime? cursor)
        {
            if (!cursor.HasValue)
            {
                return string.Empty;
            }

            // Use a tiny overlap. It avoids edge cases where two rows share the
            // same server timestamp or the local preference loses sub-ms precision.
            DateTime overlap =
                cursor.Value.AddSeconds(-1);

            string encoded =
                Uri.EscapeDataString(
                    overlap.ToUniversalTime()
                        .ToString(
                            "O",
                            CultureInfo.InvariantCulture));

            return $"&server_updated_at=gte.{encoded}";
        }

        private static string GetPullCursorKey(
            string providerUserId)
        {
            // Preferences keys stay compact and avoid exposing the full provider ID.
            byte[] bytes =
                System.Security.Cryptography.SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        providerUserId));

            string suffix =
                Convert.ToHexString(bytes)
                    .Substring(0, 16);

            return PullCursorPrefix + suffix;
        }

        private static DateTime? ReadPullCursor(
            string providerUserId)
        {
            string raw =
                Preferences.Get(
                    GetPullCursorKey(providerUserId),
                    string.Empty);

            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (!DateTimeOffset.TryParse(
                    raw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal |
                    DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset parsed))
            {
                return null;
            }

            return parsed.UtcDateTime;
        }

        private static void SavePullCursor(
            string providerUserId,
            DateTime valueUtc)
        {
            Preferences.Set(
                GetPullCursorKey(providerUserId),
                valueUtc.ToUniversalTime()
                    .ToString(
                        "O",
                        CultureInfo.InvariantCulture));
        }

        private async Task<JArray> GetJsonArrayWithAuthRetryAsync(
            string url,
            string accessToken)
        {
            using HttpRequestMessage request =
                BuildRequest(
                    HttpMethod.Get,
                    url,
                    accessToken);

            using HttpResponseMessage response =
                await httpClient.SendAsync(request);

            string body =
                await response.Content.ReadAsStringAsync();

            if (response.StatusCode == HttpStatusCode.Unauthorized ||
                response.StatusCode == HttpStatusCode.Forbidden)
            {
                string? refreshed =
                    await sessionService.RefreshAccessTokenAsync();

                if (string.IsNullOrWhiteSpace(refreshed))
                {
                    throw new UnauthorizedAccessException(
                        "Supabase rejected the pull session and it could not be refreshed.");
                }

                using HttpRequestMessage retryRequest =
                    BuildRequest(
                        HttpMethod.Get,
                        url,
                        refreshed);

                using HttpResponseMessage retryResponse =
                    await httpClient.SendAsync(retryRequest);

                string retryBody =
                    await retryResponse.Content.ReadAsStringAsync();

                if (!retryResponse.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        $"Supabase pull returned HTTP {(int)retryResponse.StatusCode}: {TrimMessage(retryBody)}");
                }

                return JArray.Parse(
                    retryBody);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Supabase pull returned HTTP {(int)response.StatusCode}: {TrimMessage(body)}");
            }

            return JArray.Parse(
                body);
        }

        private async Task<bool> SyncCollectionAsync(
            SQLiteAsyncConnection connection,
            SyncQueueItem queueItem,
            string localAccountId,
            string authenticatedUserId,
            string accessToken)
        {
            CardCollection? collection =
                await connection.Table<CardCollection>()
                    .Where(row =>
                        row.Id == queueItem.EntityId)
                    .FirstOrDefaultAsync();

            if (collection == null)
            {
                throw new InvalidOperationException(
                    $"Local collection {queueItem.EntityId} no longer exists, so its cloud tombstone cannot be built.");
            }

            if (!string.Equals(
                    collection.OwnerUserAccountId,
                    localAccountId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "CollectIQ refused to sync a collection owned by a different local account.");
            }

            JObject payload =
                new JObject
                {
                    ["id"] = collection.Id,
                    ["owner_id"] =
                        authenticatedUserId,
                    ["name"] =
                        collection.Name ??
                        "My Collection",
                    ["description"] =
                        collection.Description ??
                        string.Empty,
                    ["visibility"] =
                        NormalizeVisibility(
                            collection.Visibility),
                    ["is_default"] =
                        collection.IsDefault,
                    ["is_deleted"] =
                        collection.IsDeleted,
                    ["created_at"] =
                        collection.CreatedUtc,
                    ["updated_at"] =
                        collection.UpdatedUtc,
                    ["deleted_at"] =
                        collection.IsDeleted
                            ? new JValue(
                                collection.UpdatedUtc)
                            : JValue.CreateNull()
                };

            await UpsertAsync(
                "collections",
                payload,
                accessToken);

            return true;
        }

        private async Task<bool> SyncCardAsync(
            SQLiteAsyncConnection connection,
            SyncQueueItem queueItem,
            string localAccountId,
            string authenticatedUserId,
            string accessToken)
        {
            Card? card =
                await connection.Table<Card>()
                    .Where(row =>
                        row.Id == queueItem.EntityId)
                    .FirstOrDefaultAsync();

            if (card == null)
            {
                throw new InvalidOperationException(
                    $"Local card {queueItem.EntityId} no longer exists, so its cloud tombstone cannot be built.");
            }

            CardCollection? collection =
                await connection.Table<CardCollection>()
                    .Where(row =>
                        row.Id == card.CollectionId)
                    .FirstOrDefaultAsync();

            if (collection == null)
            {
                throw new InvalidOperationException(
                    $"Card {card.Id} references missing local collection {card.CollectionId}.");
            }

            if (!string.Equals(
                    collection.OwnerUserAccountId,
                    localAccountId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "CollectIQ refused to sync a card from a collection owned by a different local account.");
            }

            CloudCardImageRefs cloudImageRefs =
                await imageStorageService.PrepareUploadAsync(
                    card,
                    authenticatedUserId,
                    accessToken);

            JObject payload =
                new JObject
                {
                    ["id"] = card.Id,
                    ["collection_id"] =
                        card.CollectionId,
                    ["owner_id"] =
                        authenticatedUserId,
                    ["title"] =
                        card.Title ??
                        string.Empty,
                    ["year"] =
                        card.Year.HasValue
                            ? new JValue(
                                card.Year.Value)
                            : JValue.CreateNull(),
                    ["card_set"] =
                        card.Set ??
                        string.Empty,
                    ["card_number"] =
                        card.Number ??
                        string.Empty,
                    ["purchase_price"] =
                        card.PurchasePrice.HasValue
                            ? new JValue(
                                card.PurchasePrice.Value)
                            : JValue.CreateNull(),
                    ["estimated_value"] =
                        card.EstimatedValue.HasValue
                            ? new JValue(
                                card.EstimatedValue.Value)
                            : JValue.CreateNull(),

                    // Storage URLs are intentionally handled in the next phase.
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

                    ["sport_value"] =
                        card.SportValue,
                    ["parallel"] =
                        card.Parallel ??
                        string.Empty,
                    ["subset"] =
                        card.Subset ??
                        string.Empty,
                    ["serial_number"] =
                        card.SerialNumber ??
                        string.Empty,
                    ["player_json"] =
                        ParseJsonObject(
                            card.PlayerJson),
                    ["team_json"] =
                        ParseJsonObject(
                            card.TeamJson),
                    ["grading_json"] =
                        ParseJsonObject(
                            card.GradingJson),
                    ["highlight_json"] =
                        ParseJsonObject(
                            card.HighlightJson),
                    ["is_deleted"] =
                        card.IsDeleted,
                    ["created_at"] =
                        card.CreatedUtc,
                    ["updated_at"] =
                        card.UpdatedUtc,
                    ["deleted_at"] =
                        card.IsDeleted
                            ? new JValue(
                                card.UpdatedUtc)
                            : JValue.CreateNull()
                };

            await UpsertAsync(
                "cards",
                payload,
                accessToken);

            return true;
        }

        private async Task UpsertAsync(
            string table,
            JObject payload,
            string accessToken)
        {
            string url =
                $"{SocialAuthSettings.GetNormalizedSupabaseUrl()}/rest/v1/{table}?on_conflict=id";

            using HttpRequestMessage request =
                BuildRequest(
                    HttpMethod.Post,
                    url,
                    accessToken);

            request.Headers.TryAddWithoutValidation(
                "Prefer",
                "resolution=merge-duplicates,return=minimal");

            request.Content =
                new StringContent(
                    new JArray(payload)
                        .ToString(Formatting.None),
                    Encoding.UTF8,
                    "application/json");

            using HttpResponseMessage response =
                await httpClient.SendAsync(
                    request);

            string body =
                await response.Content.ReadAsStringAsync();

            if (response.StatusCode == HttpStatusCode.Unauthorized ||
                response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new UnauthorizedAccessException(
                    $"Supabase authentication rejected the {table} sync with HTTP {(int)response.StatusCode}.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Supabase {table} sync returned HTTP {(int)response.StatusCode}: {TrimMessage(body)}");
            }
        }

        private static HttpRequestMessage BuildRequest(
            HttpMethod method,
            string url,
            string accessToken)
        {
            HttpRequestMessage request =
                new HttpRequestMessage(
                    method,
                    url);

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

        private static string NormalizeVisibility(
            string? visibility)
        {
            if (string.Equals(
                    visibility,
                    CollectionVisibility.Public,
                    StringComparison.OrdinalIgnoreCase))
            {
                return CollectionVisibility.Public;
            }

            if (string.Equals(
                    visibility,
                    CollectionVisibility.Shared,
                    StringComparison.OrdinalIgnoreCase))
            {
                return CollectionVisibility.Shared;
            }

            return CollectionVisibility.Private;
        }

        private static JToken ParseJsonObject(
            string? json)
        {
            if (string.IsNullOrWhiteSpace(
                    json))
            {
                return new JObject();
            }

            try
            {
                JToken token =
                    JToken.Parse(
                        json);

                return token.Type == JTokenType.Object
                    ? token
                    : new JObject();
            }
            catch
            {
                return new JObject();
            }
        }

        private static string JsonObjectToString(
            JToken? token)
        {
            if (token == null ||
                token.Type == JTokenType.Null ||
                token.Type == JTokenType.Undefined)
            {
                return "{}";
            }

            return token.Type == JTokenType.Object
                ? token.ToString(
                    Formatting.None)
                : "{}";
        }

        private static DateTime? ReadUtc(
            JToken? token)
        {
            string? raw =
                token?.Type == JTokenType.String
                    ? token.Value<string>()
                    : token?.ToString();

            if (string.IsNullOrWhiteSpace(
                    raw))
            {
                return null;
            }

            if (!DateTimeOffset.TryParse(
                    raw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal |
                    DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset parsed))
            {
                return null;
            }

            return parsed.UtcDateTime;
        }

        private static DateTime? MaxUtc(
            DateTime? left,
            DateTime? right)
        {
            if (!left.HasValue)
            {
                return right;
            }

            if (!right.HasValue)
            {
                return left;
            }

            return left.Value >= right.Value
                ? left
                : right;
        }

        private static string TrimMessage(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return "No response body";
            }

            string normalized =
                value.Replace(
                        '\r',
                        ' ')
                    .Replace(
                        '\n',
                        ' ')
                    .Trim();

            return normalized.Length <= 260
                ? normalized
                : normalized[..260] + "…";
        }

        private sealed class CloudPullBatch<T>
        {
            public List<T> Items { get; } =
                new List<T>();

            public int ProtectedByLocalPending { get; set; }

            public int SkippedAsStale { get; set; }

            public DateTime? NewestServerUpdateUtc { get; private set; }

            public void ObserveServerTimestamp(
                DateTime? value)
            {
                NewestServerUpdateUtc =
                    MaxUtc(
                        NewestServerUpdateUtc,
                        value);
            }
        }
    }
}
