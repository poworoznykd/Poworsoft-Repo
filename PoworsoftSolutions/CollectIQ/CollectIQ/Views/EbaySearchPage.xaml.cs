//
//  FILE            : EbaySearchPage.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-28
//  UPDATED         : 2025-11-23
//  DESCRIPTION     :
//      Displays eBay search results for a scanned or manually
//      entered card query. Supports search-by-image, manual search,
//      swipe actions for "View on eBay" and "Add to Collection",
//      a futuristic bottom-sheet filter panel, and a floating
//      sold-comps overlay (with a simple price graph) that appears
//      when a result is swiped open.
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CollectIQ.Models;
using CollectIQ.Services;
using FreakyKit.Utils;
using Microsoft.Maui.Controls;

namespace CollectIQ.Views
{
    /// <summary>
    /// Interaction logic for the eBay search results page.
    /// </summary>
    [QueryProperty(nameof(FrontImagePath), "frontPath")]
    public partial class EbaySearchPage : ContentPage
    {
        /// <summary>
        /// Represents the type of search that last ran.
        /// </summary>
        private enum SearchMode
        {
            None,
            Image,
            Text
        }

        /// <summary>
        /// Holds the listing that the current Insights overlay is anchored to.
        /// This is used when recalculating insights (for example, after the
        /// user removes comps from the Insights list).
        /// </summary>
        private EbayListing? currentInsightAnchor;



        private readonly EbayService ebayService;
        private readonly ObservableCollection<EbayListing> listings;
        // Insights (sold comps) list
        private readonly ObservableCollection<EbayListing> insightsListings;

        private EbayListing? selectedListing;
        private bool isSwipeInProgress;
        private SearchMode lastSearchMode;

        private string listingTypeFilter;
        private int daysRangeFilter;
        private int averageCountFilter;

        private string lastImageBase64;
        private string lastManualQuery;
        private string frontImagePathInternal;

