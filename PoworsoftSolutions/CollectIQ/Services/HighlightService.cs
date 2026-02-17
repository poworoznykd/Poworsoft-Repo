/*
* FILE            : HighlightService.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2025-12-12
* DESCRIPTION     :
*     Service responsible for locating professional highlight reels
*     (e.g., YouTube videos) for a given player or Pokémon character.
*
*     This service currently uses the YouTube Data API v3 to:
*         1. Search for videos based on a query string.
*         2. Retrieve video details (duration, title).
*         3. Build a HighlightReel composed of HighlightClip objects.
*
*     IMPORTANT:
*         - You MUST supply a valid YouTube Data API key.
*         - The YouTube Data API v3 must be enabled for your
*           Google Cloud project.
*/

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CollectIQ.Models;

namespace CollectIQ.Services
{
    /// <summary>
    /// Service that queries YouTube for player or character highlight reels.
    /// </summary>
    public sealed class HighlightService
    {
        // ------------------------------------------------------------------
        //   CONSTANTS / CONFIGURATION
        // ------------------------------------------------------------------

        private const string YouTubeSearchEndpoint = "https://www.googleapis.com/youtube/v3/search";
        private const string YouTubeVideosEndpoint = "https://www.googleapis.com/youtube/v3/videos";
        private const string YouTubeApiKey = "AIzaSyDaPzLbHdz6wzeZPL8XtaO5gR9Hk6n2yUo";


        private readonly HttpClient httpClient;

        // ------------------------------------------------------------------
        //   CONSTRUCTORS
        // ------------------------------------------------------------------

        /*
         * FUNCTION     : HighlightService
         * DESCRIPTION  :
         *     Default constructor which instantiates a private HttpClient
         *     for use by this service. This is sufficient for mobile usage
         *     where the service is created once per page or via DI.
         * PARAMETERS   :
         *     none
         * RETURNS      :
         *     none
         */
        public HighlightService()
        {
            httpClient = new HttpClient();
        }

        /*
         * FUNCTION     : HighlightService
         * DESCRIPTION  :
         *     Overloaded constructor allowing a pre-configured HttpClient
         *     to be injected (e.g., from dependency injection, unit tests,
         *     or a shared HttpClient factory).
         * PARAMETERS   :
         *     client  - external HttpClient instance to use.
         * RETURNS      :
         *     none
         */
        public HighlightService(HttpClient client)
        {
            httpClient = client ?? new HttpClient();
        }

        // ------------------------------------------------------------------
        //   PUBLIC METHODS
        // ------------------------------------------------------------------

        /*
         * FUNCTION     : FindHighlightReelAsync
         * DESCRIPTION  :
         *     Calls the YouTube Data API v3 to find an appropriate highlight
         *     reel for the supplied search query. It:
         *         1. Calls the "search" endpoint for top videos.
         *         2. Calls the "videos" endpoint for details (duration).
         *         3. Returns a HighlightReel containing candidate clips,
         *            preferring those in the 2–5 minute range.
         * PARAMETERS   :
         *     searchQuery     - human-readable string to search for
         *                       (e.g., "Josh Allen highlights 2020").
         *     cancellationToken - optional token to cancel the request.
         * RETURNS      :
         *     Task<HighlightReel> - a reel containing zero or more clips.
         *                           If searchQuery is empty, an empty reel
         *                           is returned.
         */
        public async Task<HighlightReel> FindHighlightReelAsync(
            string searchQuery,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(searchQuery))
            {
                return new HighlightReel();
            }

            if (string.IsNullOrWhiteSpace(YouTubeApiKey) ||
                YouTubeApiKey.Contains("PUT_YOUR_REAL_YOUTUBE_API_KEY_HERE", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "YouTube API key is not configured. " +
                    "Update HighlightService.YouTubeApiKey with a valid key.");
            }

            string encodedQuery = Uri.EscapeDataString(searchQuery);

