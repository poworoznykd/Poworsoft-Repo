/*
* FILE: CardMetadataParser.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-12-01
* UPDATED: 2026-02-21
* DESCRIPTION:
*     Metadata parser for eBay listings.
*     - Extracts year, set, card number, serial (#/99), team, parallels, subsets, grading.
*     - Uses BOTH Title + Description for extraction.
*     - Uses local CSV-based player lists (NFL/NBA/MLB/NHL) before regex fallback.
*
* IMPORTANT IMPLEMENTATION NOTE:
*     Card.Team, Card.Player, and Card.Grading are JSON-backed convenience properties.
*     Their getters deserialize from JSON into a NEW object each time.
*     That means this is WRONG (changes get lost): card.Team.Name = "Oilers";
*     The correct pattern is:
*         var team = card.Team;
*         team.Name = "Oilers";
*         card.Team = team;
*/

using CollectIQ.Models;
using Microsoft.Maui.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using static CollectIQ.Enums.Enums;

namespace CollectIQ.Utilities
{
    public static class CardMetadataParser
    {
        // -----------------------------------------------------------------
        //  STATIC CONFIGURATION
        // -----------------------------------------------------------------

        private static readonly string[] setKeywords =
        {
            "Prizm","Panini Prizm","Panini","Select","Mosaic","Optic","Donruss",
            "Topps","Topps Chrome","Bowman","Upper Deck","Fleer","Chronicles",
            "Score","Illusions","Revolution","Phoenix","Absolute","SP Authentic",
            "Tim Hortons","Artifacts","O-Pee-Chee","Ultimate","Premier"
        };

        private static readonly string[] pokemonSets =
        {
            "Base Set","Jungle","Fossil","Team Rocket","Gym Heroes","Gym Challenge",
            "Neo Genesis","Neo Discovery","Neo Revelation","Neo Destiny",
            "e-Reader","Ruby Sapphire","Diamond & Pearl","HeartGold SoulSilver",
            "Black & White","XY","Sun & Moon","Sword & Shield","Scarlet & Violet",
            "Champion's Path","Evolving Skies","Vivid Voltage","Celebrations"
        };

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

        private static readonly string[] parallelKeywords =
        {
            "Refractor","XFractor","Superfractor","Mojo","Silver","Holo",
            "Wave","Hyper","Pulsar","Checkerboard","Cracked Ice","Green Ice",
            "Blue", "Orange", "Red", "Gold", "Black","Rainbow","Spectrum"
        };

        private static readonly string[] subsetKeywords =
        {
            "Canvas","Canvas Legends","UD Canvas Legends","My House","Downtown",
            "Fireworks","Shock","Stargazing","Phenoms","All-Star","Rookie",
            "Legend","Legends","Retro","Throwback","Holiday Sweaters","Holiday Sweater"
        };

        private static readonly string[] pokemonNames =
        {
            "Pikachu","Charizard","Blastoise","Venusaur","Mew","Mewtwo","Eevee",
            "Gengar","Snorlax","Arceus","Umbreon","Espeon","Rayquaza","Lugia"
        };

        // -----------------------------------------------------------------
        //  PLAYER CSV SUPPORT
        // -----------------------------------------------------------------

        private sealed class PlayerNameEntry
        {
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
        }

        private static readonly Lazy<List<PlayerNameEntry>> nflPlayers =
            new Lazy<List<PlayerNameEntry>>(() => LoadPlayersFromCsv("Data/NFL_players.csv"));

        private static readonly Lazy<List<PlayerNameEntry>> nbaPlayers =
            new Lazy<List<PlayerNameEntry>>(() => LoadPlayersFromCsv("Data/NBA_players.csv"));

        private static readonly Lazy<List<PlayerNameEntry>> mlbPlayers =
            new Lazy<List<PlayerNameEntry>>(() => LoadPlayersFromCsv("Data/MLB_players.csv"));

        private static readonly Lazy<List<PlayerNameEntry>> nhlPlayers =
            new Lazy<List<PlayerNameEntry>>(() => LoadPlayersFromCsv("Data/NHL_players.csv"));