        /// <summary>
        /// Gets or sets the path to the scanned front image passed in via Shell.
        /// </summary>
        public string FrontImagePath
        {
            get => frontImagePathInternal;
            set => frontImagePathInternal = Uri.UnescapeDataString(value ?? string.Empty);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EbaySearchPage"/> class.
        /// </summary>
        public EbaySearchPage()
        {
            InitializeComponent();

            ebayService = new EbayService(new HttpClient());
            listings = new ObservableCollection<EbayListing>();
            insightsListings = new ObservableCollection<EbayListing>();

            EbayResultsView.ItemsSource = listings;
            InsightsListView.ItemsSource = insightsListings;
            // Default filter: sold, last 90 days, top 10 comps
            listingTypeFilter = "sold";
            daysRangeFilter = 90;
            averageCountFilter = 10;

            lastSearchMode = SearchMode.None;
            lastImageBase64 = string.Empty;
            lastManualQuery = string.Empty;
            frontImagePathInternal = string.Empty;

            InitializeFilterPickers();
            UpdateFilterSummaryLabel();
        }

        #region Filter Initialization

        /// <summary>
        /// Populates the filter pickers with default options.
        /// </summary>
        private void InitializeFilterPickers()
        {
            // Listing type options
            ListingTypePicker.Items.Clear();
            ListingTypePicker.Items.Add("Sold (last sold)");
            ListingTypePicker.Items.Add("Active (for sale)");
            ListingTypePicker.SelectedIndex = 0;

            // Days range options (used only for sold)
            DaysRangePicker.Items.Clear();
            DaysRangePicker.Items.Add("30");
            DaysRangePicker.Items.Add("90");
            DaysRangePicker.Items.Add("180");
            DaysRangePicker.Items.Add("365");
            DaysRangePicker.SelectedIndex = 1; // 90 by default

            // Average count (top N results)
            AverageCountPicker.Items.Clear();
            AverageCountPicker.Items.Add("5");
            AverageCountPicker.Items.Add("10");
            AverageCountPicker.Items.Add("20");
            AverageCountPicker.Items.Add("50");
            AverageCountPicker.SelectedIndex = 1; // 10 by default
        }

        /// <summary>
        /// Updates the small filter summary label under the manual search controls.
        /// </summary>
        private void UpdateFilterSummaryLabel()
        {
            string typeLabel = string.Equals(listingTypeFilter, "sold", StringComparison.OrdinalIgnoreCase)
                ? "Sold"
                : "Active";

            if (string.Equals(listingTypeFilter, "sold", StringComparison.OrdinalIgnoreCase))
            {
                FilterSummaryLabel.Text =
                    $"Filters: {typeLabel}, last {daysRangeFilter} days, avg top {averageCountFilter}";
            }
            else
            {
                FilterSummaryLabel.Text =
                    $"Filters: {typeLabel}, avg top {averageCountFilter}";
            }
        }

        #endregion

        #region Search Helpers

        /// <summary>
        /// Enables or disables the search spinner and sets status text.
        /// </summary>
        private void SetSearchingState(bool isSearching, string statusText = "")
        {
            SearchActivityIndicator.IsVisible = isSearching;
            SearchActivityIndicator.IsRunning = isSearching;

            if (!string.IsNullOrEmpty(statusText))
            {
                StatusLabel.Text = statusText;
            }
        }

        /// <summary>
        /// Calculates the average price from the current results based on
        /// the configured top N averageCountFilter.
        /// </summary>
        private decimal CalculateAveragePrice(List<EbayListing> results)
        {
            if (results == null || results.Count == 0)
            {
                return 0m;
            }

            var pricedItems = results
                .Where(r => r.Price.HasValue && r.Price.Value > 0m)
                .OrderBy(r => r.Price!.Value)
                .Take(averageCountFilter)
                .ToList();

            if (pricedItems.Count == 0)
            {
                return 0m;
            }

            return pricedItems.Average(r => r.Price!.Value);
        }

        /// <summary>
        /// Updates the StatusLabel after a search using the given results.
        /// </summary>
        private void UpdateStatusForResults(List<EbayListing> results)
        {
            if (results == null || results.Count == 0)
            {
                StatusLabel.Text = "No results found.";
                return;
            }

            decimal averagePrice = CalculateAveragePrice(results);

            string typeLabel = string.Equals(listingTypeFilter, "sold", StringComparison.OrdinalIgnoreCase)
                ? "Sold comps"
                : "Active listings";

            if (averagePrice <= 0m)
            {
                StatusLabel.Text =
                    $"{typeLabel}: {results.Count} results (no valid prices for average).";
            }
            else
            {
                StatusLabel.Text =
                    $"{typeLabel}: {results.Count} results, avg top {averageCountFilter}: {averagePrice:C2}";
            }
        }

        /// <summary>
        /// Applies the new results to the observable collection.
        /// </summary>
        private void ApplyResultsToCollection(List<EbayListing> results)
        {
            listings.Clear();

            if (results == null)
            {
                return;
            }

            foreach (EbayListing listing in results)
            {
                listings.Add(listing);
            }
        }

        #endregion

        #region Image Search

        /// <summary>
        /// Performs a search-by-image using the front card image path.
        /// For "Sold" mode, we:
        ///   1) Identify the card by image (active listings),
        ///   2) Use the top titles to fetch sold comps by text.
        /// </summary>
        private async Task PerformImageSearchAsync(string imagePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                {
                    return;
                }

                int lookbackDays = daysRangeFilter <= 0 ? 90 : daysRangeFilter;
                if (lookbackDays > 90)
                {
                    lookbackDays = 90;
                }

                byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
                lastImageBase64 = Convert.ToBase64String(imageBytes);
                lastSearchMode = SearchMode.Image;

                var results = new List<EbayListing>();

                if (string.Equals(listingTypeFilter, "sold", StringComparison.OrdinalIgnoreCase))
                {
                    SetSearchingState(true, "Identifying card and retrieving sold comps...");

                    // Step 1: identify card using active listings via image search.
                    var identified = await ebayService.SearchByImageAsync(
                        lastImageBase64,
                        limit: 5,
                        listingTypeFilter: "active",
                        daysRange: lookbackDays);

                    if (identified == null || identified.Count == 0)
                    {
                        await DisplayAlert(
                            "No matches",
                            "Could not identify this card from the image.",
                            "OK");
                    }
                    else
                    {
                        // Step 2: take top distinct titles and search sold comps by text.
                        var topTitles = identified
                            .Where(r => !string.IsNullOrWhiteSpace(r.Title))
                            .Select(r => r.Title!)
                            .Distinct()
                            .Take(3)
                            .ToList();

                        foreach (string title in topTitles)
                        {
                            var soldForTitle = await EbayService.SearchSoldAsync(
                                title,
                                limit: Math.Max(averageCountFilter * 3, 30),
                                daysRange: lookbackDays);

                            if (soldForTitle != null && soldForTitle.Count > 0)
                            {
                                results.AddRange(soldForTitle);
                            }
                        }

                        if (results.Count == 0)
                        {
                            await DisplayAlert(
                                "No sold comps",
                                $"No sold results found in the last {lookbackDays} days for the identified card title(s). Showing active listings instead.",
                                "OK");

                            listingTypeFilter = "active";
                            UpdateFilterSummaryLabel();
                            results = identified;
                        }
                    }
                }
                else
                {
                    SetSearchingState(true, "Identifying card and retrieving listings...");

                    results = await ebayService.SearchByImageAsync(
                        lastImageBase64,
                        limit: Math.Max(averageCountFilter, 25),
                        listingTypeFilter: listingTypeFilter,
                        daysRange: lookbackDays);
                }

                ApplyResultsToCollection(results);
                UpdateStatusForResults(results);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Image search failed: {ex.Message}", "OK");
            }
            finally
            {
                SetSearchingState(false);
            }
        }

