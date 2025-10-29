/*
* FILE: OCRUtility.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-28
* DESCRIPTION:
*     Centralized OCR utility for reading text from front and back card images.
*     Uses Plugin.Maui.OCR via dependency injection.
*/

using System;
using System.IO;
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

            string cleaned = Regex.Replace(text, @"(www\.|\.com|©|inc\.|company|rights reserved|code[\w\d]+)", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\b\d+\b", "");
            cleaned = Regex.Replace(cleaned, @"\b[A-Z]\b", "");
            cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim();

            cleaned = cleaned.Replace("XeTOPPS", "Topps", StringComparison.OrdinalIgnoreCase)
                             .Replace("TOPPS", "Topps", StringComparison.OrdinalIgnoreCase)
                             .Replace("PANNI", "Panini", StringComparison.OrdinalIgnoreCase)
                             .Replace("CHROME", "Chrome", StringComparison.OrdinalIgnoreCase)
                             .Replace("ROOKE", "Rookie", StringComparison.OrdinalIgnoreCase)
                             .Replace("RC", "Rookie", StringComparison.OrdinalIgnoreCase);

            var tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 6)
                cleaned = string.Join(" ", tokens[..6]);

            return cleaned;
        }

    }

    public static class ServiceHelper
    {
        public static IServiceProvider? Services { get; set; }

        public static T? GetService<T>() where T : class =>
            Services?.GetService(typeof(T)) as T;
    }
}
