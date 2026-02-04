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
using CollectIQ.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
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


private readonly PriceChartingService priceChartingService;
private EbayListing? baseListing;
private List<EbayListing> baseComps = new List<EbayListing>();

private PriceChartingProduct? priceGuideProduct;
private string? priceGuideQuery;

        public InsightsOverlayControl()
        {
            InitializeComponent();

            // Price guide client (PriceCharting)
            priceChartingService = new PriceChartingService(new HttpClient());

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

                // No UI to animate � bail out safely.
                return;
            }

            // 2. Normalize / store inputs
            currentAnchor = anchorListing;
            this.listingTypeFilter = string.IsNullOrWhiteSpace(listingTypeFilter)
                ? "sold"
                : listingTypeFilter;
            this.daysRangeFilter = daysRangeFilter <= 0 ? 90 : daysRangeFilter;


// Keep the original inputs so we can recompute when toggles change.
baseListing = anchorListing;
baseComps = comps?.ToList() ?? new List<EbayListing>();

// Keep the UI label in sync.
if (EbayWeightLabel != null)
{
    EbayWeightLabel.Text = $"{(int)EbayWeightSlider.Value}%";
}

// Load price guide data (best effort). This is optional and will not block Insights.
if (UsePriceGuideSwitch?.IsToggled == true)
{
    _ = LoadPriceGuideForCurrentAsync();
}

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
    try
    {
        if (baseListing == null)
        {
            InsightsData = null;
            return;
        }

        // -----------------------------------------------------------------
        // 1) Collect eBay prices
        // -----------------------------------------------------------------
        List<double> ebayPrices = new List<double>();

        foreach (EbayListing l in insightsListings)
        {
            double? p = GetEbayEffectivePrice(l);
            if (p.HasValue && p.Value > 0)
            {
                ebayPrices.Add(p.Value);
            }
        }

        ebayPrices.Sort();

        if (RemoveOutliersSwitch?.IsToggled == true && ebayPrices.Count >= 6)
        {
            ebayPrices = RemoveOutliersIqr(ebayPrices);
            ebayPrices.Sort();
        }

        double? ebayMin = ebayPrices.Count > 0 ? ebayPrices.First() : null;
        double? ebayMax = ebayPrices.Count > 0 ? ebayPrices.Last() : null;
        double? ebayAvg = ebayPrices.Count > 0 ? ebayPrices.Average() : null;
        double? ebayMedian = ebayPrices.Count > 0 ? ComputeMedianSorted(ebayPrices) : null;

        // -----------------------------------------------------------------
        // 2) Price guide baseline (PriceCharting)
        // -----------------------------------------------------------------
        double? guideRaw = priceGuideProduct?.LoosePrice;
        double? guideGraded = priceGuideProduct?.GradedPrice;
        double? guidePsa10 = priceGuideProduct?.ManualOnlyPrice;
        double? guide95 = priceGuideProduct?.BoxOnlyPrice;
        double? guideBgs10 = priceGuideProduct?.Bgs10Price;
        double? guideCgc10 = priceGuideProduct?.Condition17Price;
        double? guideSgc10 = priceGuideProduct?.Condition18Price;

        int? guideSalesVolume = priceGuideProduct?.SalesVolume;

        bool useGuide = UsePriceGuideSwitch?.IsToggled == true && priceGuideProduct != null;

        string detectedGrade = DetectGradeFromText(baseListing.Title ?? string.Empty);

        double? guideBaseline = useGuide
            ? PickGuideBaseline(
                detectedGrade,
                guideRaw,
                guideGraded,
                guidePsa10,
                guide95,
                guideBgs10,
                guideCgc10,
                guideSgc10)
            : null;

        // -----------------------------------------------------------------
        // 3) Suggested price (blend guide + eBay)
        // -----------------------------------------------------------------
        bool blendEbay = BlendEbaySwitch?.IsToggled == true && ebayMedian.HasValue && ebayMedian.Value > 0;

        double ebayWeight = (EbayWeightSlider?.Value ?? 30) / 100.0;
        ebayWeight = Math.Max(0, Math.Min(1, ebayWeight));

        double? suggested = null;
        string notes;

        if (useGuide && guideBaseline.HasValue && blendEbay)
        {
            suggested = (guideBaseline.Value * (1.0 - ebayWeight)) + (ebayMedian!.Value * ebayWeight);
            notes = $"Guide baseline (${guideBaseline:0.00}) blended with eBay median (${ebayMedian:0.00}) at {(int)(ebayWeight * 100)}% eBay weight.";
        }
        else if (useGuide && guideBaseline.HasValue)
        {
            suggested = guideBaseline.Value;
            notes = $"Guide baseline used (${guideBaseline:0.00}).";
        }
        else if (ebayMedian.HasValue && ebayAvg.HasValue)
        {
            suggested = (ebayMedian.Value * 0.7) + (ebayAvg.Value * 0.3);
            notes = $"eBay-only: suggested = 70% median (${ebayMedian:0.00}) + 30% avg (${ebayAvg:0.00}).";
        }
        else if (ebayMedian.HasValue)
        {
            suggested = ebayMedian.Value;
            notes = $"eBay-only: median used (${ebayMedian:0.00}).";
        }
        else
        {
            suggested = null;
            notes = "No usable prices were found.";
        }

        // Confidence: increases with number of listings, plus a small bump if guide exists.
        double confidenceBase = ebayPrices.Count > 0
            ? Math.Min(1.0, Math.Log10(ebayPrices.Count + 1) / 1.2)
            : 0.0;

        double confidence = confidenceBase;
        if (useGuide && guideBaseline.HasValue)
        {
            confidence = Math.Min(1.0, confidence + 0.15);
        }

        // -----------------------------------------------------------------
        // 4) Build InsightsData
        // -----------------------------------------------------------------
        var data = new CardInsights
        {
            ListingCount = ebayPrices.Count,
            Currency = "USD",
            QueryUsed = (priceGuideQuery ?? baseListing.Title ?? string.Empty).Trim(),
            LastUpdatedUtc = DateTime.UtcNow,

            MinPrice = ebayMin ?? 0,
            MaxPrice = ebayMax ?? 0,
            AveragePrice = ebayAvg ?? 0,
            MedianPrice = ebayMedian ?? 0,

            SuggestedPrice = suggested.HasValue ? (decimal)suggested.Value : 0m,
            ConfidenceScore = confidence,
            Summary = BuildSummaryText(
                suggested,
                guideBaseline,
                ebayMedian,
                ebayPrices.Count,
                detectedGrade,
                useGuide,
                blendEbay),

            // Price guide breakdown
            PriceGuideRawPrice = guideRaw,
            PriceGuideGradedPrice = guideGraded,
            PriceGuidePsa10Price = guidePsa10,
            PriceGuide95Price = guide95,
            PriceGuideBgs10Price = guideBgs10,
            PriceGuideCgc10Price = guideCgc10,
            PriceGuideSgc10Price = guideSgc10,
            PriceGuideSalesVolume = guideSalesVolume,
            PriceGuideBaselineUsed = guideBaseline,

            // eBay breakdown
            EbayMedianPrice = ebayMedian,
            EbayAveragePrice = ebayAvg,
            EbayListingCountUsed = ebayPrices.Count,
            EbayBlendWeight = blendEbay ? ebayWeight : 0,

            CalculationNotes = notes
        };

        InsightsData = data;

        // -----------------------------------------------------------------
        // 5) Update UI labels
        // -----------------------------------------------------------------
        if (InsightsSuggestedValue != null)
        {
            InsightsSuggestedValue.Text =
                (suggested.HasValue && suggested.Value > 0) ? $"${suggested.Value:0.00} USD" : "—";
        }

        if (InsightsStatsLabel != null)
        {
            InsightsStatsLabel.Text =
                (ebayPrices.Count > 0)
                    ? $"Listings: {ebayPrices.Count} • Median: ${(ebayMedian ?? 0):0.00} • Avg: ${(ebayAvg ?? 0):0.00}"
                    : "No listing prices available.";
        }

        if (InsightsGuideLabel != null)
        {
            InsightsGuideLabel.Text = BuildPriceGuideLine(
                detectedGrade,
                guideRaw,
                guideGraded,
                guidePsa10,
                guideBgs10,
                guideCgc10,
                guideSgc10,
                guideSalesVolume,
                useGuide);
        }

        if (InsightsBlendLabel != null)
        {
            InsightsBlendLabel.Text = notes;
        }

        if (InsightsSummaryLabel != null)
        {
            InsightsSummaryLabel.Text = data.Summary ?? string.Empty;
        }
    }
    catch
    {
        // Keep overlay resilient - never crash UI thread.
    }
}

