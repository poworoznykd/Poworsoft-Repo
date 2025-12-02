/*
* FILE: CardMetadataParser.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-12-01
* UPDATED: 2025-12-01
* DESCRIPTION:
*     Enhanced metadata parser for eBay listings.
*     - Keeps ALL previous logic
*     - Adds multilayer extraction for:
*         * Name (athlete or Pokémon)
*         * Sport
*         * Parallels (Silver, Refractor, etc.)
*         * Subsets (Canvas Legends, My House, Downtown, etc.)
*         * Serial Numbers
*         * Pokémon card variants
*     - Uses BOTH Title and Description if available
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CollectIQ.Models;

namespace CollectIQ.Utilities
{
    public static class CardMetadataParser
    {
        // Sets (extended)
        private static readonly string[] setKeywords =
        {
            "Prizm","Panini Prizm","Panini","Select","Mosaic","Optic","Donruss",
            "Topps","Topps Chrome","Bowman","Upper Deck","Fleer","Chronicles",
            "Score","Illusions","Revolution","Phoenix","Absolute","SP Authentic",
            "Tim Hortons","Artifacts","O-Pee-Chee","Ultimate","Premier"
        };

        // Pokémon sets
        private static readonly string[] pokemonSets =
        {
            "Base Set","Jungle","Fossil","Team Rocket","Gym Heroes","Gym Challenge",
            "Neo Genesis","Neo Discovery","Neo Revelation","Neo Destiny",
            "e-Reader","Ruby Sapphire","Diamond & Pearl","HeartGold SoulSilver",
            "Black & White","XY","Sun & Moon","Sword & Shield","Scarlet & Violet",
            "Champion's Path","Evolving Skies","Vivid Voltage","Celebrations"
        };

        // Teams (all sports)
        private static readonly string[] teamKeywords =
        {
            // NHL
            "Maple Leafs","Canadiens","Oilers","Flames","Senators","Canucks","Jets",
            "Penguins","Rangers","Islanders","Bruins","Sabres","Blackhawks",
            "Kraken","Golden Knights",

            // NFL
            "Patriots","Chiefs","49ers","Cowboys","Packers","Lions","Bills","Vikings",
            "Jets","Dolphins","Ravens","Steelers","Bengals","Browns","Giants",
            "Eagles","Commanders","Titans","Buccaneers","Jaguars","Texans",

            // NBA
            "Lakers","Celtics","Warriors","Bulls","Heat","Knicks","Suns","Spurs",
            "Raptors","Mavericks","Hawks","Magic","Hornets","76ers","Pelicans",
            "Grizzlies"
        };

        // Parallels
        private static readonly string[] parallelKeywords =
        {
            "Refractor","XFractor","Superfractor","Mojo","Silver","Holo",
            "Wave","Hyper","Pulsar","Checkerboard","Cracked Ice","Green Ice",
            "Blue", "Orange", "Red", "Gold", "Black","Rainbow","Spectrum"
        };

        // Subsets / Insert names
        private static readonly string[] subsetKeywords =
        {
            "Canvas","Canvas Legends","UD Canvas Legends","My House","Downtown",
            "Fireworks","Shock","Stargazing","Phenoms","All-Star","Rookie",
            "Legend","Legends","Retro","Throwback"
        };

        // Pokémon rarities & card types
        private static readonly string[] pokemonRarities =
        {
            "Holo","Reverse Holo","Full Art","Ultra Rare","Secret Rare",
            "Gold Rare","Rainbow Rare"
        };

        private static readonly string[] pokemonCardTypes =
        {
            "GX","EX","V","VMAX","VSTAR","Trainer","Energy"
        };

        // Pokémon names (partial list; can be expanded)
        private static readonly string[] pokemonNames =
        {
            "Pikachu","Charizard","Blastoise","Venusaur","Mew","Mewtwo","Eevee",
            "Gengar","Snorlax","Arceus","Umbreon","Espeon","Rayquaza","Lugia"
        };

        public static Card Parse(EbayListing listing)
        {
            if (listing == null)
                return new Card();

            string title = listing.Title ?? string.Empty;
            string desc = listing.Description ?? string.Empty;
            string combined = title + " " + desc;

            Card card = new Card
            {
                Title = title.Trim(),
                EstimatedValue = listing.Price,
                CollectionId = "Default",
                FrontImagePath = listing.ImageUrl,
                BackImagePath = listing.ImageUrl
            };

            // -------------------------
            //  YEAR
            // -------------------------
            Match yearMatch = Regex.Match(combined, @"\b(19|20)\d{2}\b");
            if (yearMatch.Success)
                card.Year = int.Parse(yearMatch.Value);

            // -------------------------
            //  SERIAL NUMBER (#/99)
            // -------------------------
            Match serial = Regex.Match(combined, @"(\d{1,3}\/\d{1,3})");
            if (serial.Success)
                card.SerialNumber = serial.Value;

            // -------------------------
            //  CARD NUMBER (#CL-3, #205, #A-PICK)
            // -------------------------
            Match cardNum = Regex.Match(combined, @"#\s?([A-Za-z0-9\-]+)");
            if (cardNum.Success)
                card.Number = cardNum.Groups[1].Value;

            // -------------------------
            //  SET DETECTION
            // -------------------------
            foreach (var set in setKeywords)
                if (combined.Contains(set, StringComparison.OrdinalIgnoreCase))
                    card.Set = set;

            foreach (var pset in pokemonSets)
                if (combined.Contains(pset, StringComparison.OrdinalIgnoreCase))
                    card.Set = pset;

            // -------------------------
            //  SPORT DETECTION
            // -------------------------
            if (combined.Contains("Upper Deck", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("Tim Hortons", StringComparison.OrdinalIgnoreCase))
                card.Sport = "Hockey";
            else if (combined.Contains("Panini", StringComparison.OrdinalIgnoreCase) ||
                     combined.Contains("Prizm", StringComparison.OrdinalIgnoreCase))
                card.Sport = "Football/Basketball (Panini)";
            else if (combined.Contains("Topps", StringComparison.OrdinalIgnoreCase))
                card.Sport = "Baseball";
            else if (pokemonCardsFound(title, desc))
                card.Sport = "Pokémon";

            bool pokemonCardsFound(string t, string d)
            {
                return pokemonNames.Any(n => t.Contains(n, StringComparison.OrdinalIgnoreCase) ||
                                             d.Contains(n, StringComparison.OrdinalIgnoreCase));
            }

            // -------------------------
            //  TEAM
            // -------------------------
            foreach (var team in teamKeywords)
                if (combined.Contains(team, StringComparison.OrdinalIgnoreCase))
                    card.Team = team;

            // -------------------------
            //  PARALLEL
            // -------------------------
            foreach (var par in parallelKeywords)
                if (combined.Contains(par, StringComparison.OrdinalIgnoreCase))
                    card.Parallel = par;

            // -------------------------
            //  SUBSET / INSERT
            // -------------------------
            foreach (var sub in subsetKeywords)
                if (combined.Contains(sub, StringComparison.OrdinalIgnoreCase))
                    card.Subset = sub;

            // -------------------------
            //  GRADE & GRADER
            // -------------------------
            Match psa = Regex.Match(combined, @"PSA\s?(\d+(\.\d)?)");
            if (psa.Success)
            {
                card.GradeCompany = "PSA";
                card.Grade = double.Parse(psa.Groups[1].Value);
            }

            Match bgs = Regex.Match(combined, @"BGS\s?(\d+(\.\d)?)");
            if (bgs.Success)
            {
                card.GradeCompany = "BGS";
                card.Grade = double.Parse(bgs.Groups[1].Value);
            }

            Match cgc = Regex.Match(combined, @"CGC\s?(\d+(\.\d)?)");
            if (cgc.Success)
            {
                card.GradeCompany = "CGC";
                card.Grade = double.Parse(cgc.Groups[1].Value);
            }

            Match sgc = Regex.Match(combined, @"SGC\s?(\d+(\.\d)?)");
            if (sgc.Success)
            {
                card.GradeCompany = "SGC";
                card.Grade = double.Parse(sgc.Groups[1].Value);
            }

            // -----------------------------
            // Extract Name (New reliable method)
            // -----------------------------
            string extractedName = ExtractName(title, desc);
            if (!string.IsNullOrWhiteSpace(extractedName))
            {
                card.Name = extractedName;
            }

            return card;
        }

        private static string ExtractName(string title, string desc)
        {
            string combined = title + " " + desc;
            // ---------------------------------------------
            // NEW RULE: Skip everything before the first set keyword
            // This prevents "Panini Illusions" from being treated as the name.
            // ---------------------------------------------
            foreach (var set in setKeywords)
            {
                int idx = combined.IndexOf(set, StringComparison.OrdinalIgnoreCase);
                if (idx > -1)
                {
                    combined = combined.Substring(idx + set.Length);
                    break;
                }
            }

            // ---------------------------------------------
            // 0. Normalize hyphens that precede names
            // ---------------------------------------------
            // e.g., "- Trevor Lawrence" → " Trevor Lawrence"
            combined = Regex.Replace(combined, @"-\s+", " ");

            // --- PRIORITY 1: Known Pokémon ---
            foreach (var p in pokemonNames)
                if (combined.Contains(p, StringComparison.OrdinalIgnoreCase))
                    return p;

            // ---------------------------------------------
            // PRIORITY 2: Look for “Firstname Lastname” patterns
            // Supports 2–3 word human names
            // ---------------------------------------------
            Match humanName = Regex.Match(
                combined,
                @"\b([A-Z][a-z]+(?: [A-Z][a-z]+){1,2})\b"
            );

            if (humanName.Success)
            {
                string candidate = humanName.Value.Trim();

                if (!IsFalseName(candidate))
                    return candidate;
            }

            // ---------------------------------------------
            // PRIORITY 3: Name after the year
            // ---------------------------------------------
            Match yearMatch = Regex.Match(combined, @"\b(19|20)\d{2}\b");

            if (yearMatch.Success)
            {
                string afterYear = combined[(yearMatch.Index + yearMatch.Length)..];

                Match nameAfterYear = Regex.Match(
                    afterYear,
                    @"\b([A-Z][a-z]+(?: [A-Z][a-z]+){1,2})\b"
                );

                if (nameAfterYear.Success && !IsFalseName(nameAfterYear.Value))
                    return nameAfterYear.Value.Trim();
            }

            // ---------------------------------------------
            // PRIORITY 4: Look for name AFTER set/parallel keywords
            // e.g. “Prizm Draft Picks Trevor Lawrence”
            // ---------------------------------------------
            string[] hardSplitKeywords =
            {
                "Prizm", "Select", "Draft Picks", "Panini", "Topps", "Upper Deck"
            };

            foreach (var key in hardSplitKeywords)
            {
                int idx = combined.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                if (idx > -1)
                {
                    string part = combined[(idx + key.Length)..];

                    Match newName = Regex.Match(
                        part,
                        @"\b([A-Z][a-z]+(?: [A-Z][a-z]+){1,2})\b"
                    );

                    if (newName.Success && !IsFalseName(newName.Value))
                        return newName.Value.Trim();
                }
            }

            return string.Empty;

            // ----- False matches filter -----
            bool IsFalseName(string s)
            {
                string[] banned =
                {
                    "In The Game","The Game","Club Level","Field Level",
                    "Premier Level","Concourse Level","Select Certified",
                    "Tim Hortons","Upper Deck","Panini","Topps","Prizm",
                    "Draft Picks","Panini Illusions", "Panini Select",
                    "Panini Prizm", "Upper Deck Ice", "Topps Chrome",
                    "Panini Chronicles", "Panini Mosaic"
                };


                return banned.Any(b =>
                    s.Equals(b, StringComparison.OrdinalIgnoreCase));
            }
        }


    }
}