        private static List<PlayerNameEntry> LoadPlayersFromCsv(string assetName)
        {
            List<PlayerNameEntry> players = new List<PlayerNameEntry>();

            string fileOnly = Path.GetFileName(assetName);

            string[] candidates =
            {
                assetName,
                $"Resources/{assetName}",
                fileOnly,
                $"Resources/{fileOnly}"
            };

            foreach (string candidate in candidates)
            {
                try
                {
                    using Stream stream = FileSystem.OpenAppPackageFileAsync(candidate)
                        .GetAwaiter()
                        .GetResult();

                    using StreamReader reader = new StreamReader(stream);

                    // Skip header
                    reader.ReadLine();

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

                        if (!string.IsNullOrWhiteSpace(entry.FullName) &&
                            !entry.FullName.Equals("FullName", StringComparison.OrdinalIgnoreCase))
                        {
                            players.Add(entry);
                        }
                    }

                    return players;
                }
                catch (FileNotFoundException)
                {
                    // try next name
                }
                catch
                {
                    // If parsing fails, return whatever we loaded so far.
                    return players;
                }
            }

            return players;
        }

        private static string TryFindPlayerNameFromCsv(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string normalized = NormalizeForNameSearch(text);
            string padded = " " + normalized + " ";

            string bestName = string.Empty;
            int bestScore = 0;

            bestScore = EvaluateLeagueList(nflPlayers.Value, padded, ref bestName, bestScore);
            bestScore = EvaluateLeagueList(nbaPlayers.Value, padded, ref bestName, bestScore);
            bestScore = EvaluateLeagueList(mlbPlayers.Value, padded, ref bestName, bestScore);
            bestScore = EvaluateLeagueList(nhlPlayers.Value, padded, ref bestName, bestScore);

            return bestName;
        }

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

                int score = 0;

                if (!string.IsNullOrWhiteSpace(full))
                {
                    string fullNorm = NormalizeForNameSearch(full);
                    string needle = " " + fullNorm + " ";
                    if (paddedNormalizedText.Contains(needle, StringComparison.Ordinal))
                    {
                        score = Math.Max(score, 200 + fullNorm.Length);
                    }
                }

                if (!string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(last))
                {
                    string firstNeedle = " " + NormalizeForNameSearch(first) + " ";
                    string lastNeedle = " " + NormalizeForNameSearch(last) + " ";

                    bool firstIn = paddedNormalizedText.Contains(firstNeedle, StringComparison.Ordinal);
                    bool lastIn = paddedNormalizedText.Contains(lastNeedle, StringComparison.Ordinal);

                    if (firstIn && lastIn)
                    {
                        score = Math.Max(score, 150 + firstNeedle.Length + lastNeedle.Length);
                    }
                }

