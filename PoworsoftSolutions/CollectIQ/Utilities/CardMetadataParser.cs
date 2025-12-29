/*
* FILE: CardMetadataParser.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-12-01
* UPDATED: 2025-12-13
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
*     - Uses local CSV-based player lists (NFL/NBA/MLB/NHL)
*       before falling back to regex-based name parsing.
*/

using CollectIQ.Domain.Entities;
using CollectIQ.Models;
using Microsoft.Maui.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CollectIQ.Utilities
{
    public static class CardMetadataParser
    {
        // -----------------------------------------------------------------
        //  STATIC CONFIGURATION
        // -----------------------------------------------------------------

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
            "Eagles","Commanders","Titans","Buccaneers","Jaguars","Texans","Colts",

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
            "Legend","Legends","Retro","Throwback","Holiday Sweaters","Holiday Sweater"
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

        // -----------------------------------------------------------------
        //  PLAYER CSV SUPPORT
        // -----------------------------------------------------------------

        /// <summary>
        /// Simple model for a player loaded from CSV.
        /// Expected columns: FirstName, LastName, FullName.
        /// </summary>
        private sealed class PlayerNameEntry
        {
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
        }

        // CSV files live under Resources/Data and are marked as MauiAsset.
        // Logical asset names we *intend* to use: "Data/NFL_players.csv", etc.
        private static readonly Lazy<List<PlayerNameEntry>> NflPlayers =
            new Lazy<List<PlayerNameEntry>>(
                () => LoadPlayersFromCsv("Data/NFL_players.csv"));

        private static readonly Lazy<List<PlayerNameEntry>> NbaPlayers =
            new Lazy<List<PlayerNameEntry>>(
                () => LoadPlayersFromCsv("Data/NBA_players.csv"));

        private static readonly Lazy<List<PlayerNameEntry>> MlbPlayers =
            new Lazy<List<PlayerNameEntry>>(
                () => LoadPlayersFromCsv("Data/MLB_players.csv"));

        private static readonly Lazy<List<PlayerNameEntry>> NhlPlayers =
            new Lazy<List<PlayerNameEntry>>(
                () => LoadPlayersFromCsv("Data/NHL_players.csv"));

        /// <summary>
        /// Loads a CSV file of players (FirstName, LastName, FullName)
        /// from MAUI assets using FileSystem.OpenAppPackageFileAsync.
        ///
        /// IMPORTANT:
        /// MAUI sometimes flattens asset names, so we try both:
        ///   - the full "Data/Name.csv"
        ///   - just "Name.csv"
        /// and use whichever one actually exists.
        /// </summary>
        /// <summary>
        /// Loads a CSV file of players (FirstName, LastName, FullName)
        /// from MAUI assets using FileSystem.OpenAppPackageFileAsync.
        ///
        /// We try several logical names because MAUI can pack assets as:
        ///   - "Resources/Data/NFL_players.csv"
        ///   - "Data/NFL_players.csv"
        ///   - "NFL_players.csv"
        /// depending on csproj configuration.
        /// </summary>
        private static List<PlayerNameEntry> LoadPlayersFromCsv(string assetName)
        {
            List<PlayerNameEntry> players = new List<PlayerNameEntry>();

            // Example assetName coming in: "Data/NFL_players.csv"
            string fileOnly = Path.GetFileName(assetName);

            string[] candidateAssetNames =
            {
                // What the caller asked for, e.g. "Data/NFL_players.csv"
                assetName,

                // Most likely MAUI asset name for Resources/Data/...
                $"Resources/{assetName}",          // "Resources/Data/NFL_players.csv"

                // Just the filename, in case assets are flattened
                fileOnly,                          // "NFL_players.csv"

                // Some projects end up with "Resources/NFL_players.csv"
                $"Resources/{fileOnly}"            // "Resources/NFL_players.csv"
            };

            foreach (string candidate in candidateAssetNames)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[PLAYER CSV] Trying asset '{candidate}'...");

                    using Stream stream = FileSystem.OpenAppPackageFileAsync(candidate)
                        .GetAwaiter()
                        .GetResult();

                    using StreamReader reader = new StreamReader(stream);

                    // Skip header
                    string? header = reader.ReadLine();

                    while (!reader.EndOfStream)
                    {
                        string? line = reader.ReadLine();
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        string[] parts = line.Split(',');

                        if (parts.Length < 3)
                        {
                            continue;
                        }

                        PlayerNameEntry entry = new PlayerNameEntry
                        {
                            FirstName = parts[0].Trim(),
                            LastName = parts[1].Trim(),
                            FullName = parts[2].Trim()
                        };

                        if (!string.IsNullOrWhiteSpace(entry.FullName))
                        {
                            players.Add(entry);
                        }
                    }

                    System.Diagnostics.Debug.WriteLine(
                        $"[PLAYER CSV] Loaded {players.Count} players from asset '{candidate}'");

                    // Success – stop trying other candidates
                    return players;
                }
                catch (FileNotFoundException fnf)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[PLAYER CSV] Asset '{candidate}' not found: {fnf.Message}");
                    // Try next candidate
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[PLAYER CSV ERROR] Asset '{candidate}' - {ex.Message}");
                    // For other errors just bail and return whatever we have (likely empty)
                    return players;
                }
            }

            System.Diagnostics.Debug.WriteLine(
                $"[PLAYER CSV] No asset found for '{assetName}' or '{fileOnly}'. Returning empty list.");

            return players;
        }


        /// <summary>
        /// Attempts to find a player name from the preloaded CSV lists
        /// by matching FullName or (FirstName + LastName) against the
        /// normalized listing text. Returns the best match or String.Empty.
        /// </summary>
        private static string TryFindPlayerNameFromCsv(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string normalized = NormalizeForNameSearch(text);
            string padded = " " + normalized + " "; // for safe word-boundary matching

            string bestName = string.Empty;
            int bestScore = 0;

            bestScore = EvaluateLeagueList(NflPlayers.Value, padded, ref bestName, bestScore);
            bestScore = EvaluateLeagueList(NbaPlayers.Value, padded, ref bestName, bestScore);
            bestScore = EvaluateLeagueList(MlbPlayers.Value, padded, ref bestName, bestScore);
            bestScore = EvaluateLeagueList(NhlPlayers.Value, padded, ref bestName, bestScore);

            return bestName;
        }

        /// <summary>
        /// Scans one league's player list and updates the current best
        /// name/score if a better match is found.
        /// </summary>
        private static int EvaluateLeagueList(
            List<PlayerNameEntry> leaguePlayers,
            string paddedNormalizedText,
            ref string currentBestName,
            int currentBestScore)
        {
            if (leaguePlayers == null || leaguePlayers.Count == 0)
            {
                return currentBestScore;
            }

            foreach (PlayerNameEntry entry in leaguePlayers)
            {
                if (entry == null)
                {
                    continue;
                }

                string first = (entry.FirstName ?? string.Empty).Trim();
                string last = (entry.LastName ?? string.Empty).Trim();
                string full = (entry.FullName ?? string.Empty).Trim();

                // Skip header rows or junk
                if (full.Equals("FullName", StringComparison.OrdinalIgnoreCase) ||
                    first.Equals("FirstName", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int score = 0;

                // 1) Strongest: full name as a phrase in the text.
                if (!string.IsNullOrWhiteSpace(full))
                {
                    string fullNorm = NormalizeForNameSearch(full);
                    string fullNeedle = " " + fullNorm + " ";

                    if (paddedNormalizedText.Contains(fullNeedle, StringComparison.Ordinal))
                    {
                        score = Math.Max(score, 200 + fullNorm.Length);
                    }
                }

                // 2) Next: first + last BOTH present as words (any order).
                if (!string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(last))
                {
                    string firstNorm = " " + NormalizeForNameSearch(first) + " ";
                    string lastNorm = " " + NormalizeForNameSearch(last) + " ";

                    bool firstInText = paddedNormalizedText.Contains(firstNorm, StringComparison.Ordinal);
                    bool lastInText = paddedNormalizedText.Contains(lastNorm, StringComparison.Ordinal);

                    if (firstInText && lastInText)
                    {
                        int lengthScore = firstNorm.Length + lastNorm.Length;
                        score = Math.Max(score, 150 + lengthScore);
                    }
                }

                if (score > currentBestScore)
                {
                    currentBestScore = score;

                    if (!string.IsNullOrWhiteSpace(full))
                    {
                        currentBestName = full;
                    }
                    else if (!string.IsNullOrWhiteSpace(first) &&
                             !string.IsNullOrWhiteSpace(last))
                    {
                        currentBestName = $"{CapitalizeName(first)} {CapitalizeName(last)}";
                    }
                }
            }

            return currentBestScore;
        }

        /// <summary>
        /// Strips punctuation, lowercases, and collapses whitespace so
        /// we can do reliable word-based matching.
        /// </summary>
        private static string NormalizeForNameSearch(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string lower = text.ToLowerInvariant();

            // Keep letters/digits/space only.
            lower = Regex.Replace(lower, @"[^a-z0-9\s]", " ");
            lower = Regex.Replace(lower, @"\s+", " ").Trim();

            return lower;
        }

        /// <summary>
        /// Simple "nice casing" for names loaded from CSV
        /// if we have to reconstruct from first/last.
        /// </summary>
        private static string CapitalizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            value = value.Trim();

            if (value.Length == 1)
            {
                return value.ToUpperInvariant();
            }

            return char.ToUpperInvariant(value[0]) +
                   value.Substring(1).ToLowerInvariant();
        }

        // -----------------------------------------------------------------
        //  PUBLIC ENTRY POINT
        // -----------------------------------------------------------------

        public static Card Parse(EbayListing listing)
        {
            if (listing == null)
            {
                return new Card();
            }

            string title = listing.Title ?? string.Empty;
            string desc = listing.Description ?? string.Empty;
            string combined = title + " " + desc;

            Card card = new Card
            {
                Title = title.Trim(),
                Insights = new CardInsights(listing.Price ?? 0.00m),
                CollectionId = "Default",
                FrontThumbnailPath = listing.ImageUrl,
                FrontImagePath = listing.ImageUrl,
                BackImagePath = listing.ImageUrl
            };

            // YEAR
            Match yearMatch = Regex.Match(combined, @"\b(19|20)\d{2}\b");
            if (yearMatch.Success)
            {
                card.Year = int.Parse(yearMatch.Value);
            }

            // SERIAL NUMBER (#/99)
            Match serial = Regex.Match(combined, @"(\d{1,3}\/\d{1,3})");
            if (serial.Success)
            {
                card.SerialNumber = serial.Value;
            }

            // CARD NUMBER (#CL-3, #205, #A-PICK)
            Match cardNum = Regex.Match(combined, @"#\s?([A-Za-z0-9\-]+)");
            if (cardNum.Success)
            {
                card.Number = cardNum.Groups[1].Value;
            }

            // SET DETECTION
            foreach (string set in setKeywords)
            {
                if (combined.Contains(set, StringComparison.OrdinalIgnoreCase))
                {
                    card.Set = set;
                }
            }

            foreach (string pset in pokemonSets)
            {
                if (combined.Contains(pset, StringComparison.OrdinalIgnoreCase))
                {
                    card.Set = pset;
                }
            }

            // SPORT DETECTION
            if (combined.Contains("Upper Deck", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("Tim Hortons", StringComparison.OrdinalIgnoreCase))
            {
                card.Sport = "Hockey";
            }
            else if (combined.Contains("Panini", StringComparison.OrdinalIgnoreCase) ||
                     combined.Contains("Prizm", StringComparison.OrdinalIgnoreCase))
            {
                card.Sport = "Football/Basketball (Panini)";
            }
            else if (combined.Contains("Topps", StringComparison.OrdinalIgnoreCase))
            {
                card.Sport = "Baseball";
            }
            else if (PokemonCardsFound(title, desc))
            {
                card.Sport = "Pokémon";
            }

            // TEAM
            foreach (string team in teamKeywords)
            {
                if (combined.Contains(team, StringComparison.OrdinalIgnoreCase))
                {
                    card.Team = team;
                }
            }

            // PARALLEL
            foreach (string par in parallelKeywords)
            {
                if (combined.Contains(par, StringComparison.OrdinalIgnoreCase))
                {
                    card.Parallel = par;
                }
            }

            // SUBSET / INSERT
            foreach (string sub in subsetKeywords)
            {
                if (combined.Contains(sub, StringComparison.OrdinalIgnoreCase))
                {
                    card.Subset = sub;
                }
            }

            // GRADE & GRADER
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

            // NAME (CSV-first, then fallback)
            string extractedName = ExtractName(title, desc);
            if (!string.IsNullOrWhiteSpace(extractedName))
            {
                card.Name = extractedName;
            }
            // -----------------------------
            // Ensure Player object is populated
            // -----------------------------
            PopulatePlayerFromCard(card);
            return card;
        }

        /// <summary>
        /// Ensures card.Player exists and copies over whatever
        /// player-related info we know from the Card.
        /// </summary>
        private static void PopulatePlayerFromCard(Card card)
        {
            if (card == null)
            {
                return;
            }

            // Get a working instance from the JSON-backed property.
            // (Each get returns a new object, so we must assign it back after changes.)
            Player player = card.Player ?? new Player();
            bool dirty = false;

            // Full name – prefer the parsed Card.Name
            if (!string.IsNullOrWhiteSpace(card.Name) &&
                string.IsNullOrWhiteSpace(player.FullName))
            {
                player.FullName = card.Name;
                dirty = true;
            }

            // Sport – mirror from Card.Sport
            if (!string.IsNullOrWhiteSpace(card.Sport) &&
                string.IsNullOrWhiteSpace(player.Sport))
            {
                player.Sport = card.Sport;
                dirty = true;
            }

            // Highlight reel – if the Card has Highlights, push into Player.HighlightReel
            if (card.Highlights != null && player.HighlightReel == null)
            {
                player.HighlightReel = card.Highlights;
                dirty = true;
            }

            // Only write back if we actually changed something; this updates PlayerJson.
            if (dirty)
            {
                card.Player = player;
            }
        }


        // -----------------------------------------------------------------
        //  NAME EXTRACTION
        // -----------------------------------------------------------------

        private static string ExtractName(string title, string desc)
        {
            string combined = (title ?? string.Empty) + " " + (desc ?? string.Empty);

            // PRIORITY 0: Try CSV-based player lists first.
            string csvName = TryFindPlayerNameFromCsv(combined);
            if (!string.IsNullOrWhiteSpace(csvName))
            {
                return csvName;
            }

            // Skip everything before first set keyword (avoid "Panini Illusions" as name)
            foreach (string set in setKeywords)
            {
                int idx = combined.IndexOf(set, StringComparison.OrdinalIgnoreCase);
                if (idx > -1)
                {
                    combined = combined.Substring(idx + set.Length);
                    break;
                }
            }

            // Normalize "- Name" → " Name"
            combined = Regex.Replace(combined, @"-\s+", " ");

            // Known Pokémon names
            foreach (string p in pokemonNames)
            {
                if (combined.Contains(p, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }

            // ALL-CAPS (e.g., BAKER MAYFIELD)
            string allCapsCandidate = FindBestAllCapsNameCandidate(combined);
            if (!string.IsNullOrWhiteSpace(allCapsCandidate) &&
                !IsFalseName(allCapsCandidate))
            {
                return allCapsCandidate;
            }

            // TitleCase human name
            string humanCandidate = FindBestHumanNameCandidate(combined);
            if (!string.IsNullOrWhiteSpace(humanCandidate) &&
                !IsFalseName(humanCandidate))
            {
                return humanCandidate;
            }

            // Name after the year
            Match yearMatch = Regex.Match(combined, @"\b(19|20)\d{2}\b");
            if (yearMatch.Success)
            {
                string afterYear = combined.Substring(yearMatch.Index + yearMatch.Length);

                string nameAfterYear = FindBestHumanNameCandidate(afterYear);
                if (!string.IsNullOrWhiteSpace(nameAfterYear) &&
                    !IsFalseName(nameAfterYear))
                {
                    return nameAfterYear.Trim();
                }
            }

            // Name after set/parallel keywords
            string[] hardSplitKeywords =
            {
                "Prizm", "Select", "Draft Picks", "Panini", "Topps", "Upper Deck"
            };

            foreach (string key in hardSplitKeywords)
            {
                int idx = combined.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                if (idx > -1)
                {
                    string part = combined.Substring(idx + key.Length);
                    string newName = FindBestHumanNameCandidate(part);

                    if (!string.IsNullOrWhiteSpace(newName) &&
                        !IsFalseName(newName))
                    {
                        return newName.Trim();
                    }
                }
            }

            return string.Empty;
        }

        private static string FindBestAllCapsNameCandidate(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            MatchCollection matches = Regex.Matches(
                text,
                @"\b([A-Z]{2,}(?:\s+[A-Z0-9]{2,}){1,3})\b");

            List<string> candidates = new List<string>();

            foreach (Match m in matches)
            {
                if (!m.Success)
                {
                    continue;
                }

                string candidate = m.Value.Trim();
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                if (IsFalseName(candidate))
                {
                    continue;
                }

                if (candidate.Length < 4)
                {
                    continue;
                }

                candidates.Add(candidate);
            }

            if (candidates.Count == 0)
            {
                return string.Empty;
            }

            return candidates
                .OrderByDescending(c => c.Length)
                .First();
        }

        private static string FindBestHumanNameCandidate(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            text = Regex.Replace(text, @"-\s+", " ");

            MatchCollection matches = Regex.Matches(
                text,
                @"\b([A-Z][a-z]+(?: [A-Z][a-z]+){1,2})\b");

            if (matches.Count == 0)
            {
                return string.Empty;
            }

            List<(string Candidate, double Score)> scored =
                new List<(string Candidate, double Score)>();

            foreach (Match m in matches)
            {
                if (!m.Success)
                {
                    continue;
                }

                string candidate = m.Value.Trim();
                if (candidate.Length < 3)
                {
                    continue;
                }

                string[] tokens = candidate
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (tokens.Length == 0)
                {
                    continue;
                }

                double score = 0;

                score += candidate.Length;

                if (tokens.Length >= 2 && tokens.Length <= 3)
                {
                    score += 40.0;
                }

                if (tokens.Any(t =>
                        t.Equals("II", StringComparison.OrdinalIgnoreCase) ||
                        t.Equals("III", StringComparison.OrdinalIgnoreCase) ||
                        t.Equals("IV", StringComparison.OrdinalIgnoreCase) ||
                        t.Equals("Jr", StringComparison.OrdinalIgnoreCase) ||
                        t.Equals("Sr", StringComparison.OrdinalIgnoreCase)))
                {
                    score += 10.0;
                }

                if (ContainsAnyTokenIn(tokens, setKeywords))
                {
                    score -= 60.0;
                }

                if (ContainsAnyTokenIn(tokens, parallelKeywords))
                {
                    score -= 40.0;
                }

                if (ContainsAnyTokenIn(tokens, subsetKeywords))
                {
                    score -= 40.0;
                }

                if (ContainsAnyTokenIn(tokens, teamKeywords))
                {
                    score -= 50.0;
                }

                if (IsFalseName(candidate))
                {
                    score -= 100.0;
                }

                if (score > 0.0)
                {
                    scored.Add((candidate, score));
                }
            }

            if (scored.Count == 0)
            {
                return string.Empty;
            }

            return scored
                .OrderByDescending(x => x.Score)
                .First()
                .Candidate;
        }

        private static bool ContainsAnyTokenIn(string[] tokens, string[] phraseList)
        {
            foreach (string phrase in phraseList)
            {
                if (string.IsNullOrWhiteSpace(phrase))
                {
                    continue;
                }

                string[] parts = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                foreach (string part in parts)
                {
                    foreach (string token in tokens)
                    {
                        if (string.Equals(token, part, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Filters out known bad "names": brands, subsets, and team names.
        /// This is used by both ALL-CAPS and TitleCase candidate logic.
        /// </summary>
        private static bool IsFalseName(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return true;
            }

            string candidate = s.Trim();

            string[] banned =
            {
                "In The Game","The Game","Club Level","Field Level",
                "Premier Level","Concourse Level","Select Certified",
                "Tim Hortons","Upper Deck","Panini","Topps","Prizm",
                "Draft Picks","Panini Illusions", "Panini Select",
                "Panini Prizm", "Upper Deck Ice", "Topps Chrome",
                "Panini Chronicles", "Panini Mosaic",
                "Rookie Holiday Sweaters","Rookie Holiday Sweater"
            };

            if (banned.Any(b =>
                    candidate.Equals(b, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (teamKeywords.Any(t =>
                    candidate.Equals(t, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }

        private static bool PokemonCardsFound(string title, string desc)
        {
            string t = title ?? string.Empty;
            string d = desc ?? string.Empty;

            return pokemonNames.Any(n =>
                t.Contains(n, StringComparison.OrdinalIgnoreCase) ||
                d.Contains(n, StringComparison.OrdinalIgnoreCase));
        }
    }
}