            // --------------------------------------------------------------
            // 1) SEARCH FOR VIDEOS BY QUERY
            // --------------------------------------------------------------
            string searchUrl =
                 $"{YouTubeSearchEndpoint}" +
                 $"?part=snippet" +
                 $"&type=video" +
                 $"&maxResults=10" +
                 $"&order=relevance" +
                 $"&videoEmbeddable=true" +
                 $"&videoDuration=medium" +        // bias toward 4–20 min
                 $"&safeSearch=moderate" +
                 $"&relevanceLanguage=en" +
                 $"&regionCode=CA" +               // you’re in Canada; tweak if you want
                 $"&key={YouTubeApiKey}" +
                 $"&q={encodedQuery}";


            HttpResponseMessage searchResponse =
                await httpClient.GetAsync(searchUrl, cancellationToken)
                                .ConfigureAwait(false);

            string searchPayload =
                await searchResponse.Content.ReadAsStringAsync(cancellationToken)
                               .ConfigureAwait(false);

            if (!searchResponse.IsSuccessStatusCode)
            {
                string message =
                    $"YouTube search failed ({(int)searchResponse.StatusCode} - {searchResponse.ReasonPhrase}). " +
                    $"Payload: {searchPayload}";

                throw new HttpRequestException(message);
            }

            List<string> videoIds = ExtractVideoIdsFromSearchPayload(searchPayload);
            if (videoIds.Count == 0)
            {
                // No hits; just return an empty reel.
                return new HighlightReel();
            }

            // --------------------------------------------------------------
            // 2) GET VIDEO DETAILS (DURATION, TITLE) FOR CANDIDATES
            // --------------------------------------------------------------
            string idList = string.Join(",", videoIds);

            string videosUrl =
                $"{YouTubeVideosEndpoint}" +
                $"?part=contentDetails,snippet" +
                $"&id={idList}" +
                $"&key={YouTubeApiKey}";

            HttpResponseMessage videosResponse =
                await httpClient.GetAsync(videosUrl, cancellationToken)
                                .ConfigureAwait(false);

            string videosPayload =
                await videosResponse.Content.ReadAsStringAsync(cancellationToken)
                                 .ConfigureAwait(false);

            if (!videosResponse.IsSuccessStatusCode)
            {
                string message =
                    $"YouTube video details failed ({(int)videosResponse.StatusCode} - {videosResponse.ReasonPhrase}). " +
                    $"Payload: {videosPayload}";

                throw new HttpRequestException(message);
            }

            HighlightReel reel = BuildHighlightReelFromVideoPayload(videosPayload);
            return reel;
        }

        // ------------------------------------------------------------------
        //   PRIVATE HELPERS
        // ------------------------------------------------------------------

        /*
         * FUNCTION     : ExtractVideoIdsFromSearchPayload
         * DESCRIPTION  :
         *     Parses the JSON payload from the YouTube "search" endpoint
         *     and extracts the list of video IDs.
         * PARAMETERS   :
         *     searchPayload - raw JSON payload from the search call.
         * RETURNS      :
         *     List<string>  - collection of video IDs, or an empty list if
         *                     none are found.
         */
        private static List<string> ExtractVideoIdsFromSearchPayload(string searchPayload)
        {
            List<string> videoIds = new List<string>();

            using JsonDocument document = JsonDocument.Parse(searchPayload);

            if (!document.RootElement.TryGetProperty("items", out JsonElement itemsElement) ||
                itemsElement.ValueKind != JsonValueKind.Array)
            {
                return videoIds;
            }

            foreach (JsonElement item in itemsElement.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out JsonElement idElement))
                {
                    continue;
                }

                if (!idElement.TryGetProperty("videoId", out JsonElement videoIdElement))
                {
                    continue;
                }