private async Task LoadPriceGuideForCurrentAsync()
{
    try
    {
        if (baseListing == null)
        {
            return;
        }

        priceGuideQuery = BuildPriceGuideQuery(baseListing);

        if (string.IsNullOrWhiteSpace(priceGuideQuery))
        {
            return;
        }

        PriceChartingProduct? product = await priceChartingService.GetBestMatchAsync(priceGuideQuery);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            priceGuideProduct = product;
            RecalculateInsightsFromCurrentComps();
        });
    }
    catch
    {
        // ignore
    }
}

private static string BuildPriceGuideQuery(EbayListing listing)
{
    string title = (listing?.Title ?? string.Empty).Trim();

    if (string.IsNullOrWhiteSpace(title))
    {
        return string.Empty;
    }

    title = title.Replace("|", " ")
                 .Replace("  ", " ")
                 .Trim();

    return title;
}

private static string DetectGradeFromText(string text)
{
    if (string.IsNullOrWhiteSpace(text))
    {
        return "RAW";
    }

    string t = text.ToUpperInvariant();

    if (t.Contains("PSA 10") || t.Contains("PSA10") || t.Contains("GEM MINT 10"))
    {
        return "PSA10";
    }

    if (t.Contains("BGS 10") || t.Contains("BGS10") || t.Contains("BLACK LABEL"))
    {
        return "BGS10";
    }

    if (t.Contains("CGC 10") || t.Contains("CGC10"))
    {
        return "CGC10";
    }

    if (t.Contains("SGC 10") || t.Contains("SGC10"))
    {
        return "SGC10";
    }

    if (t.Contains("9.5") || t.Contains("9,5"))
    {
        return "9.5";
    }

    if (t.Contains("PSA 9") || t.Contains("PSA9"))
    {
        return "GRADED";
    }

    return "RAW";
}

