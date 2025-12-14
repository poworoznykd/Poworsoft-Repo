/*
* FILE: EbayAuthService.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* DESCRIPTION:
*     Handles eBay OAuth2 token refresh for REST API calls.
*/

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CollectIQ.Services
{
    public static class EbayAuthService
    {
        private static string? _cachedToken;
        private static DateTime _expiry = DateTime.MinValue;

        public static async Task<string> GetAccessTokenAsync()
        {
            if (_cachedToken != null && DateTime.UtcNow < _expiry.AddMinutes(-2))
                return _cachedToken;

            // Load credentials from your local secure.json
            var path = Path.Combine(AppContext.BaseDirectory, "secure.json");
            if (!File.Exists(path))
                throw new FileNotFoundException("secure.json not found.");

            var creds = JsonDocument.Parse(await File.ReadAllTextAsync(path)).RootElement;
            var clientId = creds.GetProperty("EBAY_CLIENT_ID").GetString();
            var clientSecret = creds.GetProperty("EBAY_CLIENT_SECRET").GetString();
            var refreshToken = creds.GetProperty("EBAY_REFRESH_TOKEN").GetString();

            var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authHeader);

            var body = new StringContent(
                $"grant_type=refresh_token&refresh_token={refreshToken}&scope=https://api.ebay.com/oauth/api_scope",
                Encoding.UTF8, "application/x-www-form-urlencoded");

            var resp = await client.PostAsync("https://api.ebay.com/identity/v1/oauth2/token", body);
            var json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Token refresh failed: {resp.StatusCode} {json}");

            var doc = JsonDocument.Parse(json);
            _cachedToken = doc.RootElement.GetProperty("access_token").GetString();
            var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();
            _expiry = DateTime.UtcNow.AddSeconds(expiresIn);

            System.Diagnostics.Debug.WriteLine($"[eBay TOKEN REFRESH] success, expires in {expiresIn / 60} min");
            return _cachedToken!;
        }
    }
}