                string videoId = videoIdElement.GetString();
                if (!string.IsNullOrWhiteSpace(videoId))
                {
                    videoIds.Add(videoId);
                }
            }

            return videoIds;
        }

        /*
         * FUNCTION     : BuildHighlightReelFromVideoPayload
         * DESCRIPTION  :
         *     Parses the JSON payload from the YouTube "videos" endpoint
         *     and builds a HighlightReel. It prefers videos whose durations
         *     are in roughly the 2–5 minute range, but will still include
         *     other videos if those are not available.
         * PARAMETERS   :
         *     videosPayload - raw JSON payload from the videos call.
         * RETURNS      :
         *     HighlightReel - reel containing candidate HighlightClip items.
         */
        private static HighlightReel BuildHighlightReelFromVideoPayload(string videosPayload)
        {
            HighlightReel reel = new HighlightReel();

            using JsonDocument document = JsonDocument.Parse(videosPayload);

            if (!document.RootElement.TryGetProperty("items", out JsonElement itemsElement) ||
                itemsElement.ValueKind != JsonValueKind.Array)
            {
                return reel;
            }

            List<(HighlightClip Clip, double Score)> scoredClips = new List<(HighlightClip, double)>();

            foreach (JsonElement item in itemsElement.EnumerateArray())
            {
                string videoId = item.GetProperty("id").GetString();
                if (string.IsNullOrWhiteSpace(videoId))
                {
                    continue;
                }

                string title = item.GetProperty("snippet").GetProperty("title").GetString();
                string durationIso =
                    item.GetProperty("contentDetails").GetProperty("duration").GetString();

                TimeSpan? duration = TryParseIso8601Duration(durationIso);

                double score = ScoreDuration(duration);
                string videoUrl = $"https://www.youtube.com/watch?v={videoId}";

                HighlightClip clip = new HighlightClip
                {
                    VideoUrl = videoUrl,
                    Description = title,
                    StartTimestamp = null
                };

                scoredClips.Add((clip, score));
            }

            // Sort by score (higher is better) and take the top few.
            scoredClips.Sort((a, b) => b.Score.CompareTo(a.Score));

            foreach ((HighlightClip clip, double _) in scoredClips)
            {
                reel.Clips.Add(clip);
            }

            return reel;
        }

        /*
         * FUNCTION     : TryParseIso8601Duration
         * DESCRIPTION  :
         *     Attempts to parse an ISO 8601 duration string (e.g., "PT3M12S")
         *     into a TimeSpan.
         * PARAMETERS   :
         *     durationIso - ISO 8601 duration string from YouTube.
         * RETURNS      :
         *     TimeSpan?   - parsed duration, or null if parsing fails.
         */
        private static TimeSpan? TryParseIso8601Duration(string durationIso)
        {
            if (string.IsNullOrWhiteSpace(durationIso))
            {
                return null;
            }

            try
            {
                // System.Xml may be used here (XmlConvert.ToTimeSpan), but to
                // avoid adding extra dependencies we manually handle the most
                // common PT#M#S formats later if needed.
                return System.Xml.XmlConvert.ToTimeSpan(durationIso);
            }
            catch
            {
                return null;
            }
        }

        /*
         * FUNCTION     : ScoreDuration
         * DESCRIPTION  :
         *     Scores a duration based on how close it is to the preferred
         *     highlight length (roughly 3.5 minutes, within a 2–5 minute
         *     window). Clips with unknown duration receive a low score but
         *     are still allowed.
         * PARAMETERS   :
         *     duration - optional TimeSpan retrieved from YouTube.
         * RETURNS      :
         *     double   - a relative score (higher is better).
         */
        private static double ScoreDuration(TimeSpan? duration)
        {
            if (!duration.HasValue)
            {
                return 0.1;   // Unknown duration: still usable, low confidence.
            }

            double minutes = duration.Value.TotalMinutes;
            const double targetMinutes = 3.5;

            // Penalize videos that are extremely short or extremely long.
            if (minutes < 1.0 || minutes > 10.0)
            {
                return 0.2;
            }

            // Ideal is around 3.5 minutes; use a simple inverse distance.
            double distance = Math.Abs(minutes - targetMinutes);
            return 1.0 / (1.0 + distance);
        }
    }
}
