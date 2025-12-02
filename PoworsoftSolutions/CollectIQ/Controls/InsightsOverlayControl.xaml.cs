//
//  FILE            : InsightsOverlayControl.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-11-28
//  DESCRIPTION     :
//      Reusable semi-transparent overlay control that displays
//      market insights (sold/active comps) for a selected eBay
//      listing. The host page passes in the anchor listing and
//      a comps collection; this control calculates stats and
//      draws the mini price graph.
//

using CollectIQ.Models;
using Microsoft.Maui.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace CollectIQ.Controls
{
    /// <summary>
    /// Reusable overlay for displaying market insights for a card.
    /// </summary>
    public partial class InsightsOverlayControl : ContentView
    {
        private readonly ObservableCollection<EbayListing> _insightsListings;
        private EbayListing? _currentAnchor;

        // These drive the "Sold over last X days" text.
        private string _listingTypeFilter;
        private int _daysRangeFilter;

        /// <summary>
        /// Fired after the overlay finishes closing.
        /// </summary>
        public event EventHandler? Closed;

        public InsightsOverlayControl()
        {
            InitializeComponent();

            _insightsListings = new ObservableCollection<EbayListing>();
            InsightsListView.ItemsSource = _insightsListings;

            _listingTypeFilter = "sold";
            _daysRangeFilter = 90;

            InsightsOverlay.IsVisible = false;
            InsightsScrim.IsVisible = false;
            InsightsOverlay.Opacity = 0;
            InsightsScrim.Opacity = 0;
        }

        // =======================================================
        //      VALUE CALLBACK (Used to send value back to page)
        // =======================================================

        public decimal? SuggestedValue { get; private set; }

        // Called by EbaySearchPage when overlay is shown.
        // The overlay will invoke this when user closes the overlay
        // or clicks the "Apply Suggested Value" button.
        public Action<decimal?> OnEstimatedValueReady { get; set; }


        private void ApplySuggestedValue_Clicked(object sender, EventArgs e)
        {
            if (SuggestedValue.HasValue)
            {
                // Send value back to the page
                OnEstimatedValueReady?.Invoke(SuggestedValue);
            }
        }


        /// <summary>
        /// Shows the overlay with the given anchor listing and comps.
        /// The host page is responsible for providing the comps
        /// (e.g., from a search, collection page, etc.).
        /// </summary>
        /// <param name="anchorListing">Listing the overlay is describing.</param>
        /// <param name="comps">Collection of comps to analyze.</param>
        /// <param name="listingTypeFilter">
        /// "sold" for sold comps, anything else for active listings.
        /// </param>
        /// <param name="daysRangeFilter">
        /// Number of days to show in the description for sold comps.
        /// </param>
        public async Task ShowAsync(
            EbayListing anchorListing,
            IEnumerable<EbayListing> comps,
            string listingTypeFilter,
            int daysRangeFilter)
        {
            _currentAnchor = anchorListing;
            _listingTypeFilter = string.IsNullOrWhiteSpace(listingTypeFilter)
                ? "sold"
                : listingTypeFilter;
            _daysRangeFilter = daysRangeFilter <= 0 ? 90 : daysRangeFilter;

            _insightsListings.Clear();

            if (comps != null)
            {
                foreach (EbayListing comp in comps)
                {
                    if (comp != null)
                    {
                        _insightsListings.Add(comp);
                    }
                }
            }

            RecalculateInsightsFromCurrentComps();

            if (InsightsOverlay.IsVisible)
            {
                // Already visible – just refreshed the data.
                return;
            }

            InsightsOverlay.IsVisible = true;
            InsightsScrim.IsVisible = true;

            InsightsOverlay.Opacity = 0;
            InsightsOverlay.TranslationY = 60;
            InsightsScrim.Opacity = 0;

            await Task.WhenAll(
                InsightsOverlay.FadeTo(1, 180, Easing.CubicOut),
                InsightsOverlay.TranslateTo(0, 0, 180, Easing.CubicOut),
                InsightsScrim.FadeTo(1, 180, Easing.CubicOut));
        }

        /// <summary>
        /// Hides the overlay with a short slide/fade animation.
        /// </summary>
        public async Task HideAsync()
        {
            if (!InsightsOverlay.IsVisible && !InsightsScrim.IsVisible)
            {
                return;
            }

            await Task.WhenAll(
                InsightsScrim.FadeTo(0, 150, Easing.CubicIn),
                InsightsOverlay.FadeTo(0, 150, Easing.CubicIn),
                InsightsOverlay.TranslateTo(0, 60, 150, Easing.CubicIn));

            InsightsOverlay.IsVisible = false;
            InsightsScrim.IsVisible = false;

            // APPLY VALUE AUTOMATICALLY IF AVAILABLE
            if (SuggestedValue.HasValue)
            {
                OnEstimatedValueReady?.Invoke(SuggestedValue);
            }

            Closed?.Invoke(this, EventArgs.Empty);
        }

        #region Event Handlers

        private async void OnScrimTapped(object sender, TappedEventArgs e)
        {
            await HideAsync();
        }

        private async void OnCloseTapped(object sender, TappedEventArgs e)
        {
            await HideAsync();
        }

        /// <summary>
        /// Handles the "Remove" swipe action on a single comp row.
        /// Recalculates all metrics after removal.
        /// </summary>
        private void OnRemoveCompSwipe(object sender, EventArgs e)
        {
            try
            {
                EbayListing? comp = null;

                if (sender is SwipeItem swipeItem &&
                    swipeItem.CommandParameter is EbayListing parameterListing)
                {
                    comp = parameterListing;
                }

                if (comp == null)
                {
                    return;
                }

                if (_insightsListings.Contains(comp))
                {
                    _insightsListings.Remove(comp);
                }

                RecalculateInsightsFromCurrentComps();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[INSIGHTS REMOVE ERROR] {ex.Message}");
            }
        }

        #endregion

        #region Insights Calculation

        /// <summary>
        /// Recomputes all metrics (count, min, max, avg, median, suggested,
        /// volatility) and refreshes the graph.
        /// </summary>
        private void RecalculateInsightsFromCurrentComps()
        {
            List<EbayListing> listingList = _insightsListings.ToList();

            if (listingList.Count == 0)
            {
                InsightsTitleLabel.Text = _currentAnchor?.Title ?? "Selected Card";
                InsightsSummaryLabel.Text = "No market data available.";
                InsightsCountValue.Text = "0";
                InsightsMinValue.Text = "$0.00";
                InsightsMaxValue.Text = "$0.00";
                InsightsAvgValue.Text = "$0.00";
                InsightsMedianValue.Text = "$0.00";
                InsightsSuggestedValue.Text = "$0.00";

                InsightsStatsLabel.Text = "No comps.";
                InsightsRangeLabel.Text = string.Empty;
                InsightsGraphLayout.Children.Clear();
                return;
            }

            List<decimal> prices = listingList
                .Where(l => l != null && l.Price.HasValue && l.Price.Value > 0m)
                .Select(l => l.Price!.Value)
                .OrderBy(p => p)
                .ToList();

            if (prices.Count == 0)
            {
                InsightsTitleLabel.Text = _currentAnchor?.Title ?? "Selected Card";
                InsightsSummaryLabel.Text = "No valid price data available.";
                InsightsCountValue.Text = listingList.Count.ToString();
                InsightsMinValue.Text = "$0.00";
                InsightsMaxValue.Text = "$0.00";
                InsightsAvgValue.Text = "$0.00";
                InsightsMedianValue.Text = "$0.00";
                InsightsSuggestedValue.Text = "$0.00";

                InsightsStatsLabel.Text = "No valid prices.";
                InsightsRangeLabel.Text = string.Empty;
                InsightsGraphLayout.Children.Clear();
                return;
            }

            int count = prices.Count;
            decimal min = prices.First();
            decimal max = prices.Last();
            decimal avg = prices.Average();
            decimal median = ComputeMedian(prices);

            decimal q25 = Percentile(prices, 0.25);
            decimal q75 = Percentile(prices, 0.75);

            decimal suggested = Math.Round(median * 0.95m, 2);
            SuggestedValue = suggested;
            // Volatility description
            decimal spread = max - min;
            string volatility;
            if (avg <= 0 || spread <= 0)
            {
                volatility = "No volatility data.";
            }
            else
            {
                decimal ratio = spread / avg;
                if (ratio < 0.3m)
                {
                    volatility = "Tight, consistent pricing.";
                }
                else if (ratio < 0.7m)
                {
                    volatility = "Moderate price spread.";
                }
                else
                {
                    volatility = "Highly volatile market.";
                }
            }

            // Where anchor listing sits, if we know it
            string positionText = string.Empty;
            if (_currentAnchor != null &&
                _currentAnchor.Price.HasValue &&
                _currentAnchor.Price.Value > 0m &&
                prices.Count > 0)
            {
                decimal anchorPrice = _currentAnchor.Price.Value;

                if (anchorPrice <= q25)
                {
                    positionText = "This listing is in the lowest 25% of prices (potential bargain).";
                }
                else if (anchorPrice >= q75)
                {
                    positionText = "This listing is in the highest 25% of prices (top-end or overpriced).";
                }
                else
                {
                    positionText = "This listing is in the mid-range of current prices (fair market).";
                }
            }

            // Update overlay labels
            InsightsTitleLabel.Text = _currentAnchor?.Title ?? "Selected Card";

            InsightsCountValue.Text = count.ToString();
            InsightsMinValue.Text = $"${min:F2}";
            InsightsMaxValue.Text = $"${max:F2}";
            InsightsAvgValue.Text = $"${avg:F2}";
            InsightsMedianValue.Text = $"${median:F2}";
            InsightsSuggestedValue.Text = $"${suggested:F2}";

            InsightsSummaryLabel.Text =
                $"Median around ${median:F2}. Range ${min:F2} – ${max:F2}. {volatility} {positionText}";

            InsightsStatsLabel.Text = $"Count: {count}  Avg: ${avg:F2}  Min: ${min:F2}  Max: ${max:F2}";

            InsightsRangeLabel.Text =
                string.Equals(_listingTypeFilter, "sold", StringComparison.OrdinalIgnoreCase)
                    ? $"Sold comps over last {_daysRangeFilter} days"
                    : "Active listings only";

            // Rebuild chart
            BuildInsightsPriceChart(
                _currentAnchor ?? new EbayListing { Price = median },
                prices,
                min,
                max,
                median,
                q25,
                q75);
        }

        private static decimal ComputeMedian(List<decimal> sortedPrices)
        {
            int n = sortedPrices.Count;
            if (n == 0)
            {
                return 0;
            }

            if (n % 2 == 1)
            {
                return sortedPrices[n / 2];
            }

            decimal a = sortedPrices[(n / 2) - 1];
            decimal b = sortedPrices[n / 2];
            return (a + b) / 2m;
        }

        private static decimal Percentile(List<decimal> sortedPrices, double percentile)
        {
            if (sortedPrices.Count == 0)
            {
                return 0;
            }

            if (percentile <= 0)
            {
                return sortedPrices.First();
            }

            if (percentile >= 1)
            {
                return sortedPrices.Last();
            }

            double index = (sortedPrices.Count - 1) * percentile;
            int lower = (int)Math.Floor(index);
            int upper = (int)Math.Ceiling(index);

            if (lower == upper)
            {
                return sortedPrices[lower];
            }

            decimal lowerVal = sortedPrices[lower];
            decimal upperVal = sortedPrices[upper];
            double frac = index - lower;

            return lowerVal + (decimal)frac * (upperVal - lowerVal);
        }

        private void BuildInsightsPriceChart(
            EbayListing selected,
            List<decimal> sortedPrices,
            decimal min,
            decimal max,
            decimal median,
            decimal q25,
            decimal q75)
        {
            InsightsGraphLayout.Children.Clear();

            if (sortedPrices.Count == 0 || max <= min)
            {
                return;
            }

            double minHeight = 18;   // short "blip" bars
            double maxHeight = 60;   // tall bar

            decimal? selectedPrice = selected.Price;

            void AddBar(string labelText, decimal price, string colorHex)
            {
                if (price <= 0)
                {
                    return;
                }

                double ratio = (double)((price - min) / (max - min));
                double height = minHeight + ratio * (maxHeight - minHeight);

                var bar = new BoxView
                {
                    WidthRequest = 14,
                    HeightRequest = height,
                    Margin = new Thickness(3, 0, 3, 0),
                    CornerRadius = 4,
                    Color = Color.FromArgb(colorHex),
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.End
                };

                var priceLabel = new Label
                {
                    Text = $"${price:F0}",
                    FontSize = 10,
                    TextColor = Color.FromArgb(colorHex),
                    HorizontalTextAlignment = TextAlignment.Center
                };

                var nameLabel = new Label
                {
                    Text = labelText,
                    FontSize = 10,
                    TextColor = Colors.White,
                    HorizontalTextAlignment = TextAlignment.Center
                };

                var stack = new VerticalStackLayout
                {
                    Spacing = 2,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.End
                };

                stack.Children.Add(bar);
                stack.Children.Add(priceLabel);
                stack.Children.Add(nameLabel);

                InsightsGraphLayout.Children.Add(stack);
            }

            // Build the summary bars in a fixed, readable order
            AddBar("Min", min, "#7CFC7C");       // green
            AddBar("Q1", q25, "#66FFAA");        // lighter green
            AddBar("Median", median, "#00E5FF"); // bright cyan

            if (selectedPrice.HasValue)
            {
                AddBar("Selected", selectedPrice.Value, "#FF4B4B"); // hot red
            }

            AddBar("Q3", q75, "#FFD966");        // yellow-ish
            AddBar("Max", max, "#FFB347");       // orange
        }

        #endregion
    }
}
