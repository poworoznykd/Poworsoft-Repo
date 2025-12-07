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
using System.ComponentModel;
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
        public CardInsights? InsightsData { get; set; }

        private readonly ObservableCollection<EbayListing> insightsListings;
        private EbayListing? currentAnchor;

        // These drive the "Sold over last X days" text.
        private string listingTypeFilter;
        private int daysRangeFilter;

        /// <summary>
        /// Fired after the overlay finishes closing.
        /// </summary>
        public event EventHandler? Closed;

        public InsightsOverlayControl()
        {
            InitializeComponent();

            insightsListings = new ObservableCollection<EbayListing>();
            InsightsListView.ItemsSource = insightsListings;

            listingTypeFilter = "sold";
            daysRangeFilter = 90;

            InsightsOverlay.IsVisible = false;
            InsightsScrim.IsVisible = false;
            InsightsOverlay.Opacity = 0;
            InsightsScrim.Opacity = 0;
        }

        // =======================================================
        //      VALUE CALLBACK (Used to send value back to page)
        // =======================================================

        // Called by EbaySearchPage when overlay is shown.
        // The overlay will invoke this when user closes the overlay
        // or clicks the "Apply Suggested Value" button.
        public Action<decimal?> OnEstimatedValueReady { get; set; }


        private void ApplySuggestedValue_Clicked(object sender, EventArgs e)
        {
            if (InsightsData != null && InsightsData.SuggestedPrice.HasValue)
            {
                // Send value back to the page
                OnEstimatedValueReady?.Invoke((decimal)InsightsData.SuggestedPrice);
            }
        }


        public async Task ShowAsync(
            EbayListing anchorListing,
            IEnumerable<EbayListing> comps,
            string listingTypeFilter,
            int daysRangeFilter)
        {
            System.Diagnostics.Debug.WriteLine("[Insights] Entering ShowAsync.");

            // 1. Make sure the visual elements are actually wired up
            if (InsightsOverlay == null || InsightsScrim == null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Insights] UI not ready. " +
                    $"InsightsOverlay null: {InsightsOverlay == null}, " +
                    $"InsightsScrim null: {InsightsScrim == null}. " +
                    "Check x:Name in XAML and InitializeComponent().");

                // No UI to animate – bail out safely.
                return;
            }

            // 2. Normalize / store inputs
            currentAnchor = anchorListing;
            this.listingTypeFilter = string.IsNullOrWhiteSpace(listingTypeFilter)
                ? "sold"
                : listingTypeFilter;
            this.daysRangeFilter = daysRangeFilter <= 0 ? 90 : daysRangeFilter;

            // 3. Populate the internal comps collection
            insightsListings.Clear();

            if (comps != null)
            {
                foreach (EbayListing comp in comps)
                {
                    if (comp != null)
                    {
                        insightsListings.Add(comp);
                    }
                }
            }

            // 4. Recalculate insights based on the current comps
            RecalculateInsightsFromCurrentComps();

            // 5. If already visible, we only needed to refresh the data
            if (InsightsOverlay.IsVisible)
            {
                System.Diagnostics.Debug.WriteLine("[Insights] Overlay already visible, data refreshed.");
                return;
            }

            // 6. Prepare initial visual state for the animation
            InsightsOverlay.IsVisible = true;
            InsightsScrim.IsVisible = true;

            InsightsOverlay.Opacity = 0;
            InsightsOverlay.TranslationY = 60;
            InsightsScrim.Opacity = 0;

            System.Diagnostics.Debug.WriteLine(
                $"[Insights] Starting animation. " +
                $"InsightsOverlay null: {InsightsOverlay == null}, " +
                $"InsightsScrim null: {InsightsScrim == null}");

            // 7. Animate in (wrapped in try/catch so we can see any failures clearly)
            try
            {
                await Task.WhenAll(
                    InsightsOverlay.FadeTo(1, 180, Easing.CubicOut),
                    InsightsOverlay.TranslateTo(0, 0, 180, Easing.CubicOut),
                    InsightsScrim.FadeTo(1, 180, Easing.CubicOut));

                System.Diagnostics.Debug.WriteLine("[Insights] ShowAsync completed successfully.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Insights] Animation error: " + ex);

                // Optional: hide again if animation fails
                // InsightsOverlay.IsVisible = false;
                // InsightsScrim.IsVisible = false;

                throw; // keep this while debugging so you see the stack trace
            }
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
            if (InsightsData != null && InsightsData.SuggestedPrice.HasValue)
            {
                OnEstimatedValueReady?.Invoke((decimal)InsightsData.SuggestedPrice.Value);
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

                if (insightsListings.Contains(comp))
                {
                    insightsListings.Remove(comp);
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
            List<EbayListing> listingList = insightsListings.ToList();

            if (listingList.Count == 0)
            {
                InsightsTitleLabel.Text = currentAnchor?.Title ?? "Selected Card";
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
                InsightsTitleLabel.Text = currentAnchor?.Title ?? "Selected Card";
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
            if(InsightsData == null)
            {
                InsightsData = new CardInsights();
            }
            InsightsData.SuggestedPrice = suggested;
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
            if (currentAnchor != null &&
                currentAnchor.Price.HasValue &&
                currentAnchor.Price.Value > 0m &&
                prices.Count > 0)
            {
                decimal anchorPrice = currentAnchor.Price.Value;

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
            InsightsTitleLabel.Text = currentAnchor?.Title ?? "Selected Card";

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
                string.Equals(listingTypeFilter, "sold", StringComparison.OrdinalIgnoreCase)
                    ? $"Sold comps over last {daysRangeFilter} days"
                    : "Active listings only";

            // Rebuild chart
            BuildInsightsPriceChart(
                currentAnchor ?? new EbayListing { Price = median },
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