private static double? PickGuideBaseline(
    string detectedGrade,
    double? raw,
    double? graded,
    double? psa10,
    double? g95,
    double? bgs10,
    double? cgc10,
    double? sgc10)
{
    switch (detectedGrade)
    {
        case "PSA10":
            return psa10 ?? graded ?? raw;

        case "BGS10":
            return bgs10 ?? psa10 ?? graded ?? raw;

        case "CGC10":
            return cgc10 ?? psa10 ?? graded ?? raw;

        case "SGC10":
            return sgc10 ?? psa10 ?? graded ?? raw;

        case "9.5":
            return g95 ?? graded ?? raw;

        case "GRADED":
            return graded ?? raw;

        default:
            return raw ?? graded ?? psa10;
    }
}

private static string BuildPriceGuideLine(
    string detectedGrade,
    double? raw,
    double? graded,
    double? psa10,
    double? bgs10,
    double? cgc10,
    double? sgc10,
    int? salesVolume,
    bool enabled)
{
    if (!enabled)
    {
        return "Price guide: disabled.";
    }

    if (!raw.HasValue && !graded.HasValue && !psa10.HasValue && !bgs10.HasValue && !cgc10.HasValue && !sgc10.HasValue)
    {
        return "Price guide: no match found.";
    }

    string sv = salesVolume.HasValue ? $" • SV: {salesVolume.Value}" : string.Empty;

    return $"Price guide (USD) • Detected: {detectedGrade} • Raw: {Fmt(raw)} • Graded: {Fmt(graded)} • PSA10: {Fmt(psa10)} • BGS10: {Fmt(bgs10)} • CGC10: {Fmt(cgc10)} • SGC10: {Fmt(sgc10)}{sv}";
}

