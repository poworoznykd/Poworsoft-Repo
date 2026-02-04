// -------------------------------------------------------------------------------------------------
// File: CardInsights.cs
// Description: Holds pricing and market insight data for a single sports card, derived primarily
//              from recent eBay search results.
// -------------------------------------------------------------------------------------------------

using System;
using System.ComponentModel;

namespace CollectIQ.Models
{
    public class CardInsights : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public CardInsights(decimal suggested = 0.00m)
        {
            SuggestedPrice = suggested;
        }

        private double? minPrice;
        public double? MinPrice
        {
            get => minPrice;
            set
            {
                if (minPrice != value)
                {
                    minPrice = value;
                    OnPropertyChanged();
                }
            }
        }

        private double? maxPrice;
        public double? MaxPrice
        {
            get => maxPrice;
            set
            {
                if (maxPrice != value)
                {
                    maxPrice = value;
                    OnPropertyChanged();
                }
            }
        }

        private double? medianPrice;
        public double? MedianPrice
        {
            get => medianPrice;
            set
            {
                if (medianPrice != value)
                {
                    medianPrice = value;
                    OnPropertyChanged();
                }
            }
        }

        private double? averagePrice;
        public double? AveragePrice
        {
            get => averagePrice;
            set
            {
                if (averagePrice != value)
                {
                    averagePrice = value;
                    OnPropertyChanged();
                }
            }
        }

        // Suggested fair value for the card based on comps
        private decimal? suggestedPrice;
        public decimal? SuggestedPrice
        {
            get => suggestedPrice;
            set
            {
                if (suggestedPrice != value)
                {
                    suggestedPrice = value;
                    OnPropertyChanged();
                }
            }
        }

        // Number of listings used to compute insights
        private int listingCount;
        public int ListingCount
        {
            get => listingCount;
            set
            {
                if (listingCount != value)
                {
                    listingCount = value;
                    OnPropertyChanged();
                }
            }
        }

        private string currency = "USD";
        public string Currency
        {
            get => currency;
            set
            {
                if (currency != value)
                {
                    currency = value;
                    OnPropertyChanged();
                }
            }
        }

        private DateTime? lastUpdatedUtc;
        public DateTime? LastUpdatedUtc
        {
            get => lastUpdatedUtc;
            set
            {
                if (lastUpdatedUtc != value)
                {
                    lastUpdatedUtc = value;
                    OnPropertyChanged();
                }
            }
        }

        // Short human-readable description of the market picture
        private string summary = string.Empty;
        public string Summary
        {
            get => summary;
            set
            {
                if (summary != value)
                {
                    summary = value;
                    OnPropertyChanged();
                }
            }
        }

        // The exact query that was used to fetch these insights
        private string queryUsed = string.Empty;
        public string QueryUsed
        {
            get => queryUsed;
            set
            {
                if (queryUsed != value)
                {
                    queryUsed = value;
                    OnPropertyChanged();
                }
            }
        }

        // 0.0–1.0 indicating how confident we are in the suggested price
        private double? confidenceScore;
        public double? ConfidenceScore
        {
            get => confidenceScore;
            set
            {
                if (confidenceScore != value)
                {
                    confidenceScore = value;
                    OnPropertyChanged();
                }
            }
        }


// ---------------------------------------------------------------------
// PriceCharting (price guide) fields
// ---------------------------------------------------------------------

/// <summary>
/// Raw / ungraded price from the price guide (PriceCharting "loose-price").
/// </summary>
public double? PriceGuideRawPrice { get; set; }

/// <summary>
/// Generic graded price from the price guide (PriceCharting "graded-price").
/// </summary>
public double? PriceGuideGradedPrice { get; set; }

/// <summary>
/// PSA 10 price from the price guide (PriceCharting "manual-only-price").
/// </summary>
public double? PriceGuidePsa10Price { get; set; }

/// <summary>
/// PSA 9.5 / BGS 9.5 style price from the price guide (PriceCharting "box-only-price").
/// </summary>
public double? PriceGuide95Price { get; set; }

/// <summary>
/// BGS 10 price from the price guide (PriceCharting "bgs-10-price").
/// </summary>
public double? PriceGuideBgs10Price { get; set; }

/// <summary>
/// CGC 10 price from the price guide (PriceCharting "condition-17-price").
/// </summary>
public double? PriceGuideCgc10Price { get; set; }

/// <summary>
/// SGC 10 price from the price guide (PriceCharting "condition-18-price").
/// </summary>
public double? PriceGuideSgc10Price { get; set; }

/// <summary>
/// Sales volume from the price guide (PriceCharting "sales-volume"), when provided.
/// </summary>
public int? PriceGuideSalesVolume { get; set; }

/// <summary>
/// The price we ultimately used as the baseline from the price guide (based on detected grade).
/// </summary>
public double? PriceGuideBaselineUsed { get; set; }

// ---------------------------------------------------------------------
// eBay breakdown fields (active listings)
// ---------------------------------------------------------------------

/// <summary>
/// Median price from eBay listings after filters/outlier removal.
/// </summary>
public double? EbayMedianPrice { get; set; }

/// <summary>
/// Average price from eBay listings after filters/outlier removal.
/// </summary>
public double? EbayAveragePrice { get; set; }

/// <summary>
/// Number of eBay listings used in the calculation.
/// </summary>
public int? EbayListingCountUsed { get; set; }

/// <summary>
/// Weight applied to eBay (0..1). The remainder is applied to PriceCharting baseline when enabled.
/// </summary>
public double? EbayBlendWeight { get; set; }

/// <summary>
/// Notes explaining which sources were used.
/// </summary>
public string? CalculationNotes { get; set; }

    }
}