                if (score > currentBestScore)
                {
                    currentBestScore = score;
                    currentBestName = !string.IsNullOrWhiteSpace(full)
                        ? full
                        : $"{CapitalizeName(first)} {CapitalizeName(last)}";
                }
            }

            return currentBestScore;
        }

        private static string NormalizeForNameSearch(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string lower = text.ToLowerInvariant();
            lower = Regex.Replace(lower, @"[^a-z0-9\s]", " ");
            lower = Regex.Replace(lower, @"\s+", " ").Trim();
            return lower;
        }

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

            return char.ToUpperInvariant(value[0]) + value.Substring(1).ToLowerInvariant();
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
            string combined = (title + " " + desc).Trim();

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
            if (yearMatch.Success && int.TryParse(yearMatch.Value, out int parsedYear))
            {
                card.Year = parsedYear;
            }

            // SERIAL NUMBER (#/99)
            Match serial = Regex.Match(combined, @"(\d{1,3}\/\d{1,3})");
            if (serial.Success)
            {
                card.SerialNumber = serial.Value;
            }

            // CARD NUMBER (#205, #A-PICK)
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
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(card.Set))
            {
                foreach (string set in pokemonSets)
                {
                    if (combined.Contains(set, StringComparison.OrdinalIgnoreCase))
                    {
                        card.Set = set;
                        break;
                    }
                }
            }

            // TEAM (JSON-backed property: must assign back!)
            foreach (string teamName in teamKeywords)
            {
                if (combined.Contains(teamName, StringComparison.OrdinalIgnoreCase))
                {
                    Team team = card.Team;
                    team.Name = teamName;
                    card.Team = team;
                    break;
                }
            }

            // PARALLEL
            foreach (string par in parallelKeywords)
            {
                if (combined.Contains(par, StringComparison.OrdinalIgnoreCase))
                {
                    card.Parallel = par;
                    break;
                }
            }

            // SUBSET / INSERT
            foreach (string sub in subsetKeywords)
            {
                if (combined.Contains(sub, StringComparison.OrdinalIgnoreCase))
                {
                    card.Subset = sub;
                    break;
                }
            }

            // GRADE & GRADER (JSON-backed property: must assign back!)
            ApplyGradingIfPresent(card, combined);

            // NAME (CSV-first, then fallback)
            string extractedName = ExtractName(title, desc);
            if (!string.IsNullOrWhiteSpace(extractedName))
            {
                Player player = card.Player;
                player.FullName = extractedName;
                card.Player = player;
            }

            // Sports/Pokemon flag (best-effort)
            if (PokemonCardsFound(title, desc))
            {
                card.Sport = CollectingCardCategory.Pokemon;
            }

            return card;
        }

        // -----------------------------------------------------------------
        //  GRADING
        // -----------------------------------------------------------------

        private static void ApplyGradingIfPresent(Card card, string combined)
        {
            if (card == null || string.IsNullOrWhiteSpace(combined))
            {
                return;
            }

            Match psa = Regex.Match(combined, @"PSA\s?(\d+(?:\.\d)?)", RegexOptions.IgnoreCase);
            if (psa.Success)
            {
                if (double.TryParse(psa.Groups[1].Value, out double grade))
                {
                    Grading g = card.Grading;
                    g.Company = "PSA";
                    g.Grade = grade;
                    card.Grading = g;
                }

                return;
            }

            Match bgs = Regex.Match(combined, @"BGS\s?(\d+(?:\.\d)?)", RegexOptions.IgnoreCase);
            if (bgs.Success)
            {
                if (double.TryParse(bgs.Groups[1].Value, out double grade))
                {
                    Grading g = card.Grading;
                    g.Company = "BGS";
                    g.Grade = grade;
                    card.Grading = g;
                }

                return;
            }

            Match cgc = Regex.Match(combined, @"CGC\s?(\d+(?:\.\d)?)", RegexOptions.IgnoreCase);
            if (cgc.Success)
            {
                if (double.TryParse(cgc.Groups[1].Value, out double grade))
                {
                    Grading g = card.Grading;
                    g.Company = "CGC";
                    g.Grade = grade;
                    card.Grading = g;
                }

                return;
            }

            Match sgc = Regex.Match(combined, @"SGC\s?(\d+(?:\.\d)?)", RegexOptions.IgnoreCase);
            if (sgc.Success)
            {
                if (double.TryParse(sgc.Groups[1].Value, out double grade))
                {
                    Grading g = card.Grading;
                    g.Company = "SGC";
                    g.Grade = grade;
                    card.Grading = g;
                }
            }
        }

        // -----------------------------------------------------------------
        //  NAME EXTRACTION
        // -----------------------------------------------------------------

        private static string ExtractName(string title, string desc)
        {
            string combined = (title ?? string.Empty) + " " + (desc ?? string.Empty);

            // PRIORITY 0: CSV-based lookup.
            string csvName = TryFindPlayerNameFromCsv(combined);
            if (!string.IsNullOrWhiteSpace(csvName))
            {
                return csvName;
            }

            // Pokémon short-circuit.
            foreach (string p in pokemonNames)
            {
                if (combined.Contains(p, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }

            // All-caps candidate (e.g. "BAKER MAYFIELD")
            string caps = FindBestAllCapsNameCandidate(combined);
            if (!string.IsNullOrWhiteSpace(caps) && !IsFalseName(caps))
            {
                return caps;
            }

            // TitleCase candidate (e.g. "Connor McDavid")
            string human = FindBestHumanNameCandidate(combined);
            if (!string.IsNullOrWhiteSpace(human) && !IsFalseName(human))
            {
                return human;
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
                if (candidate.Length < 4)
                {
                    continue;
                }

                if (!IsFalseName(candidate))
                {
                    candidates.Add(candidate);
                }
            }

            return candidates.Count == 0
                ? string.Empty
                : candidates.OrderByDescending(c => c.Length).First();
        }

        private static string FindBestHumanNameCandidate(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            MatchCollection matches = Regex.Matches(
                text,
                @"\b([A-Z][a-z]+(?: [A-Z][a-z]+){1,2})\b");

            if (matches.Count == 0)
            {
                return string.Empty;
            }

            List<(string Candidate, double Score)> scored = new List<(string Candidate, double Score)>();
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

                string[] tokens = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                double score = candidate.Length;

                if (tokens.Length >= 2 && tokens.Length <= 3)
                {
                    score += 40.0;
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

                if (!IsFalseName(candidate) && score > 0.0)
                {
                    scored.Add((candidate, score));
                }
            }

            return scored.Count == 0
                ? string.Empty
                : scored.OrderByDescending(x => x.Score).First().Candidate;
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

            if (banned.Any(b => candidate.Equals(b, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (teamKeywords.Any(t => candidate.Equals(t, StringComparison.OrdinalIgnoreCase)))
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
