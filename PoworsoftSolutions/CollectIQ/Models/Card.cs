/*
* FILE            : Card.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2025-10-28
* UPDATED         : 2025-12-09
* DESCRIPTION     :
*     Represents a collectible card record within the user’s collection.
*     Now supports composition-based domain models (Player, Team, Grading,
*     MarketData, HighlightReel) using JSON serialization for backwards-
*     compatible storage.
*/

using CollectIQ.Domain.Entities;
using CollectIQ.Models.Domain;
using CollectIQ.Models.Domain.Entities;
using SQLite;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a single collectible card within a collection.
    /// Now composes real domain models through JSON-backed properties.
    /// </summary>
    public sealed class Card : BaseEntity, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;


        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        // Example EstimatedValue property (adjust type to match your model)
        private decimal? estimatedValue;
        public decimal? EstimatedValue
        {
            get => estimatedValue;
            set
            {
                if (estimatedValue != value)
                {
                    estimatedValue = value;
                    OnPropertyChanged();
                }
            }
        }

        private CardInsights insights = new CardInsights();


        [Ignore]
        public CardInsights Insights
        {
            get => insights;
            set
            {
                if (insights == value)
                {
                    return;
                }

                // Unsubscribe from old instance
                if (insights != null)
                {
                    insights.PropertyChanged -= OnInsightsPropertyChanged;
                }

                // Never allow null – if someone sets null, create a new instance
                insights = value ?? new CardInsights();

                // Subscribe to new instance
                insights.PropertyChanged += OnInsightsPropertyChanged;

                OnPropertyChanged();
            }
        }

        private void OnInsightsPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CardInsights.SuggestedPrice))
            {
                // If SuggestedPrice is decimal?:
                EstimatedValue = Insights?.SuggestedPrice ?? 0.00m;

                // If SuggestedPrice is double? and EstimatedValue is double:
                // EstimatedValue = Insights.SuggestedPrice ?? 0.00;
            }
        }

        public Card()
        {
            // Go through the property so event subscription is wired up
            Insights = new CardInsights();
        }

        [Indexed]
        public string CollectionId { get; set; } = string.Empty;

        [Indexed]
        public string Title { get; set; } = string.Empty;

        [Indexed]
        public string Name { get; set; } = string.Empty;

        [Indexed]
        public string Team { get; set; } = string.Empty;

        public int? Year { get; set; }
        public string Set { get; set; } = string.Empty;
        public string Number { get; set; } = string.Empty;

        // --- Grading ---
        public string GradeCompany { get; set; } = "None";
        public double? Grade { get; set; }

        // --- Financial ---
        public decimal? PurchasePrice { get; set; }

        // --- Images ---
        public string FrontImagePath { get; set; } = string.Empty;
        public string BackImagePath { get; set; } = string.Empty;

        // --- Advanced fields ---
        public string Sport { get; set; } = string.Empty;
        public string Parallel { get; set; } = string.Empty;
        public string Subset { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;

        // --- Subgrades ---
        public string Grader { get; set; } = string.Empty;
        public double? SubgradeCorners { get; set; }
        public double? SubgradeEdges { get; set; }
        public double? SubgradeSurface { get; set; }
        public double? SubgradeCentering { get; set; }


        // ============================================================
        //   NEW JSON BACKING FIELDS (Storage Only, Keep As Strings)
        // ============================================================

        public string PlayerJson { get; set; } = "{}";
        public string TeamJson { get; set; } = "{}";
        public string GradingJson { get; set; } = "{}";
        public string MarketJson { get; set; } = "{}";
        public string HighlightJson { get; set; } = "{}";


        // ============================================================
        //   NEW COMPOSITION PROPERTIES (Ignored by SQLite)
        // ============================================================

        [Ignore]
        public Player Player
        {
            get => SafeDeserialize<Player>(PlayerJson) ?? new Player();
            set => PlayerJson = SafeSerialize(value);
        }

        [Ignore]
        public Team TeamDetails
        {
            get => SafeDeserialize<Team>(TeamJson) ?? new Team();
            set => TeamJson = SafeSerialize(value);
        }

        [Ignore]
        public Grading Grading
        {
            get => SafeDeserialize<Grading>(GradingJson) ?? new Grading();
            set => GradingJson = SafeSerialize(value);
        }

        [Ignore]
        public MarketData Market
        {
            get => SafeDeserialize<MarketData>(MarketJson) ?? new MarketData();
            set => MarketJson = SafeSerialize(value);
        }

        [Ignore]
        public HighlightReel Highlights
        {
            get => SafeDeserialize<HighlightReel>(HighlightJson) ?? new HighlightReel();
            set => HighlightJson = SafeSerialize(value);
        }

        // ============================================================
        //   PRIVATE JSON HELPERS (Safe for nulls + bad data)
        // ============================================================

        private static T? SafeDeserialize<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return default;

            try
            {
                return JsonSerializer.Deserialize<T>(json);
            }
            catch
            {
                return default;
            }
        }

        private static string SafeSerialize<T>(T model)
        {
            return JsonSerializer.Serialize(model);
        }
    }
}