        #endregion

        #region Manual Search

        /// <summary>
        /// Performs a manual text-based search using the configured filters.
        /// </summary>
        private async Task PerformManualSearchAsync(string query
        )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    return;
                }

                lastManualQuery = query.Trim();
                lastSearchMode = SearchMode.Text;

                SetSearchingState(true, $"Searching eBay for: {lastManualQuery}");

                var results = await ebayService.SearchListingsAsync(
                    lastManualQuery,
                    limit: Math.Max(averageCountFilter, 25),
                    listingTypeFilter: listingTypeFilter,
                    daysRange: daysRangeFilter);

                ApplyResultsToCollection(results);
                UpdateStatusForResults(results);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"eBay search failed: {ex.Message}", "OK");
            }
            finally
            {
                SetSearchingState(false);
            }
        }

        #endregion

        #region Insights Overlay

        // ============================================================
        // INSIGHTS OVERLAY (REPLACES ALL OLD COMPS PANEL)
        // ============================================================

        // ============================
        //  INSIGHTS OVERLAY HANDLERS
        // ============================
        private async void OnInsightsIconTapped(object sender, TappedEventArgs e)
        {
            if (e?.Parameter is not EbayListing listing)
            {
                return;
            }

            try
            {
                // Track which listing this Insights session is anchored to
                currentInsightAnchor = listing;
                BuildInsightsForListing(listing);

                // Animate overlay in
                InsightsScrim.IsVisible = true;
                InsightsOverlay.IsVisible = true;
                InsightsOverlay.Opacity = 0;
                InsightsOverlay.TranslationY = 60;

                await Task.WhenAll(
                    InsightsScrim.FadeTo(1, 150),
                    InsightsOverlay.FadeTo(1, 180),
                    InsightsOverlay.TranslateTo(0, 0, 180, Easing.CubicOut)
                );
            }
            catch (Exception ex)
            {
                await DisplayAlert("Insights Error", ex.Message, "OK");
            }
        }

        private async void OnCloseInsightsTapped(object sender, EventArgs e)
        {
            await HideInsightsOverlayAsync();
        }

        private async void OnInsightsScrimTapped(object sender, EventArgs e)
        {
            await HideInsightsOverlayAsync();
        }

        private async Task HideInsightsOverlayAsync()
        {
            if (!InsightsOverlay.IsVisible && !InsightsScrim.IsVisible)
            {
                return;
            }

            await Task.WhenAll(
                InsightsScrim.FadeTo(0, 150),
                InsightsOverlay.FadeTo(0, 150),
                InsightsOverlay.TranslateTo(0, 60, 150, Easing.CubicIn)
            );

            InsightsOverlay.IsVisible = false;
            InsightsScrim.IsVisible = false;
        }

        // Build insights from whatever is currently shown in the results list
        /// <summary>
        /// Builds the initial Insights view for the selected listing using
        /// the currently displayed search results as the comps set.
        /// </summary>
        /// <param name="selected">Listing the user tapped Insights on.</param>
        private void BuildInsightsForListing(EbayListing selected)
        {
            // Treat this listing as the anchor for position/summary text.
            currentInsightAnchor = selected;

            // Use whatever the CollectionView is currently showing as the base comps set.
            var items = EbayResultsView?.ItemsSource as IEnumerable<EbayListing>;
            if (items == null)
            {
                InsightsSummaryLabel.Text = "No market data available.";
                InsightsListView.ItemsSource = null;
                InsightsGraphLayout.Children.Clear();
                return;
            }

            insightsListings.Clear();
            foreach (EbayListing? item in items)
            {
                if (item != null)
                {
                    insightsListings.Add(item);
                }
            }

            RecalculateInsightsFromCurrentComps();
        }

        /// <summary>
        /// Recomputes all Insights metrics (count, min, max, avg, median,
        /// suggested price and volatility) from the current contents of
        /// <see cref="insightsListings"/> and updates the overlay UI.
        /// Call this after any add/remove operation on the comps list.
        /// </summary>
        private void RecalculateInsightsFromCurrentComps()
        {
            var listingList = insightsListings.ToList();
            if (listingList.Count == 0)
            {
                InsightsTitleLabel.Text = currentInsightAnchor?.Title ?? "Selected Card";
                InsightsSummaryLabel.Text = "No market data available.";
                InsightsCountValue.Text = "0";
                InsightsMinValue.Text = "$0.00";
                InsightsMaxValue.Text = "$0.00";
                InsightsAvgValue.Text = "$0.00";
                InsightsMedianValue.Text = "$0.00";
                InsightsSuggestedValue.Text = "$0.00";

                InsightsStatsLabel.Text = "No comps.";
                InsightsRangeLabel.Text = string.Empty;
                InsightsListView.ItemsSource = null;
                InsightsGraphLayout.Children.Clear();
                return;
            }

            // Safely extract prices
            var prices = listingList
                .Where(l => l != null && l.Price.HasValue && l.Price.Value > 0m)
                .Select(l => l.Price!.Value)
                .OrderBy(p => p)
                .ToList();

            if (prices.Count == 0)
            {
                InsightsSummaryLabel.Text = "No valid price data available.";
                InsightsListView.ItemsSource = null;
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
            if (currentInsightAnchor != null &&
                currentInsightAnchor.Price.HasValue &&
                currentInsightAnchor.Price.Value > 0m &&
                prices.Count > 0)
            {
                decimal anchorPrice = currentInsightAnchor.Price.Value;

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
            InsightsTitleLabel.Text = currentInsightAnchor?.Title ?? "Selected Card";

            InsightsCountValue.Text = count.ToString();
            InsightsMinValue.Text = $"${min:F2}";
            InsightsMaxValue.Text = $"${max:F2}";
            InsightsAvgValue.Text = $"${avg:F2}";
            InsightsMedianValue.Text = $"${median:F2}";
            InsightsSuggestedValue.Text = $"${suggested:F2}";

            InsightsSummaryLabel.Text =
                $"Median around ${median:F2}. Range ${min:F2} – ${max:F2}. {volatility} {positionText}";

            InsightsStatsLabel.Text = $"Count: {count}  Avg: ${avg:F2}  Min: ${min:F2}  Max: ${max:F2}";

            InsightsRangeLabel.Text = string.Equals(listingTypeFilter, "sold", StringComparison.OrdinalIgnoreCase)
                ? $"Sold comps over last {daysRangeFilter} days"
                : "Active listings only";

            // Bind the current comps collection
            InsightsListView.ItemsSource = insightsListings;

            // Rebuild chart
            BuildInsightsPriceChart(
                currentInsightAnchor ?? new EbayListing { Price = median },
                prices,
                min,
                max,
                median,
                q25,
                q75);
        }

        /// <summary>
        /// Handles the swipe "Remove" action on a single comp row in the
        /// Insights list. Removes it from the comps collection and triggers
        /// a full Insights recalculation so stats and chart update live.
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

            // Safety checks
            if (sortedPrices.Count == 0 || max <= min)
            {
                return;
            }

            // Heights for the bars
            double minHeight = 18;   // short "blip" bars
            double maxHeight = 60;   // tall bar

            decimal? selectedPrice = selected.Price;

            // Helper to add a labeled bar
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
            AddBar("Min", min, "#7CFC7C"); // green
            AddBar("Q1", q25, "#66FFAA"); // lighter green
            AddBar("Median", median, "#00E5FF"); // bright cyan

            if (selectedPrice.HasValue)
            {
                AddBar("Selected", selectedPrice.Value, "#FF4B4B"); // hot red
            }

            AddBar("Q3", q75, "#FFD966"); // yellow-ish
            AddBar("Max", max, "#FFB347"); // orange
        }


        private async Task LoadAndShowInsightsAsync(EbayListing listing)
        {
            try
            {
                if (listing == null || string.IsNullOrWhiteSpace(listing.Title))
                    return;

                SetSearchingState(true, $"Loading insights...");
                List<EbayListing> cardComps = new List<EbayListing>();
                if (listing.IsSold)
                {
                    cardComps = await EbayService.SearchSoldAsync(
                       listing.Title,
                       limit: Math.Max(averageCountFilter * 3, 30),
                       daysRange: 90);
                }
                else
                {
                    cardComps = await ebayService.SearchListingsAsync(
                      listing.Title,
                      limit: Math.Max(averageCountFilter * 3, 30),
                      "active",
                      90);
                }
                insightsListings.Clear();

                if (cardComps != null)
                {
                    foreach (var comp in cardComps)
                        insightsListings.Add(comp);
                }

                UpdateInsightsHeaderAndGraph(cardComps, listing.Title);

                await ShowInsightsOverlayAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Insights Error", ex.Message, "OK");
            }
            finally
            {
                SetSearchingState(false);
            }
        }

        private void UpdateInsightsHeaderAndGraph(List<EbayListing>? cardComps, string title)
        {
            InsightsTitleLabel.Text = title;
            InsightsGraphLayout.Children.Clear();

            if (cardComps == null || cardComps.Count == 0)
            {
                InsightsStatsLabel.Text = "No sold comps.";
                InsightsRangeLabel.Text = "";
                return;
            }

            var priced = cardComps
                .Where(c => c.Price.HasValue && c.Price.Value > 0m)
                .ToList();

            if (priced.Count == 0)
            {
                InsightsStatsLabel.Text = "No valid prices.";
                InsightsRangeLabel.Text = "";
                return;
            }

            decimal min = priced.Min(c => c.Price!.Value);
            decimal max = priced.Max(c => c.Price!.Value);
            decimal avg = priced.Average(c => c.Price!.Value);

            InsightsStatsLabel.Text = $"Count: {priced.Count}, Avg: {avg:C2}, Min: {min:C2}, Max: {max:C2}";

            var ordered = priced.OrderBy(c => c.EndDateUtc ?? DateTime.UtcNow).ToList();
            DateTime? first = ordered.First().EndDateUtc;
            DateTime? last = ordered.Last().EndDateUtc;

            if (first.HasValue && last.HasValue)
                InsightsRangeLabel.Text = $"{first.Value:yyyy-MM-dd} → {last.Value:yyyy-MM-dd}";
            else
                InsightsRangeLabel.Text = "";

            // Build graph
            InsightsGraphLayout.Children.Clear();
            const double maxHeight = 70;

            if (max <= 0) return;

            foreach (var comp in ordered)
            {
                decimal p = comp.Price ?? 0m;
                double normalized = (double)(p / max);
                if (normalized < 0) normalized = 0;
                if (normalized > 1) normalized = 1;

                InsightsGraphLayout.Children.Add(
                    new BoxView
                    {
                        WidthRequest = 8,
                        HeightRequest = Math.Max(6, maxHeight * normalized),
                        BackgroundColor = Color.FromArgb("#33D6FF"),
                        CornerRadius = 3,
                        VerticalOptions = LayoutOptions.End,
                        HorizontalOptions = LayoutOptions.Center
                    });
            }
        }

        private async Task ShowInsightsOverlayAsync()
        {
            if (InsightsOverlay.IsVisible)
                return;

            InsightsOverlay.IsVisible = true;

            InsightsOverlay.Opacity = 0;
            InsightsOverlay.TranslationY = 40;
            InsightsScrim.IsVisible = true;
            InsightsScrim.Opacity = 0;

            await Task.WhenAll(
                InsightsOverlay.FadeTo(1, 180, Easing.CubicOut),
                InsightsOverlay.TranslateTo(0, 0, 180, Easing.CubicOut),
                InsightsScrim.FadeTo(1, 180, Easing.CubicOut)
            );
        }

        private async void OnInsightsScrimTapped(object sender, TappedEventArgs e)
        {
            await HideInsightsOverlayAsync();
        }

        #endregion

        #region Event Handlers - Manual Search and Filters

        /// <summary>
        /// Handles the manual SEARCH button click.
        /// </summary>
        private async void OnManualSearchClicked(object sender, EventArgs e)
        {
            string query = ManualSearchBox?.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(query))
            {
                await DisplayAlert("Missing Input", "Enter text to search eBay.", "OK");
                return;
            }

            await PerformManualSearchAsync(query);
        }

        /// <summary>
        /// Opens the CardPage for adding a new card manually.
        /// </summary>
        private async void Add_Manual_Button_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new CardPage(new Card()));
        }

        /// <summary>
        /// Displays the bottom-sheet filter overlay.
        /// </summary>
        private void OnFilterButtonClicked(object sender, EventArgs e)
        {
            FilterOverlay.IsVisible = true;
        }

        #region Begin Searching
        /// <summary>
        /// On navigation to this page, if a front image path was provided,
        /// automatically perform a search-by-image using the current filters.
        /// </summary>
        /// <param name="args">Navigation arguments.</param>
        protected override void OnNavigatedTo(NavigatedToEventArgs args)
        {
            base.OnNavigatedTo(args);

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                // Give Shell enough time to apply query params the first time
                await Task.Delay(50);

                if (!string.IsNullOrWhiteSpace(FrontImagePath) &&
                    File.Exists(FrontImagePath))
                {
                    await PerformImageSearchAsync(FrontImagePath);
                }
            });
        }

        /// <summary>
        /// Applies selected filters and re-runs the last search.
        /// </summary>
        private async void OnApplyFiltersClicked(object sender, EventArgs e)
        {
            try
            {
                // Listing type
                if (ListingTypePicker.SelectedIndex == 0)
                {
                    listingTypeFilter = "sold";
                }
                else
                {
                    listingTypeFilter = "active";
                }

                // Days range (only really used for sold)
                int selectedDays = daysRangeFilter;
                if (DaysRangePicker.SelectedItem is string daysText &&
                    int.TryParse(daysText, out int parsedDays))
                {
                    selectedDays = parsedDays;
                }

                daysRangeFilter = selectedDays;

                // Average count (top N)
                int selectedAverageCount = averageCountFilter;
                if (AverageCountPicker.SelectedItem is string avgText &&
                    int.TryParse(avgText, out int parsedAvg))
                {
                    selectedAverageCount = Math.Max(1, parsedAvg);
                }

                averageCountFilter = selectedAverageCount;

                UpdateFilterSummaryLabel();
                FilterOverlay.IsVisible = false;

                // Re-run the last search with updated filters
                if (lastSearchMode == SearchMode.Image && !string.IsNullOrEmpty(lastImageBase64))
                {
                    SetSearchingState(true, "Updating image search with new filters...");

                    // Reuse existing base64 rather than re-reading from disk
                    var results = await ebayService.SearchByImageAsync(
                        lastImageBase64,
                        limit: Math.Max(averageCountFilter, 25),
                        listingTypeFilter: listingTypeFilter,
                        daysRange: daysRangeFilter);

                    ApplyResultsToCollection(results);
                    UpdateStatusForResults(results);
                }
                else if (lastSearchMode == SearchMode.Text && !string.IsNullOrEmpty(lastManualQuery))
                {
                    await PerformManualSearchAsync(lastManualQuery);
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to apply filters: {ex.Message}", "OK");
            }
            finally
            {
                SetSearchingState(false);
            }
        }

        #endregion

        /// <summary>
        /// Hides the filter overlay without applying changes.
        /// </summary>
        private void OnCancelFiltersClicked(object sender, EventArgs e)
        {
            FilterOverlay.IsVisible = false;
        }

        #endregion

        #region Event Handlers - Result Selection and Swipes

        private async Task AnimateInsightsIconAsync(View icon)
        {
            try
            {
                await icon.ScaleTo(1.15, 120, Easing.CubicOut);
                await icon.ScaleTo(1.0, 120, Easing.CubicIn);
            }
            catch { /* ignore */ }
        }

        private void OnItemTapped(object sender, TappedEventArgs e)
        {
            if (e.Parameter is not EbayListing tapped)
                return;

            foreach (var item in listings)
                item.IsSelected = false;

            tapped.IsSelected = true;
            selectedListing = tapped;

            ManualSearchBox.Text = tapped.Title;
        }

        /// <summary>
        /// Swipe start handler – track that a swipe is in progress and show comps for this item.
        /// </summary>
        private async void SwipeView_SwipeStarted(object sender, SwipeStartedEventArgs e)
        {
            isSwipeInProgress = true;

            if (sender is SwipeView swipe &&
                swipe.BindingContext is EbayListing listing)
            {
                selectedListing = listing;
            }
        }

        /// <summary>
        /// Swipe end handler to clear the swipe-in-progress flag.
        /// </summary>
        private void SwipeView_SwipeEnded(object sender, SwipeEndedEventArgs e)
        {
            isSwipeInProgress = false;
        }

        /// <summary>
        /// Handles the "Add" swipe action and adds the selected card
        /// to the user's collection.
        /// </summary>
        private async void OnAddToCollectionSwipe(object sender, EventArgs e)
        {
            try
            {
                EbayListing? listing = null;

                if (sender is SwipeItem swipeItem &&
                    swipeItem.CommandParameter is EbayListing paramListing)
                {
                    listing = paramListing;
                }
                else if (selectedListing != null)
                {
                    listing = selectedListing;
                }

                if (listing == null)
                {
                    return;
                }
                //TODO: Improve mapping from EbayListing to Card
                Card card = new Card
                {
                    Title = listing.Title,
                    EstimatedValue = listing.Price,
                    CollectionId = "Default",
                    FrontImagePath = listing.ImageUrl,
                    BackImagePath = listing.ImageUrl,
                    Set = "eBay Import",
                    GradeCompany = "Raw",
                    InsightsJson = $"{{ \"EbayListingId\": \"{listing.ListingId}\", \"EbayUrl\": \"{listing.Url}\" }}"
                };

                await App.Database.AddCardAsync(card);
                await DisplayAlert("Added", $"{listing.Title} added to your collection.", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Could not add card: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Handles the "Ebay" swipe action and opens the listing in the browser.
        /// </summary>
        private async void OnViewOnEbaySwipe(object sender, EventArgs e)
        {
            try
            {
                EbayListing? listing = null;

                if (sender is SwipeItem swipeItem &&
                    swipeItem.CommandParameter is EbayListing paramListing)
                {
                    listing = paramListing;
                }
                else if (selectedListing != null)
                {
                    listing = selectedListing;
                }

                if (listing == null || string.IsNullOrWhiteSpace(listing.Url))
                {
                    return;
                }

                await Browser.Default.OpenAsync(listing.Url, BrowserLaunchMode.SystemPreferred);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Unable to open eBay: {ex.Message}", "OK");
            }
        }

        #endregion

    }
}
