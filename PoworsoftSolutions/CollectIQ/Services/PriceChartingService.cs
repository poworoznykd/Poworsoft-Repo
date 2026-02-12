/*
 *  CollectIQ - PriceChartingService.cs
 *  ---------------------------------------------------------------------------
 *  Purpose:
 *      Minimal client for the PriceCharting API used to fetch price guide values
 *      (raw / graded / PSA10, etc.) to power CollectIQ Insights.
 *
 *  Notes:
 *      - This is a price guide (current values). It is not a sold-comps feed.
 *      - Token is loaded from secure.json in the app package at runtime.
 *      - Prices from PriceCharting are returned as pennies (integer cents). We store dollars.
 */

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CollectIQ.Models;
using Microsoft.Maui.Storage;

namespace CollectIQ.Services
{
    /// <summary>
    /// Provides access to PriceCharting price guide data.
    /// </summary>
    public sealed class PriceChartingService
    {
        private const string ProductEndpoint = "https://www.pricecharting.com/api/product";
        private const string ProductsEndpoint = "https://www.pricecharting.com/api/products";

        private readonly HttpClient httpClient;
        private string cachedToken;

        public PriceChartingService(HttpClient httpClient)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>
        /// Best-match lookup (Prices API) by query string (q=...).
        /// </summary>
        public async Task<PriceChartingProduct> GetBestMatchAsync(string query, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            string token = await GetTokenAsync(ct);
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            string url = $"{ProductEndpoint}?t={token}&q={UrlEncoder.Default.Encode(query.Trim())}";
            return await GetProductInternalAsync(url, ct);
        }

        /// <summary>
        /// Lookup a single product by PriceCharting product id.
        /// </summary>
        public async Task<PriceChartingProduct> GetByIdAsync(string id, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            string token = await GetTokenAsync(ct);
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            string url = $"{ProductEndpoint}?t={token}&id={UrlEncoder.Default.Encode(id.Trim())}";
            return await GetProductInternalAsync(url, ct);
        }

        /// <summary>
        /// Return multiple candidate products (up to 20) using /api/products.
        /// </summary>
        public async Task<IReadOnlyList<PriceChartingProductStub>> SearchProductsAsync(string query, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Array.Empty<PriceChartingProductStub>();
            }

            string token = await GetTokenAsync(ct);
            if (string.IsNullOrWhiteSpace(token))
            {
                return Array.Empty<PriceChartingProductStub>();
            }

            string url = $"{ProductsEndpoint}?t={token}&q={UrlEncoder.Default.Encode(query.Trim())}";
            HttpResponseMessage response = await httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<PriceChartingProductStub>();
            }

            string json = await response.Content.ReadAsStringAsync(ct);
            using JsonDocument doc = JsonDocument.Parse(json);

            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("status", out JsonElement status) ||
                status.GetString() != "success")
            {
                return Array.Empty<PriceChartingProductStub>();
            }

            if (!root.TryGetProperty("products", out JsonElement productsEl) ||
                productsEl.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<PriceChartingProductStub>();
            }

            List<PriceChartingProductStub> list = new List<PriceChartingProductStub>();
            foreach (JsonElement item in productsEl.EnumerateArray())
            {
                PriceChartingProductStub stub = PriceChartingProductStub.FromJson(item);
                if (stub != null)
                {
                    list.Add(stub);
                }
            }

            return list;
        }

        private async Task<PriceChartingProduct> GetProductInternalAsync(string url, CancellationToken ct)
        {
            HttpResponseMessage response = await httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(ct);
            using JsonDocument doc = JsonDocument.Parse(json);

            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("status", out JsonElement status) ||
                status.GetString() != "success")
            {
                return null;
            }

            return PriceChartingProduct.FromJson(root);
        }

        private async Task<string> GetTokenAsync(CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(cachedToken))
            {
                return cachedToken;
            }

            try
            {
                // secure.json is expected to be included as a MAUI asset.
                using var stream = await FileSystem.OpenAppPackageFileAsync("secure.json");
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("pricecharting_token", out JsonElement tokenEl))
                {
                    cachedToken = tokenEl.GetString();
                }
            }
            catch
            {
                cachedToken = null;
            }

            return cachedToken;
        }
    }

    /// <summary>
    /// Minimal /api/products result element (id, product-name, console-name).
    /// </summary>
    public sealed class PriceChartingProductStub
    {
        public string Id { get; set; }
        public string ProductName { get; set; }
        public string ConsoleName { get; set; }

        public static PriceChartingProductStub FromJson(JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new PriceChartingProductStub
            {
                Id = GetString(el, "id"),
                ProductName = GetString(el, "product-name"),
                ConsoleName = GetString(el, "console-name")
            };
        }

        private static string GetString(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out JsonElement v))
            {
                return null;
            }

            if (v.ValueKind == JsonValueKind.String)
            {
                return v.GetString();
            }

            if (v.ValueKind == JsonValueKind.Number)
            {
                return v.ToString();
            }

            return null;
        }
    }
}
