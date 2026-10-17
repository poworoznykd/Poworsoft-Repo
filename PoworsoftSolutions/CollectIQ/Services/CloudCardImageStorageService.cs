using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using CollectIQ.Interfaces;
using CollectIQ.Models;
using CollectIQ.Models.Cloud;
using Microsoft.Maui.Storage;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace CollectIQ.Services
{
    /// <summary>
    /// Stores card images in a private Supabase Storage bucket.
    /// Supports CollectIQ local files and http/https image sources.
    /// </summary>
    public sealed class CloudCardImageStorageService : ICloudCardImageStorageService
    {
        private const string BucketName = "card-images";

        private readonly ISupabaseSessionService sessionService;
        private readonly HttpClient httpClient = new HttpClient();

        public string LastOperationSummary { get; private set; } =
            "No card image sync attempted yet.";

        public CloudCardImageStorageService(
            ISupabaseSessionService sessionService)
        {
            this.sessionService = sessionService;
        }

        public async Task<CloudCardImageRefs> PrepareUploadAsync(
            Card card,
            string authenticatedUserId,
            string accessToken)
        {
            CloudCardImageRefs refs =
                await GetExistingCloudRefsAsync(
                    card.Id,
                    accessToken);

            int uploaded = 0;
            int skipped = 0;
            int failed = 0;
            List<string> details = new List<string>();

            refs.Front =
                await UploadSourceAsync(
                    card.FrontImagePath,
                    BuildObjectPath(
                        authenticatedUserId,
                        card,
                        "front",
                        card.FrontImagePath),
                    refs.Front,
                    accessToken,
                    "front",
                    details,
                    () => uploaded++,
                    () => skipped++,
                    () => failed++);

            string? thumbnailSource =
                GetThumbnailSource(card);

            refs.FrontThumbnail =
                await UploadSourceAsync(
                    thumbnailSource,
                    BuildObjectPath(
                        authenticatedUserId,
                        card,
                        "front-thumb",
                        thumbnailSource),
                    refs.FrontThumbnail,
                    accessToken,
                    "front thumbnail",
                    details,
                    () => uploaded++,
                    () => skipped++,
                    () => failed++);

            refs.Back =
                await UploadSourceAsync(
                    card.BackImagePath,
                    BuildObjectPath(
                        authenticatedUserId,
                        card,
                        "back",
                        card.BackImagePath),
                    refs.Back,
                    accessToken,
                    "back",
                    details,
                    () => uploaded++,
                    () => skipped++,
                    () => failed++);

            refs.FrontOverlay =
                await UploadSourceAsync(
                    card.FrontOverlayImagePath,
                    BuildObjectPath(
                        authenticatedUserId,
                        card,
                        "front-overlay",
                        card.FrontOverlayImagePath),
                    refs.FrontOverlay,
                    accessToken,
                    "front overlay",
                    details,
                    () => uploaded++,
                    () => skipped++,
                    () => failed++);

            refs.BackOverlay =
                await UploadSourceAsync(
                    card.BackOverlayImagePath,
                    BuildObjectPath(
                        authenticatedUserId,
                        card,
                        "back-overlay",
                        card.BackOverlayImagePath),
                    refs.BackOverlay,
                    accessToken,
                    "back overlay",
                    details,
                    () => uploaded++,
                    () => skipped++,
                    () => failed++);

            string detailText =
                details.Count == 0
                    ? string.Empty
                    : " " + string.Join(" ", details.Take(3));

            LastOperationSummary =
                $"Image sync for '{card.Title}': {uploaded} uploaded, {skipped} unavailable, {failed} failed.{detailText}";

            Debug.WriteLine(
                $"[CollectIQ Storage] {LastOperationSummary}");

            return refs;
        }

        public async Task PopulateLocalImagesFromCloudAsync(
            Card card,
            CloudCardImageRefs cloudRefs,
            string accessToken)
        {
            if (cloudRefs == null)
            {
                return;
            }

            card.FrontImagePath =
                await DownloadOrPreserveAsync(
                    cloudRefs.Front,
                    card.Id,
                    "front",
                    card.FrontImagePath,
                    accessToken);

            card.FrontThumbnailPath =
                await DownloadOrPreserveAsync(
                    cloudRefs.FrontThumbnail,
                    card.Id,
                    "front-thumb",
                    card.FrontThumbnailPath,
                    accessToken);

            card.BackImagePath =
                await DownloadOrPreserveAsync(
                    cloudRefs.Back,
                    card.Id,
                    "back",
                    card.BackImagePath,
                    accessToken) ?? string.Empty;

            card.FrontOverlayImagePath =
                await DownloadOrPreserveAsync(
                    cloudRefs.FrontOverlay,
                    card.Id,
                    "front-overlay",
                    card.FrontOverlayImagePath,
                    accessToken) ?? string.Empty;

            card.BackOverlayImagePath =
                await DownloadOrPreserveAsync(
                    cloudRefs.BackOverlay,
                    card.Id,
                    "back-overlay",
                    card.BackOverlayImagePath,
                    accessToken) ?? string.Empty;
        }

        private async Task<CloudCardImageRefs> GetExistingCloudRefsAsync(
            string cardId,
            string accessToken)
        {
            if (string.IsNullOrWhiteSpace(cardId))
            {
                return new CloudCardImageRefs();
            }

            string encodedId =
                Uri.EscapeDataString(cardId);

            string url =
                $"{SocialAuthSettings.GetNormalizedSupabaseUrl()}/rest/v1/cards" +
                $"?id=eq.{encodedId}" +
                "&select=front_image_url,front_thumbnail_url,back_image_url,front_overlay_image_url,back_overlay_image_url";

            using HttpRequestMessage request =
                BuildAuthorizedRequest(
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
                    return new CloudCardImageRefs();
                }

                return await GetExistingCloudRefsAsync(
                    cardId,
                    refreshed);
            }

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine(
                    $"[CollectIQ Storage] Existing image lookup failed: {(int)response.StatusCode} {body}");
                return new CloudCardImageRefs();
            }

            JArray rows =
                JArray.Parse(body);

            if (rows.Count != 1 ||
                rows[0] is not JObject row)
            {
                return new CloudCardImageRefs();
            }

            return FromRow(row);
        }

        private async Task<string> UploadSourceAsync(
            string? source,
            string objectPath,
            string existingCloudPath,
            string accessToken,
            string label,
            List<string> details,
            Action markUploaded,
            Action markSkipped,
            Action markFailed)
        {
            ImageSourceBytes? image =
                await ResolveImageSourceAsync(source);

            if (image == null ||
                image.Bytes.Length == 0)
            {
                markSkipped();
                details.Add(
                    $"{label}: source not found.");
                return existingCloudPath ?? string.Empty;
            }

            image =
                await OptimizeForCloudAsync(
                    image,
                    label);

            try
            {
                HttpResponseMessage response =
                    await UploadBytesAsync(
                        image.Bytes,
                        image.ContentType,
                        objectPath,
                        accessToken);

                using (response)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized ||
                        response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        string? refreshed =
                            await sessionService.RefreshAccessTokenAsync();

                        if (string.IsNullOrWhiteSpace(refreshed))
                        {
                            markFailed();
                            details.Add(
                                $"{label}: auth refresh failed.");
                            return existingCloudPath ?? string.Empty;
                        }

                        using HttpResponseMessage retry =
                            await UploadBytesAsync(
                                image.Bytes,
                                image.ContentType,
                                objectPath,
                                refreshed);

                        if (!retry.IsSuccessStatusCode)
                        {
                            string retryBody =
                                await retry.Content.ReadAsStringAsync();

                            markFailed();
                            details.Add(
                                $"{label}: HTTP {(int)retry.StatusCode} {Trim(retryBody)}");
                            return existingCloudPath ?? string.Empty;
                        }

                        markUploaded();
                        return objectPath;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        string body =
                            await response.Content.ReadAsStringAsync();

                        markFailed();
                        details.Add(
                            $"{label}: HTTP {(int)response.StatusCode} {Trim(body)}");
                        return existingCloudPath ?? string.Empty;
                    }
                }

                markUploaded();
                return objectPath;
            }
            catch (Exception ex)
            {
                markFailed();
                details.Add(
                    $"{label}: {ex.Message}");
                Debug.WriteLine(
                    $"[CollectIQ Storage] Upload exception for {source}: {ex}");
                return existingCloudPath ?? string.Empty;
            }
        }

        private async Task<ImageSourceBytes?> ResolveImageSourceAsync(
            string? source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return null;
            }

            string candidate =
                source.Trim();

            if (candidate.StartsWith(
                    "file://",
                    StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(
                    candidate,
                    UriKind.Absolute,
                    out Uri? fileUri))
            {
                candidate =
                    fileUri.LocalPath;
            }

            if (File.Exists(candidate))
            {
                return new ImageSourceBytes(
                    await File.ReadAllBytesAsync(candidate),
                    GetContentType(candidate));
            }

            if (!Path.IsPathRooted(candidate))
            {
                string appDataCandidate =
                    Path.Combine(
                        FileSystem.AppDataDirectory,
                        candidate);

                if (File.Exists(appDataCandidate))
                {
                    return new ImageSourceBytes(
                        await File.ReadAllBytesAsync(appDataCandidate),
                        GetContentType(appDataCandidate));
                }

                string cacheCandidate =
                    Path.Combine(
                        FileSystem.CacheDirectory,
                        candidate);

                if (File.Exists(cacheCandidate))
                {
                    return new ImageSourceBytes(
                        await File.ReadAllBytesAsync(cacheCandidate),
                        GetContentType(cacheCandidate));
                }
            }

            if (Uri.TryCreate(
                    candidate,
                    UriKind.Absolute,
                    out Uri? remoteUri) &&
                (remoteUri.Scheme == Uri.UriSchemeHttp ||
                 remoteUri.Scheme == Uri.UriSchemeHttps))
            {
                using HttpRequestMessage request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        remoteUri);

                using HttpResponseMessage response =
                    await httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                byte[] bytes =
                    await response.Content.ReadAsByteArrayAsync();

                string contentType =
                    response.Content.Headers.ContentType?.MediaType ??
                    GetContentType(remoteUri.AbsolutePath);

                return new ImageSourceBytes(
                    bytes,
                    contentType);
            }

            return null;
        }

        private static string? GetThumbnailSource(
            Card card)
        {
            if (!string.IsNullOrWhiteSpace(
                    card.FrontThumbnailPath))
            {
                return card.FrontThumbnailPath;
            }

            return card.FrontImagePath;
        }

        private static async Task<ImageSourceBytes> OptimizeForCloudAsync(
            ImageSourceBytes source,
            string label)
        {
            try
            {
                int maxDimension;
                int jpegQuality;

                if (label.Contains(
                        "thumbnail",
                        StringComparison.OrdinalIgnoreCase))
                {
                    maxDimension = 360;
                    jpegQuality = 72;
                }
                else if (label.Contains(
                             "overlay",
                             StringComparison.OrdinalIgnoreCase))
                {
                    maxDimension = 1200;
                    jpegQuality = 80;
                }
                else
                {
                    maxDimension = 1600;
                    jpegQuality = 82;
                }

                using SixLabors.ImageSharp.Image image =
                    SixLabors.ImageSharp.Image.Load(
                        source.Bytes);

                if (image.Width > maxDimension ||
                    image.Height > maxDimension)
                {
                    image.Mutate(
                        context =>
                            context.Resize(
                                new ResizeOptions
                                {
                                    Mode = SixLabors.ImageSharp.Processing.ResizeMode.Max,
                                    Size = new SixLabors.ImageSharp.Size(
                                        maxDimension,
                                        maxDimension)
                                }));
                }

                await using MemoryStream output =
                    new MemoryStream();

                bool preservePng =
                    string.Equals(
                        source.ContentType,
                        "image/png",
                        StringComparison.OrdinalIgnoreCase);

                if (preservePng)
                {
                    await image.SaveAsPngAsync(
                        output);

                    return new ImageSourceBytes(
                        output.ToArray(),
                        "image/png");
                }

                await image.SaveAsJpegAsync(
                    output,
                    new JpegEncoder
                    {
                        Quality = jpegQuality
                    });

                return new ImageSourceBytes(
                    output.ToArray(),
                    "image/jpeg");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[CollectIQ Storage] Image optimization skipped: {ex.Message}");

                return source;
            }
        }

        private async Task<HttpResponseMessage> UploadBytesAsync(
            byte[] bytes,
            string contentType,
            string objectPath,
            string accessToken)
        {
            string url =
                $"{SocialAuthSettings.GetNormalizedSupabaseUrl()}/storage/v1/object/{BucketName}/{EncodeObjectPath(objectPath)}";

            HttpRequestMessage request =
                BuildAuthorizedRequest(
                    HttpMethod.Post,
                    url,
                    accessToken);

            request.Headers.TryAddWithoutValidation(
                "x-upsert",
                "true");

            ByteArrayContent content =
                new ByteArrayContent(bytes);

            content.Headers.ContentType =
                new MediaTypeHeaderValue(
                    string.IsNullOrWhiteSpace(contentType)
                        ? "image/jpeg"
                        : contentType);

            request.Content =
                content;

            try
            {
                return await httpClient.SendAsync(
                    request);
            }
            finally
            {
                request.Dispose();
            }
        }

        private async Task<string?> DownloadOrPreserveAsync(
            string cloudObjectPath,
            string cardId,
            string roleName,
            string? existingLocalPath,
            string accessToken)
        {
            if (string.IsNullOrWhiteSpace(cloudObjectPath))
            {
                return existingLocalPath;
            }

            try
            {
                byte[]? bytes =
                    await DownloadBytesAsync(
                        cloudObjectPath,
                        accessToken);

                if (bytes == null ||
                    bytes.Length == 0)
                {
                    return existingLocalPath;
                }

                string extension =
                    GetSafeExtension(
                        cloudObjectPath);

                string directory =
                    Path.Combine(
                        FileSystem.AppDataDirectory,
                        "CloudCardImages",
                        SanitizeSegment(cardId));

                Directory.CreateDirectory(
                    directory);

                string localPath =
                    Path.Combine(
                        directory,
                        $"{roleName}{extension}");

                await File.WriteAllBytesAsync(
                    localPath,
                    bytes);

                return localPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[CollectIQ Storage] Download exception for {cloudObjectPath}: {ex}");
                return existingLocalPath;
            }
        }

        private async Task<byte[]?> DownloadBytesAsync(
            string objectPath,
            string accessToken)
        {
            string url =
                $"{SocialAuthSettings.GetNormalizedSupabaseUrl()}/storage/v1/object/authenticated/{BucketName}/{EncodeObjectPath(objectPath)}";

            using HttpRequestMessage request =
                BuildAuthorizedRequest(
                    HttpMethod.Get,
                    url,
                    accessToken);

            using HttpResponseMessage response =
                await httpClient.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.Unauthorized ||
                response.StatusCode == HttpStatusCode.Forbidden)
            {
                string? refreshed =
                    await sessionService.RefreshAccessTokenAsync();

                if (string.IsNullOrWhiteSpace(refreshed))
                {
                    return null;
                }

                using HttpRequestMessage retryRequest =
                    BuildAuthorizedRequest(
                        HttpMethod.Get,
                        url,
                        refreshed);

                using HttpResponseMessage retryResponse =
                    await httpClient.SendAsync(retryRequest);

                if (!retryResponse.IsSuccessStatusCode)
                {
                    return null;
                }

                return await retryResponse.Content.ReadAsByteArrayAsync();
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync();
        }

        private static string BuildObjectPath(
            string authenticatedUserId,
            Card card,
            string roleName,
            string? source)
        {
            string extension =
                GetCloudExtension(source);

            return string.Join(
                "/",
                SanitizeSegment(authenticatedUserId),
                SanitizeSegment(card.CollectionId),
                SanitizeSegment(card.Id),
                roleName + extension);
        }

        private static string EncodeObjectPath(
            string objectPath)
        {
            return string.Join(
                "/",
                (objectPath ?? string.Empty)
                    .Split(
                        '/',
                        StringSplitOptions.RemoveEmptyEntries)
                    .Select(
                        Uri.EscapeDataString));
        }

        private static string SanitizeSegment(
            string? value)
        {
            string source =
                value ?? string.Empty;

            char[] chars =
                source
                    .Where(ch =>
                        char.IsLetterOrDigit(ch) ||
                        ch == '-' ||
                        ch == '_')
                    .ToArray();

            return chars.Length == 0
                ? "item"
                : new string(chars);
        }

        private static string GetCloudExtension(
            string? path)
        {
            string extension =
                GetSafeExtension(path);

            return string.Equals(
                    extension,
                    ".png",
                    StringComparison.OrdinalIgnoreCase)
                ? ".png"
                : ".jpg";
        }

        private static string GetSafeExtension(
            string? path)
        {
            string extension;

            if (Uri.TryCreate(
                    path,
                    UriKind.Absolute,
                    out Uri? uri) &&
                (uri.Scheme == Uri.UriSchemeHttp ||
                 uri.Scheme == Uri.UriSchemeHttps))
            {
                extension =
                    Path.GetExtension(
                        uri.AbsolutePath)
                        .ToLowerInvariant();
            }
            else
            {
                extension =
                    Path.GetExtension(
                        path ?? string.Empty)
                        .ToLowerInvariant();
            }

            return extension switch
            {
                ".jpg" => ".jpg",
                ".jpeg" => ".jpg",
                ".png" => ".png",
                ".webp" => ".webp",
                _ => ".jpg"
            };
        }

        private static string GetContentType(
            string path)
        {
            string extension =
                Path.GetExtension(path)
                    .ToLowerInvariant();

            return extension switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };
        }

        private static HttpRequestMessage BuildAuthorizedRequest(
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

            return request;
        }

        private static string Trim(
            string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "no response body";
            }

            string normalized =
                value.Replace('\r', ' ')
                    .Replace('\n', ' ')
                    .Trim();

            return normalized.Length <= 140
                ? normalized
                : normalized[..140] + "…";
        }

        public static CloudCardImageRefs FromRow(
            JObject row)
        {
            return new CloudCardImageRefs
            {
                Front =
                    row.Value<string>("front_image_url") ??
                    string.Empty,
                FrontThumbnail =
                    row.Value<string>("front_thumbnail_url") ??
                    string.Empty,
                Back =
                    row.Value<string>("back_image_url") ??
                    string.Empty,
                FrontOverlay =
                    row.Value<string>("front_overlay_image_url") ??
                    string.Empty,
                BackOverlay =
                    row.Value<string>("back_overlay_image_url") ??
                    string.Empty
            };
        }

        private sealed record ImageSourceBytes(
            byte[] Bytes,
            string ContentType);
    }
}
