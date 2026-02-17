using CollectIQ.Utilities;
using SQLite;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a single eBay listing or sold item.
    /// This is the ONLY model used by the app for eBay results.
    /// </summary>
    public class EbayListing : INotifyPropertyChanged
    {
        public string DisplayTitle => BuildDisplayTitle();
        // IMPORTANT: implement INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        private bool isSelected;

        /// <summary>
        /// Indicates whether this listing is currently selected in the UI.
        /// Used to drive Border highlight.
        /// </summary>
        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected != value)
                {
                    isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        private decimal? estimatedValue;

        /// <summary>
        /// Indicates whether this listing is currently selected in the UI.
        /// Used to drive Border highlight.
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

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>
        /// eBay itemId / itemSaleId.
        /// </summary>
        public string ListingId { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable title from eBay.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable title from eBay.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Main image URL to show in the results list.
        /// </summary>
        public string ImageUrl { get; set; } = string.Empty;

        /// <summary>
        /// URL to open in the browser when user taps a card.
        /// </summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// Current or sold price value (numeric).
        /// </summary>
        public decimal? Price { get; set; }

        


/// <summary>
/// If this listing is an auction, this represents the current bid amount (when available).
/// </summary>
public decimal? CurrentBidPrice { get; set; }

/// <summary>
/// If this listing offers Buy It Now, this represents the Buy It Now amount (when available).
/// </summary>
public decimal? BuyNowPrice { get; set; }

/// <summary>
/// True when we believe this listing is an auction (best-effort based on fields we have).
/// </summary>
public bool IsAuction { get; set; }
/// <summary>
        /// ISO currency code, e.g. USD, CAD, EUR.
        /// </summary>
        public string Currency { get; set; } = string.Empty;

        /// <summary>
        /// Status: "ACTIVE", "SOLD", etc. 
        /// Used for filtering/tabs.
        /// </summary>
        public string Status { get; set; } = "ACTIVE";

        /// <summary>
        /// When the item was sold/ended (if known).
        /// </summary>
        public DateTime? EndDateUtc { get; set; }

        /// <summary>
        /// Optional shipping cost if you want to show it later.
        /// </summary>
        public decimal? ShippingCost { get; set; }

        /// <summary>
        /// Helper: formatted "pretty" price (used by XAML bindings).
        /// </summary>
        public string FormattedPrice => FormatPrice(Price, Currency);

        public string SoldDateDisplay =>
        EndDateUtc.HasValue
            ? EndDateUtc.Value.ToLocalTime().ToString("yyyy-MM-dd")
            : string.Empty;


        public bool IsSold =>
            Status.Equals("SOLD", StringComparison.OrdinalIgnoreCase);

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Title)
                ? base.ToString() ?? string.Empty
                : $"{Title} - {FormattedPrice}";
        }

        private string BuildDisplayTitle()
        {
            // Try to extract player name from title
            // (use the same logic as CardMetadataParser)
            string extractedPlayer = CardMetadataParser.Parse(this as EbayListing).Player.FullName;

            if (!string.IsNullOrWhiteSpace(extractedPlayer))
            {
                return $"{extractedPlayer} - {Title}";
            }

            return Title;
        }
        /// <summary>
        /// Utility that formats a price nicely for display.
        /// </summary>
        public static string FormatPrice(decimal? price, string currencyCode)
        {
            if (!price.HasValue)
            {
                return string.Empty;
            }

            string code = string.IsNullOrWhiteSpace(currencyCode)
                ? "USD"
                : currencyCode.Trim().ToUpperInvariant();

            decimal value = price.Value;

            return code switch
            {
                "USD" => $"${value:0.00}",
                "CAD" => $"C${value:0.00}",
                "EUR" => $"€{value:0.00}",
                "GBP" => $"£{value:0.00}",
                _ => $"{value:0.00} {code}"
            };
        }
    }
}
