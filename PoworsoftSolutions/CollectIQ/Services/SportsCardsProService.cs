using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CollectIQ.Models.SportsCardsPro;
using Microsoft.Maui.Storage;

namespace CollectIQ.Services
{
    // One service. One responsibility: call SportsCardsPro/PriceCharting API and return parsed models.
    public class SportsCardsProService
    {
        private const string SecureJsonPath = "secure.json";

        private readonly string[] bases = new[]
        {
        "https://www.sportscardspro.com",
        "https://www.pricecharting.com"
    };

        private static readonly SemaphoreSlim throttleLock = new SemaphoreSlim(1, 1);
        private static DateTime lastRequestUtc = DateTime.MinValue;

        private readonly HttpClient http;
        private readonly JsonSerializerOptions jsonOptions;

        private string token;

        public SportsCardsProService(HttpClient httpClient = null)
        {
            var handler = new HttpClientHandler
            {
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            http = httpClient ?? new HttpClient(handler);

            jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
        }

        public async Task InitializeAsync()
        {
            if (!string.IsNullOrWhiteSpace(token))
                return;

            token = await LoadTokenFromSecureJsonAsync();

            if (string.IsNullOrWhiteSpace(token))
                Debug.WriteLine("[SportsCardsProService] Token missing. secure.json must contain SPORTS_CARDSPRO_TOKEN.");
        }

        //public async Task<SportCardsProItem> GetProductAsync(string idOrQuery, CancellationToken cancellationToken = default)
        //{
        //    if (string.IsNullOrWhiteSpace(idOrQuery))
        //    {
        //        return null;
        //    }

        //    await InitializeAsync();

        //    // If it's all digits, treat it as an ID. Otherwise treat it as a search query.
        //    bool looksLikeId = idOrQuery.All(char.IsDigit);

        //    if (looksLikeId)
        //    {
        //        return await GetByIdAsync(idOrQuery, cancellationToken);
        //    }

        //    return await GetBestMatchAsync(idOrQuery, cancellationToken);
        //}

        // This is the call you use from CollectIQ when you only have a query string.
        // It searches, picks the best candidate, fetches /api/product, and returns an item for Insights.
        public async Task<SportCardsProItem> GetBestMatchAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;

            await InitializeAsync();

            if (string.IsNullOrWhiteSpace(token))
                return null;

            SportsCardsProSearchResponse search = await SearchAsync(query, cancellationToken);

            if (search == null || !search.IsSuccess || search.Products == null || search.Products.Count == 0)
                return null;

            // Don’t assume first result is best.
            // Pull details for the top few and pick the one with the richest pricing.
            int probeCount = Math.Min(5, search.Products.Count);

            SportCardsProPricesSnapshot bestSnapshot = null;
            int bestScore = -1;

            for (int i = 0; i < probeCount; i++)
            {
                string id = search.Products[i].Id;

                if (string.IsNullOrWhiteSpace(id))
                    continue;

                SportCardsProPricesSnapshot snap = await GetProductByIdAsync(id, cancellationToken);

                if (snap == null || !snap.IsSuccess)
                    continue;

                int score = ScoreSnapshot(snap);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestSnapshot = snap;
                }
            }

            if (bestSnapshot == null)
                return null;

            return BuildItem(bestSnapshot);
        }

        public async Task<SportsCardsProSearchResponse> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;

            await InitializeAsync();

            if (string.IsNullOrWhiteSpace(token))
                return null;

            string path =
                "/api/products?t=" + UrlEncoder.Default.Encode(token) +
                "&q=" + UrlEncoder.Default.Encode(query);