private static string Fmt(double? v)
{
    return v.HasValue && v.Value > 0 ? $"${v.Value:0.00}" : "—";
}

private string BuildSummaryText(
    double? suggested,
    double? guideBaseline,
    double? ebayMedian,
    int ebayCount,
    string detectedGrade,
    bool guideEnabled,
    bool blendEnabled)
{
    if (!suggested.HasValue || suggested.Value <= 0)
    {
        return "No suggested value available based on the current inputs.";
    }

    if (guideEnabled && guideBaseline.HasValue && blendEnabled && ebayMedian.HasValue)
    {
        return $"Suggested ${suggested.Value:0.00} USD (guide ${guideBaseline.Value:0.00} blended with eBay median ${ebayMedian.Value:0.00} from {ebayCount} listings).";
    }

    if (guideEnabled && guideBaseline.HasValue)
    {
        return $"Suggested ${suggested.Value:0.00} USD based on guide baseline (${guideBaseline.Value:0.00}) with detected grade {detectedGrade}.";
    }

    return $"Suggested ${suggested.Value:0.00} USD based on {ebayCount} eBay listings (median/avg blend).";
}

private double? GetEbayEffectivePrice(EbayListing listing)
{
    if (listing == null)
    {
        return null;
    }

    if (UseBidForAuctionsSwitch?.IsToggled == true && listing.CurrentBidPrice.HasValue && listing.CurrentBidPrice.Value > 0)
    {
        return (double)listing.CurrentBidPrice.Value;
    }

    if (listing.BuyNowPrice.HasValue && listing.BuyNowPrice.Value > 0)
    {
        return (double)listing.BuyNowPrice.Value;
    }

    if (listing.Price.HasValue && listing.Price.Value > 0)
    {
        return (double)listing.Price.Value;
    }

    return null;
}

private static double ComputeMedianSorted(IReadOnlyList<double> sorted)
{
    int count = sorted.Count;
    if (count == 0)
    {
        return 0;
    }

    if (count % 2 == 1)
    {
        return sorted[count / 2];
    }

    return (sorted[count / 2 - 1] + sorted[count / 2]) / 2.0;
}

private static List<double> RemoveOutliersIqr(List<double> sorted)
{
    int n = sorted.Count;
    if (n < 6)
    {
        return sorted;
    }

    double q1 = PercentileSorted(sorted, 0.25);
    double q3 = PercentileSorted(sorted, 0.75);
    double iqr = q3 - q1;

    double lower = q1 - (1.5 * iqr);
    double upper = q3 + (1.5 * iqr);

    return sorted.Where(x => x >= lower && x <= upper).ToList();
}

private static double PercentileSorted(IReadOnlyList<double> sorted, double p)
{
    int n = sorted.Count;
    if (n == 0)
    {
        return 0;
    }

    double pos = (n - 1) * p;
    int idx = (int)pos;
    double frac = pos - idx;

    if (idx + 1 < n)
    {
        return sorted[idx] + (sorted[idx + 1] - sorted[idx]) * frac;
    }

    return sorted[idx];
}

private void OnOptionToggled(object sender, ToggledEventArgs e)
{
    try
    {
        if (UsePriceGuideSwitch?.IsToggled == true && priceGuideProduct == null)
        {
            _ = LoadPriceGuideForCurrentAsync();
        }

        RecalculateInsightsFromCurrentComps();
    }
    catch
    {
        // ignore
    }
}

private void OnEbayWeightChanged(object sender, ValueChangedEventArgs e)
{
    try
    {
        if (EbayWeightLabel != null)
        {
            EbayWeightLabel.Text = $"{(int)e.NewValue}%";
        }

        RecalculateInsightsFromCurrentComps();
    }
    catch
    {
        // ignore
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
