/*
* FILE: CardMetadataParser.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk (corrected by ChatGPT)
* FIRST VERSION: 2025-11-20
* UPDATED: 2025-11-29
* DESCRIPTION:
*     Extracts structured metadata from eBay listing titles/descriptions and
*     applies them to Card objects. Also produces a temporary CardMetadata
*     model for Insights workflows. Complies with SET Coding Standards.
*/

using System;
using System.Text.RegularExpressions;
using CollectIQ.Models;

namespace CollectIQ.Parsers
{
    /// <summary>
    /// Provides methods for parsing structured card metadata from eBay listing text.
    /// </summary>
    public static class CardMetadataParser
    {
        /// <summary>
        /// Applies parsed metadata from an eBay listing directly into an existing Card object.
        /// </summary>
        /// <param name="listing">The eBay listing to parse.</param>
        /// <param name="card">The card object to populate.</param>
        public static void ApplyMetadata(EbayListing listing, Card card)
        {
            if (listing == null || card == null)
                return;

            string title = listing.Title ?? string.Empty;
            string desc = listing.Description ?? string.Empty;

            // --- Player ---
            card.PlayerName = ExtractPlayer(title);

            // --- Team ---
            card.Team = ExtractTeam(title);

            // --- Year ---
            card.Year = ExtractYear(title);

            // --- Set Name ---
            card.SetName = ExtractSet(title);

            // --- Card Number ---
            card.CardNumber = ExtractCardNumber(title);

            // --- Parallel ---
            card.Parallel = ExtractParallel(title);

            // --- Serial ---
            card.SerialNumber = ExtractSerial(title);

            // --- Rookie ---
            card.IsRookie = title.Contains("RC", StringComparison.OrdinalIgnoreCase)
                         || title.Contains("Rookie", StringComparison.OrdinalIgnoreCase);

            // --- Grade ---
            card.GradeValue = ExtractGrade(title);

            // --- Condition ---
            card.Condition = ExtractCondition(desc);
        }

        // ---------------------------------------------------------------------
        // EXTRACTORS (single-argument versions ONLY — matches your new calls)
        // ---------------------------------------------------------------------

        private static int? ExtractYear(string text)
        {
            var match = Regex.Match(text, @"\b(19|20)\d{2}\b");
            return match.Success ? int.Parse(match.Value) : null;
        }

        private static string ExtractPlayer(string text)
        {
            // Example: “Ja Morant”, “LeBron James”
            var match = Regex.Match(text, @"[A-Z][a-z]+ [A-Z][a-z]+");
            return match.Success ? match.Value : string.Empty;
        }

        private static string ExtractTeam(string text)
        {
            string[] teams =
            {
                "Lakers","Warriors","Celtics","Raptors",
                "Chiefs","Bills","Browns","Vikings",
                "Yankees","Dodgers","Blue Jays"
            };

            foreach (string t in teams)
            {
                if (text.Contains(t, StringComparison.OrdinalIgnoreCase))
                    return t;
            }

            return string.Empty;
        }

        private static string ExtractSet(string text)
        {
            string[] sets = { "Prizm", "Select", "Optic", "Donruss", "Mosaic", "Chrome" };

            foreach (string s in sets)
            {
                if (text.Contains(s, StringComparison.OrdinalIgnoreCase))
                    return s;
            }

            return string.Empty;
        }

        private static string ExtractCardNumber(string text)
        {
            var match = Regex.Match(text, @"#\s?(\d+)");
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static string ExtractParallel(string text)
        {
            string[] parallels =
            {
                "Silver","Holo","Zebra","Tie-Dye","Gold","Blue",
                "Red","Purple","Checkerboard","Wave","Mojo"
            };

            foreach (string p in parallels)
            {
                if (text.Contains(p, StringComparison.OrdinalIgnoreCase))
                    return p;
            }

            return string.Empty;
        }

        private static string ExtractSerial(string text)
        {
            var match = Regex.Match(text, @"\/\d+");
            return match.Success ? match.Value : string.Empty;
        }

        private static string ExtractGrade(string text)
        {
            // PSA 9.5 or PSA9
            var psa = Regex.Match(text, @"PSA\s?\d+(\.\d)?");
            if (psa.Success)
                return psa.Value;

            // BGS 9.5
            var bgs = Regex.Match(text, @"BGS\s?\d+(\.\d)?");
            if (bgs.Success)
                return bgs.Value;

            return string.Empty;
        }

        private static string ExtractCondition(string text)
        {
            string[] conditions =
            {
                "NM-MT","NM","EX","VG","Damaged","Crease",
                "Soft corner","Surface scratch"
            };

            foreach (string c in conditions)
            {
                if (text.Contains(c, StringComparison.OrdinalIgnoreCase))
                    return c;
            }

            return string.Empty;
        }

        // ---------------------------------------------------------------------
        // CARDMETADATA (Temporary Insights object)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Extracts structured metadata for Insights workflows.
        /// </summary>
        /// <param name="listing">The eBay listing.</param>
        /// <returns>A populated CardMetadata object.</returns>
        public static CardMetadata ExtractMetadata(EbayListing listing)
        {
            if (listing == null)
                return new CardMetadata();

            string title = listing.Title ?? string.Empty;
            string desc = listing.Description ?? string.Empty;

            return new CardMetadata
            {
                RawTitle = title,
                RawDescription = desc,

                Year = ExtractYear(title),
                PlayerName = ExtractPlayer(title),
                SetName = ExtractSet(title),
                CardNumber = ExtractCardNumber(title),
                Parallel = ExtractParallel(title),
                SerialNumber = ExtractSerial(title),
                GradeValue = ExtractGrade(title),
                IsRookie = title.Contains("RC", StringComparison.OrdinalIgnoreCase)
                        || title.Contains("Rookie", StringComparison.OrdinalIgnoreCase)
            };
        }
    }

    /// <summary>
    /// Lightweight metadata class for Insights.
    /// </summary>
    public sealed class CardMetadata
    {
        public string RawTitle { get; set; } = string.Empty;
        public string RawDescription { get; set; } = string.Empty;

        public int? Year { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public string SetName { get; set; } = string.Empty;
        public string CardNumber { get; set; } = string.Empty;
        public string Parallel { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string GradeValue { get; set; } = string.Empty;
        public bool IsRookie { get; set; }
    }
}
