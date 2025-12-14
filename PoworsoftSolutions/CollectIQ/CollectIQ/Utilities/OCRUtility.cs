/*
* FILE: OCRUtility.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-28
* DESCRIPTION:
*     Centralized OCR utility for reading text from front and back card images.
*     Uses Plugin.Maui.OCR via dependency injection.
*     Sanitization optimized for eBay Browse API keyword accuracy.
*/

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Plugin.Maui.OCR;

namespace CollectIQ.Utilities
{
    public static class OCRUtility
    {
        public static async Task<string?> ExtractTextFromImageAsync(string imagePath)
        {
            try
            {
                if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                    return null;

                var ocr = ServiceHelper.GetService<IOcrService>();
                if (ocr == null)
                    throw new InvalidOperationException("OCR service not registered.");

                var bytes = await File.ReadAllBytesAsync(imagePath);
                var result = await ocr.RecognizeTextAsync(bytes);
                return result?.AllText?.Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OCRUtility] {ex.Message}");
                return null;
            }
        }

        public static async Task<string?> ExtractTextFromFrontAndBackAsync(string frontPath, string backPath)
        {
            var front = await ExtractTextFromImageAsync(frontPath) ?? "";
            var back = await ExtractTextFromImageAsync(backPath) ?? "";

            var merged = $"{front} {back}".Trim();
            return string.IsNullOrWhiteSpace(merged) ? null : merged;
        }

        public static async Task<string?> SanitizeForEbay(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // --- Normalize spacing ---
            string cleaned = Regex.Replace(text, @"[\r\n]+", " ");
            cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim();

            // --- Remove irrelevant words / sections ---
            cleaned = Regex.Replace(cleaned,
                @"(www\.|\.com|©|inc\.|rights reserved|career totals?|stats?|copyright|" +
                "front|back|scan|photo|curacaoan|history|class of|latin american|hall of fame)",
                "", RegexOptions.IgnoreCase);

            // --- Remove stat headers ---
            cleaned = Regex.Replace(cleaned,
                @"\b(AVG|OBP|SLG|OPS|HR|RBI|H|R|2B|3B|AB|BB|SO|SB|CS|G|E|PO|A|DP|IP|ERA|W|L|SV|CG|GS|BF|ER|HBP|CAREER)\b",
                "", RegexOptions.IgnoreCase);

            // --- Extract card number ---
            string cardNumber = "";
            var cardNoMatch = Regex.Match(cleaned, @"No\.?\s*#?\s*(\d+)", RegexOptions.IgnoreCase);
            if (cardNoMatch.Success)
                cardNumber = "No. " + cardNoMatch.Groups[1].Value;

            // --- Extract year ---
            var yearMatch = Regex.Match(cleaned, @"\b(19|20)\d{2}\b");
            string year = yearMatch.Success ? yearMatch.Value : "";

            // --- Normalize brand ---
            cleaned = Regex.Replace(cleaned, @"PANNI|PANlNl", "Panini", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"DONRUSS", "Donruss", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"TOPPS", "Topps", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"CHROME", "Chrome", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"PRIZM", "Prizm", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"SELECT", "Select", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"MOSAIC", "Mosaic", RegexOptions.IgnoreCase);

            // --- Detect brand ---
            var brandMatch = Regex.Match(cleaned, @"\b(Panini\s+Donruss|Donruss|Topps|Select|Mosaic|Prizm|Chrome)\b",
                RegexOptions.IgnoreCase);
            string brand = brandMatch.Success ? brandMatch.Value.Trim() : "";

            // --- Find the correct player name ---
            string playerName = "";

            // (1) Try to find ALL CAPS names near Donruss / No. / ATLANTA
            var allCapsNearBrand = Regex.Match(cleaned,
                @"([A-Z][A-Z]+(?:\s+[A-Z][A-Z]+)+)\s*(?:[•\-\|]*\s*(ATLANTA|Donruss|No\.))",
                RegexOptions.IgnoreCase);
            if (allCapsNearBrand.Success)
            {
                playerName = allCapsNearBrand.Groups[1].Value;
            }
            else
            {
                // (2) Fallback: any two-word name in ALL CAPS
                var allCaps = Regex.Match(cleaned, @"\b([A-Z]{2,}\s+[A-Z]{2,})\b");
                if (allCaps.Success)
                    playerName = allCaps.Groups[1].Value;
            }

            // (3) If no ALL CAPS match, fallback to Title Case name near Donruss
            if (string.IsNullOrEmpty(playerName))
            {
                var titleName = Regex.Match(cleaned,
                    @"([A-Z][a-z]+(?:\s+[A-Z][a-z]+)+)(?=.*(Donruss|No\.))",
                    RegexOptions.IgnoreCase);
                if (titleName.Success)
                    playerName = titleName.Groups[1].Value;
            }

            // --- Final normalization ---
            playerName = ToTitleCase(playerName);
            string result = $"{year} {playerName} {brand} {cardNumber}".Trim();
            result = Regex.Replace(result, @"\s{2,}", " ");

            return ToTitleCase(result);
        }

        private static string ToTitleCase(string text)
        {
            return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.ToLower());
        }



    }

    public static class ServiceHelper
    {
        public static IServiceProvider? Services { get; set; }

        public static T? GetService<T>() where T : class =>
            Services?.GetService(typeof(T)) as T;
    }
}
