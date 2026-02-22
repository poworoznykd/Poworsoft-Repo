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
*
*     This entity also implements INotifyPropertyChanged and is used as the
*     BindingContext (view-model) for CardPage so that UI elements (such as
*     the estimated value label) update automatically when properties change.
*/

using SQLite;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using static CollectIQ.Enums.Enums;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a single collectible card within a collection.
    /// Now composes real domain models through JSON-backed properties and
    /// supports data binding via INotifyPropertyChanged.
    /// </summary>
    public sealed class Card : BaseModel
    {
        private decimal? estimatedValue;
        /// <summary>
        /// Gets or sets the estimated value for this card based on pricing
        /// insights. This property is bound directly to the UI and formatted
        /// as currency via XAML in CardPage.xaml.
        /// </summary>
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
        /// <summary>
        /// Gets or sets the pricing and market insights for this card.
        /// This property is ignored by SQLite (not persisted directly) but
        /// is used to keep the EstimatedValue property synchronized when the
        /// SuggestedPrice inside CardInsights changes.
        /// </summary>
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

                // Unsubscribe from the old instance, if any.
                if (insights != null)
                {
                    insights.PropertyChanged -= OnInsightsPropertyChanged;
                }

                // Never allow Insights to be null; always provide a live object.
                insights = value ?? new CardInsights();

                // Subscribe to the new instance so that we can react when
                // SuggestedPrice or other properties change.
                insights.PropertyChanged += OnInsightsPropertyChanged;

                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Handles changes coming from CardInsights. When the SuggestedPrice
        /// is updated, we automatically refresh EstimatedValue so that the UI
        /// (which binds to EstimatedValue) shows the latest calculation.
        /// </summary>
        /// <param name="sender">The CardInsights object that raised the event.</param>
        /// <param name="e">Property change details.</param>
        private void OnInsightsPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CardInsights.SuggestedPrice))
            {
                // Adopt the suggested price as the card's estimated value.
                EstimatedValue = Insights?.SuggestedPrice ?? 0.00m;
            }
        }

        /// <summary>
        /// Default constructor. Ensures that Insights is always initialized and
        /// wired for change notifications.
        /// </summary>
        public Card()
        {
            Insights = new CardInsights();
        }

        // --------------------------------------------------------------------
        //  BASIC CARD FIELDS
        // --------------------------------------------------------------------

        [Indexed]
        public string CollectionId { get; set; } = string.Empty;

        /// <summary>
        /// Primary display title for the card.
        /// IMPORTANT: some pages historically bind to SelectedCard.Name.
        /// Title is the real stored field; Name is an alias (see below).
        /// </summary>
        [Indexed]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Alias for Title.
        /// Keeping this prevents blank UI when older XAML still references "Name".
        /// Not persisted as a separate column.
        /// </summary>
        [Ignore]
        public string Name
        {
            get => Title;
            set => Title = value ?? string.Empty;
        }

        public int? Year { get; set; }
        public string Set { get; set; } = string.Empty;
        public string Number { get; set; } = string.Empty;

        // --- Financial ---
        public decimal? PurchasePrice { get; set; }

        // --- Images ---
  
        private string? frontImagePath;
        private string? frontThumbnailPath;

        public string? FrontImagePath
        {
            get => frontImagePath;
            set
            {
                if (frontImagePath == value)
                    return;

                frontImagePath = value;
                OnPropertyChanged();
            }
        }

        public string? FrontThumbnailPath
        {
            get => frontThumbnailPath;
            set
            {
                if (frontThumbnailPath == value)
                    return;

                frontThumbnailPath = value;
                OnPropertyChanged();
            }
        }

        public string BackImagePath { get; set; } = string.Empty;

        /// <summary>
        /// Optional overlay image drawn over the front of the card
        /// (e.g., condition markings) by ImageViewerPage.
        /// </summary>
        public string FrontOverlayImagePath { get; set; } = string.Empty;

        /// <summary>
        /// Optional overlay image drawn over the back of the card
        /// (e.g., condition markings) by ImageViewerPage.
        /// </summary>
        public string BackOverlayImagePath { get; set; } = string.Empty;
        // --- Advanced fields ---
        // IMPORTANT (SQLite enum safety)
        // --------------------------
        // SQLite-net can persist enums as TEXT in some configurations and then uses Enum.Parse(...) when reading.
        // If an older row contains an empty string (""), Enum.Parse throws:
        //   "Must specify valid information for parsing in the string"
        // To permanently prevent this, we store the enum as an INT column and expose the enum via an [Ignore] wrapper.
        public int SportValue { get; set; } = 0;

        [Ignore]
        public CollectingCardCategory Sport
        {
            get => (CollectingCardCategory)SportValue;
            set => SportValue = (int)value;
        }
        public string Parallel { get; set; } = string.Empty;
        public string Subset { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;

        // ============================================================
        //   COMPOSITION PROPERTIES (IGNORED BY SQLITE)
        // ============================================================

        [Ignore]
        public Player Player
        {
            get => SafeDeserialize<Player>(PlayerJson) ?? new Player();
            set => PlayerJson = SafeSerialize(value);
        }

        [Ignore]
        public Team Team
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
        public HighlightReel Highlights
        {
            get => SafeDeserialize<HighlightReel>(HighlightJson) ?? new HighlightReel();
            set => HighlightJson = SafeSerialize(value);
        }

        // ============================================================
        //   DISPLAY / UI CONVENIENCE PROPERTIES (NOT STORED IN SQLITE)
        // ============================================================

        /// <summary>
        /// Primary line used in CollectionPage list items.
        /// Prefer extracted player name; fall back to the original title.
        /// </summary>
        [Ignore]
        public string DisplayName
        {
            get
            {
                string player = Player?.FullName ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(player))
                {
                    return player.Trim();
                }

                if (!string.IsNullOrWhiteSpace(Title))
                {
                    return Title.Trim();
                }

                return "(Untitled Card)";
            }
        }

        /// <summary>
        /// Display-friendly team name for list bindings.
        /// </summary>
        [Ignore]
        public string TeamName => Team?.Name ?? string.Empty;

        /// <summary>
        /// Display-friendly sport name for list bindings.
        /// </summary>
        [Ignore]
        public string SportName => Sport.ToString();


        // ============================================================
        //   JSON BACKING FIELDS (STORAGE ONLY, KEEP AS STRINGS)
        // ============================================================

        public string PlayerJson { get; set; } = "{}";
        public string TeamJson { get; set; } = "{}";
        public string GradingJson { get; set; } = "{}";
        public string HighlightJson { get; set; } = "{}";

        // ============================================================
        //   PRIVATE JSON HELPERS (SAFE FOR NULLS + BAD DATA)
        // ============================================================

        /// <summary>
        /// Safely deserializes JSON into a model type. Returns default(T) if
        /// the JSON is null, empty, or invalid.
        /// </summary>
        private static T? SafeDeserialize<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return default;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(json);
            }
            catch
            {
                return default;
            }
        }

        /// <summary>
        /// Safely serializes a model to JSON. For now this simply wraps the
        /// standard serializer but centralizes usage for future changes.
        /// </summary>
        private static string SafeSerialize<T>(T model)
        {
            return JsonSerializer.Serialize(model);
        }
    }
}