            string json = await GetFirstOkJsonAsync(path, cancellationToken);

            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonSerializer.Deserialize<SportsCardsProSearchResponse>(json, jsonOptions);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[SportsCardsProService] Search parse failed: " + ex.Message);
                return null;
            }
        }

        public async Task<SportCardsProPricesSnapshot> GetProductByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            await InitializeAsync();

            if (string.IsNullOrWhiteSpace(token))
                return null;

            string path =
                "/api/product?t=" + UrlEncoder.Default.Encode(token) +
                "&id=" + UrlEncoder.Default.Encode(id);

            string json = await GetFirstOkJsonAsync(path, cancellationToken);

            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonSerializer.Deserialize<SportCardsProPricesSnapshot>(json, jsonOptions);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[SportsCardsProService] Product parse failed: " + ex.Message);
                Debug.WriteLine("[SportsCardsProService] Raw JSON preview: " + json.Substring(0, Math.Min(json.Length, 900)));
                return null;
            }
        }

        private SportCardsProItem BuildItem(SportCardsProPricesSnapshot snapshot)
        {
            var item = new SportCardsProItem
            {
                CardSnapShot = snapshot,
                ItemPageUrl = BuildItemUrl(snapshot),
                ImageUrl = null
            };

            return item;
        }

        private string BuildItemUrl(SportCardsProPricesSnapshot snapshot)
        {
            if (!string.IsNullOrWhiteSpace(snapshot?.Id))
                return bases[0] + "/product/" + snapshot.Id;

            return bases[0];
        }

        private static int ScoreSnapshot(SportCardsProPricesSnapshot s)
        {
            int score = 0;

            // Base prices
            if (s.LoosePrice.HasValue) score += 2;
            if (s.NewPrice.HasValue) score += 2;
            if (s.CibPrice.HasValue) score += 2;

            // Retail spread (useful for insights)
            if (s.RetailLooseBuy.HasValue || s.RetailLooseSell.HasValue) score += 1;
            if (s.RetailNewBuy.HasValue || s.RetailNewSell.HasValue) score += 1;
            if (s.RetailCibBuy.HasValue || s.RetailCibSell.HasValue) score += 1;

            // Optional grading keys if they exist (some items)
            if (s.GradedPrice.HasValue) score += 2;
            if (s.ManualOnlyPrice.HasValue) score += 2;
            if (s.Bgs10Price.HasValue) score += 1;
            if (s.Condition9Price.HasValue) score += 1;
            if (s.Condition10Price.HasValue) score += 1;
            if (s.Condition13Price.HasValue) score += 1;
            if (s.Condition14Price.HasValue) score += 1;
            if (s.Condition15Price.HasValue) score += 1;
            if (s.Condition16Price.HasValue) score += 1;
            if (s.Condition17Price.HasValue) score += 1;
            if (s.Condition18Price.HasValue) score += 1;
            if (s.Condition19Price.HasValue) score += 1;
            if (s.Condition20Price.HasValue) score += 1;
            if (s.Condition21Price.HasValue) score += 1;
            if (s.Condition22Price.HasValue) score += 1;

            // Sales volume is a confidence signal
            if (s.SalesVolume.HasValue) 
                    score += 1;

            return score;
        }

        public static decimal? GetPriceForGrade(
            SportCardsProPricesSnapshot? snapshot,
            SportsCardsProGradeOption? gradeOption)
        {
            if (snapshot == null)
                return null;

            gradeOption ??= SportsCardsProGradeCatalog.Ungraded;

            long? pennies = gradeOption.ApiPriceKey switch
            {
                "loose-price" => snapshot.LoosePrice,
                "condition-9-price" => snapshot.Condition9Price,
                "condition-10-price" => snapshot.Condition10Price,
                "condition-13-price" => snapshot.Condition13Price,
                "condition-14-price" => snapshot.Condition14Price,
                "condition-15-price" => snapshot.Condition15Price,
                "condition-16-price" => snapshot.Condition16Price,
                "cib-price" => snapshot.CibPrice,
                "new-price" => snapshot.NewPrice,
                "graded-price" => snapshot.GradedPrice,
                "box-only-price" => snapshot.BoxOnlyPrice,
                "manual-only-price" => snapshot.ManualOnlyPrice,
                "bgs-10-price" => snapshot.Bgs10Price,
                "condition-17-price" => snapshot.Condition17Price,
                "condition-18-price" => snapshot.Condition18Price,
                "condition-19-price" => snapshot.Condition19Price,
                "condition-20-price" => snapshot.Condition20Price,
                "condition-21-price" => snapshot.Condition21Price,
                "condition-22-price" => snapshot.Condition22Price,
                _ => null
            };

            return pennies.HasValue && pennies.Value > 0
                ? pennies.Value / 100m
                : null;
        }

        public async Task<decimal?> GetBestMatchPriceForGradeAsync(
            string query,
            SportsCardsProGradeOption gradeOption,
            CancellationToken cancellationToken = default)
        {
            SportCardsProItem? item = await GetBestMatchAsync(query, cancellationToken);
            return GetPriceForGrade(item?.CardSnapShot, gradeOption);
        }

        private async Task<string> GetFirstOkJsonAsync(string path, CancellationToken cancellationToken)
        {
            Exception last = null;

            for (int i = 0; i < bases.Length; i++)
            {
                string url = bases[i] + path;

                string json = await GetStringThrottledAsync(url, cancellationToken);

                if (!string.IsNullOrWhiteSpace(json))
                    return json;

                last = new Exception("No JSON returned from " + url);
            }

            Debug.WriteLine("[SportsCardsProService] Failed calling API: " + (last?.Message ?? "unknown"));
            return null;
        }

        private async Task<string> GetStringThrottledAsync(string url, CancellationToken cancellationToken)
        {
            await throttleLock.WaitAsync(cancellationToken);

            try
            {
                TimeSpan sinceLast = DateTime.UtcNow - lastRequestUtc;
                if (sinceLast.TotalMilliseconds < 1000)
                {
                    int delayMs = (int)(1000 - sinceLast.TotalMilliseconds);
                    if (delayMs > 0)
                        await Task.Delay(delayMs, cancellationToken);
                }

                lastRequestUtc = DateTime.UtcNow;

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await http.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine("[SportsCardsProService] HTTP " + (int)response.StatusCode + " for " + url);
                    return null;
                }

                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[SportsCardsProService] Request failed: " + ex.Message);
                return null;
            }
            finally
            {
                throttleLock.Release();
            }
        }

        private static async Task<string> LoadTokenFromSecureJsonAsync()
        {
            try
            {
                using Stream stream = await FileSystem.OpenAppPackageFileAsync(SecureJsonPath);
                using StreamReader reader = new StreamReader(stream);

                string json = await reader.ReadToEndAsync();

                if (string.IsNullOrWhiteSpace(json))
                    return null;

                using JsonDocument doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("SPORTS_CARDSPRO_TOKEN", out JsonElement tokenElement) &&
                    tokenElement.ValueKind == JsonValueKind.String)
                {
                    return tokenElement.GetString()?.Trim();
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[SportsCardsProService] Failed to read secure.json: " + ex.Message);
                return null;
            }
        }
    }

}