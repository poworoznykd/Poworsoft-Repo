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
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        // Original inputs so toggles can recompute.
        private EbayListing? baseListing;
        private List<EbayListing> baseComps;

        // These drive the "Sold over last X days" text (if you use it)
        private string listingTypeFilter;
        private int daysRangeFilter;

        // Options (must exist because XAML events reference them)
        private bool includeSoldInCalcs;
        private bool includeActiveInCalcs;
        private string guideGradeSelection;

        // Graph
        private InsightsGraphDrawable? graphDrawable;

        // PriceCharting
        private readonly PriceChartingService priceChartingService;
        private PriceChartingProduct? priceGuideProduct;
        private string? priceGuideQuery;

        /// <summary>
        /// Fired after the overlay finishes closing.
        /// </summary>
        public event EventHandler? Closed;

        // Called by EbaySearchPage when overlay is shown.
        // The overlay will invoke this ONLY when user clicks Apply.
        public Action<decimal?>? OnEstimatedValueReady { get; set; }

        public InsightsOverlayControl()
        {
            InitializeComponent();

            // Defaults
            includeSoldInCalcs = true;
            includeActiveInCalcs = true;
            guideGradeSelection = "Auto (Detected)";

            baseComps = new List<EbayListing>();

            // Picker options
            if (GuideGradePicker != null)
            {
                GuideGradePicker.Items.Clear();
                GuideGradePicker.Items.Add("Auto (Detected)");
                GuideGradePicker.Items.Add("RAW");
                GuideGradePicker.Items.Add("PSA 7");
                GuideGradePicker.Items.Add("PSA 8");
                GuideGradePicker.Items.Add("PSA 9");
                GuideGradePicker.Items.Add("BGS 9.5");
                GuideGradePicker.Items.Add("PSA 10");
                GuideGradePicker.SelectedIndex = 0;
            }

            // Graph drawable
            if (InsightsGraphView != null)
            {
                graphDrawable = new InsightsGraphDrawable();
                InsightsGraphView.Drawable = graphDrawable;
            }

            if (PriceGuideStatusLabel != null)
            {
                PriceGuideStatusLabel.Text = "Price guide: not loaded yet";
            }

            // Price guide client
            priceChartingService = new PriceChartingService(new HttpClient());

            insightsListings = new ObservableCollection<EbayListing>();
            InsightsListView.ItemsSource = insightsListings;

            listingTypeFilter = "sold";
            daysRangeFilter = 90;

            // Initial hidden state
            InsightsOverlay.IsVisible = false;
            InsightsScrim.IsVisible = false;
            InsightsOverlay.Opacity = 0;
            InsightsScrim.Opacity = 0;

            // If switches exist, sync booleans
            if (IncludeSoldSwitch != null) includeSoldInCalcs = IncludeSoldSwitch.IsToggled;
            if (IncludeActiveSwitch != null) includeActiveInCalcs = IncludeActiveSwitch.IsToggled;

            UpdateEbayWeightLabel();
        }

        // =======================================================
        //      APPLY BUTTON
        // =======================================================

        private void ApplySuggestedValue_Clicked(object sender, EventArgs e)
        {
            if (InsightsData != null && InsightsData.SuggestedPrice.HasValue)
            {
                OnEstimatedValueReady?.Invoke(InsightsData.SuggestedPrice.Value);
            }
        }

        // =======================================================
        //      SHOW / HIDE
        // =======================================================

        public async Task ShowAsync(
            EbayListing anchorListing,
            IEnumerable<EbayListing> comps,
            string listingTypeFilter,
            int daysRangeFilter)
        {
            Debug.WriteLine("[Insights] Entering ShowAsync.");

            if (InsightsOverlay == null || InsightsScrim == null)
            {
                Debug.WriteLine("[Insights] UI not ready. Check x:Name in XAML + InitializeComponent.");
                return;
            }

            currentAnchor = anchorListing;

            this.listingTypeFilter = string.IsNullOrWhiteSpace(listingTypeFilter) ? "sold" : listingTypeFilter;
            this.daysRangeFilter = (daysRangeFilter <= 0) ? 90 : daysRangeFilter;

            baseListing = anchorListing;
            baseComps = comps?.ToList() ?? new List<EbayListing>();

            // Populate list view from comps (the list is the active listings list per your rule)
            insightsListings.Clear();
            foreach (EbayListing c in baseComps)
            {
                if (c != null)
                {
                    insightsListings.Add(c);
                }
            }

            UpdateEbayWeightLabel();

            // Load price guide if enabled (non-blocking)
            if (UsePriceGuideSwitch?.IsToggled == true)
            {
                _ = LoadPriceGuideForCurrentAsync();
            }
            else
            {
                priceGuideProduct = null;
                if (PriceGuideStatusLabel != null)
                {
                    PriceGuideStatusLabel.Text = "Price guide: disabled";
                }
            }

            RecalculateInsightsFromCurrentComps();

            // If already visible, we just refreshed
            if (InsightsOverlay.IsVisible)
            {
                Debug.WriteLine("[Insights] Overlay already visible, refreshed only.");
                return;
            }

            InsightsOverlay.IsVisible = true;
            InsightsScrim.IsVisible = true;

            InsightsOverlay.Opacity = 0;
            InsightsOverlay.TranslationY = 60;
            InsightsScrim.Opacity = 0;

            try
            {
                await Task.WhenAll(
                    InsightsOverlay.FadeTo(1, 180, Easing.CubicOut),
                    InsightsOverlay.TranslateTo(0, 0, 180, Easing.CubicOut),
                    InsightsScrim.FadeTo(1, 180, Easing.CubicOut));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Insights] Animation error: " + ex);
                throw;
            }
        }

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

            // DO NOT auto-apply value on close.
            Closed?.Invoke(this, EventArgs.Empty);
        }

        // =======================================================
        //      SCRIM / CLOSE
        // =======================================================

        private async void OnScrimTapped(object sender, TappedEventArgs e)
        {
            await HideAsync();
        }

        private async void OnCloseTapped(object sender, TappedEventArgs e)
        {
            await HideAsync();
        }

        // =======================================================
        //      SWIPE REMOVE (must affect calculations)
        // =======================================================

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

        // =======================================================
        //      TOGGLES / PICKER (wired to XAML)
        // =======================================================

        private void UsePriceGuideSwitch_Toggled(object sender, ToggledEventArgs e)
        {
            try
            {
                if (PriceGuideStatusLabel != null)
                {
                    PriceGuideStatusLabel.Text = e.Value ? "Price guide: loading..." : "Price guide: disabled";
                }

                if (e.Value)
                {
                    _ = LoadPriceGuideForCurrentAsync();
                }
                else
                {
                    priceGuideProduct = null;
                    RecalculateInsightsFromCurrentComps();
                }
            }
            catch
            {
                // ignore
            }
        }

        private void BlendEbaySwitch_Toggled(object sender, ToggledEventArgs e)
        {
            RecalculateInsightsFromCurrentComps();
        }

        private void IncludeSoldSwitch_Toggled(object sender, ToggledEventArgs e)
        {
            includeSoldInCalcs = e.Value;
            RecalculateInsightsFromCurrentComps();
        }

        private void IncludeActiveSwitch_Toggled(object sender, ToggledEventArgs e)
        {
            includeActiveInCalcs = e.Value;
            RecalculateInsightsFromCurrentComps();
        }

        private void RemoveOutliersSwitch_Toggled(object sender, ToggledEventArgs e)
        {
            RecalculateInsightsFromCurrentComps();
        }

        private void UseBidForAuctionsSwitch_Toggled(object sender, ToggledEventArgs e)
        {
            RecalculateInsightsFromCurrentComps();
        }

        private void EbayWeightSlider_ValueChanged(object sender, ValueChangedEventArgs e)
        {
            UpdateEbayWeightLabel();
            RecalculateInsightsFromCurrentComps();
        }

        private void GuideGradePicker_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (GuideGradePicker?.SelectedItem is string selected)
            {
                guideGradeSelection = selected;
                RecalculateInsightsFromCurrentComps();
            }
        }

        private void UpdateEbayWeightLabel()
        {
            if (EbayWeightLabel == null || EbayWeightSlider == null)
            {
                return;
            }

            // Slider is 0..1 in your XAML
            int pct = (int)Math.Round(EbayWeightSlider.Value * 100.0);
            EbayWeightLabel.Text = $"eBay Weight: {pct}%";
        }

        // =======================================================
        //      PRICE GUIDE LOAD
        // =======================================================

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
                    if (PriceGuideStatusLabel != null)
                    {
                        PriceGuideStatusLabel.Text = "Price guide: no query";
                    }
                    return;
                }

                PriceChartingProduct? product = await priceChartingService.GetBestMatchAsync(priceGuideQuery);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    priceGuideProduct = product;

                    if (PriceGuideStatusLabel != null)
                    {
                        if (product == null)
                        {
                            PriceGuideStatusLabel.Text = "Price guide: no match";
                        }
                        else
                        {
                            string volText = product.SalesVolume.HasValue ? $" • SV: {product.SalesVolume.Value}" : "";
                            PriceGuideStatusLabel.Text = $"Price guide: {product.ProductName}{volText}";
                        }
                    }

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

            title = title.Replace("|", " ").Replace("  ", " ").Trim();
            return title;
        }

        // =======================================================
        //      INSIGHTS CALC
        // =======================================================

        private void RecalculateInsightsFromCurrentComps()
        {
            try
            {
                if (baseListing == null)
                {
                    InsightsData = null;
                    return;
                }

                // Apply SOLD/ACTIVE filters to the current list (after user removes rows)
                List<EbayListing> filteredListings = ApplyIncludeFilters(insightsListings.ToList());

                // Collect eBay prices
                List<double> ebayPrices = new List<double>();

                foreach (EbayListing l in filteredListings)
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

                // Price guide (only use properties you actually have)
                bool useGuide = UsePriceGuideSwitch?.IsToggled == true && priceGuideProduct != null;

                double? guideRaw = priceGuideProduct?.LoosePrice;
                double? guidePsa7 = priceGuideProduct?.CibPrice;
                double? guidePsa8 = priceGuideProduct?.NewPrice;
                double? guidePsa9 = priceGuideProduct?.GradedPrice;
                double? guideBgs95 = priceGuideProduct?.BoxOnlyPrice;        // used for your "BGS 9.5" choice
                double? guidePsa10 = priceGuideProduct?.ManualOnlyPrice;

                int? guideSalesVolume = priceGuideProduct?.SalesVolume;

                string detectedGrade = DetectGradeFromText(baseListing.Title ?? string.Empty);

                string chosenLabel;
                double? guideBaseline = useGuide
                    ? PickGuideBaselineBySelection(
                        detectedGrade,
                        guideGradeSelection,
                        guideRaw,
                        guidePsa7,
                        guidePsa8,
                        guidePsa9,
                        guideBgs95,
                        guidePsa10,
                        out chosenLabel)
                    : null;

                // Suggested price (blend)
                bool blendEbay = BlendEbaySwitch?.IsToggled == true
                                && ebayMedian.HasValue
                                && ebayMedian.Value > 0;

                // Slider is 0..1
                double ebayWeight = EbayWeightSlider?.Value ?? 0.5;
                ebayWeight = Math.Max(0, Math.Min(1, ebayWeight));

                double? suggested = null;
                string notes;

                if (useGuide && guideBaseline.HasValue && blendEbay)
                {
                    suggested = (guideBaseline.Value * (1.0 - ebayWeight)) + (ebayMedian!.Value * ebayWeight);
                    notes = $"Guide baseline ({Fmt(guideBaseline)}) blended with eBay median ({Fmt(ebayMedian)}) at {(int)(ebayWeight * 100)}% eBay weight.";
                }
                else if (useGuide && guideBaseline.HasValue)
                {
                    suggested = guideBaseline.Value;
                    notes = $"Guide baseline used ({Fmt(guideBaseline)}).";
                }
                else if (ebayMedian.HasValue && ebayAvg.HasValue)
                {
                    suggested = (ebayMedian.Value * 0.7) + (ebayAvg.Value * 0.3);
                    notes = $"eBay-only: suggested = 70% median ({Fmt(ebayMedian)}) + 30% avg ({Fmt(ebayAvg)}).";
                }
                else if (ebayMedian.HasValue)
                {
                    suggested = ebayMedian.Value;
                    notes = $"eBay-only: median used ({Fmt(ebayMedian)}).";
                }
                else
                {
                    suggested = null;
                    notes = "No usable prices were found.";
                }

                // Confidence
                double confidenceBase = ebayPrices.Count > 0
                    ? Math.Min(1.0, Math.Log10(ebayPrices.Count + 1) / 1.2)
                    : 0.0;

                double confidence = confidenceBase;
                if (useGuide && guideBaseline.HasValue)
                {
                    confidence = Math.Min(1.0, confidence + 0.15);
                }

                // Build CardInsights
                var data = new CardInsights
                {
                    ListingCount = ebayPrices.Count,
                    Currency = "USD",
                    QueryUsed = (priceGuideQuery ?? baseListing.Title ?? string.Empty).Trim(),
                    LastUpdatedUtc = DateTime.UtcNow,

                    MinPrice = (ebayMin ?? 0),
                    MaxPrice = (ebayMax ?? 0),
                    AveragePrice = (ebayAvg ?? 0),
                    MedianPrice = (ebayMedian ?? 0),

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

                    // Price guide breakdown (only fields you actually have)
                    PriceGuideRawPrice = guideRaw,
                    PriceGuideGradedPrice = guidePsa9,
                    PriceGuidePsa10Price = guidePsa10,
                    PriceGuide95Price = guideBgs95,
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

                // Update graph
                UpdateGraph(
                    guideRaw,
                    guidePsa7,
                    guidePsa8,
                    guidePsa9,
                    guideBgs95,
                    guidePsa10,
                    suggested,
                    ebayPrices);

                // Update UI labels
                if (InsightsSuggestedValue != null)
                {
                    InsightsSuggestedValue.Text =
                        (suggested.HasValue && suggested.Value > 0) ? $"${suggested.Value:0.00} USD" : "—";
                }

                if (InsightsStatsLabel != null)
                {
                    InsightsStatsLabel.Text =
                        (ebayPrices.Count > 0)
                            ? $"Listings: {ebayPrices.Count} • Median: {Fmt(ebayMedian)} • Avg: {Fmt(ebayAvg)}"
                            : "No listing prices available.";
                }

                if (InsightsGuideLabel != null)
                {
                    InsightsGuideLabel.Text = BuildPriceGuideLine(
                        detectedGrade,
                        guideRaw,
                        guidePsa7,
                        guidePsa8,
                        guidePsa9,
                        guideBgs95,
                        guidePsa10,
                        guideSalesVolume,
                        useGuide,
                        guideGradeSelection);
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
                // Never crash UI thread.
            }
        }

        private List<EbayListing> ApplyIncludeFilters(List<EbayListing> source)
        {
            if (source == null)
            {
                return new List<EbayListing>();
            }

            List<EbayListing> filtered = new List<EbayListing>();

            foreach (EbayListing l in source)
            {
                string status = l?.Status ?? string.Empty;

                bool isSold = status.IndexOf("sold", StringComparison.OrdinalIgnoreCase) >= 0
                              || status.IndexOf("ended", StringComparison.OrdinalIgnoreCase) >= 0;

                bool isActive = status.IndexOf("active", StringComparison.OrdinalIgnoreCase) >= 0
                                || status.IndexOf("available", StringComparison.OrdinalIgnoreCase) >= 0;

                // If status missing, treat as active.
                if (string.IsNullOrWhiteSpace(status))
                {
                    isActive = true;
                }

                if (!includeSoldInCalcs && isSold)
                {
                    continue;
                }

                if (!includeActiveInCalcs && isActive)
                {
                    continue;
                }

                filtered.Add(l);
            }

            return filtered;
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
                return "PSA 10";
            }

            if (t.Contains("9.5") || t.Contains("9,5") || t.Contains("BGS 9.5"))
            {
                return "BGS 9.5";
            }

            if (t.Contains("PSA 9") || t.Contains("PSA9"))
            {
                return "PSA 9";
            }

            if (t.Contains("PSA 8") || t.Contains("PSA8"))
            {
                return "PSA 8";
            }

            if (t.Contains("PSA 7") || t.Contains("PSA7"))
            {
                return "PSA 7";
            }

            return "RAW";
        }

        private double? GetGuidePrice(string gradeKey,
                                      double? raw,
                                      double? psa7,
                                      double? psa8,
                                      double? psa9,
                                      double? bgs95,
                                      double? psa10)
        {
            if (string.IsNullOrWhiteSpace(gradeKey))
            {
                return raw;
            }

            string g = gradeKey.Trim().ToUpperInvariant();

            if (g.Contains("PSA 10"))
            {
                return psa10 ?? psa9 ?? raw;
            }

            if (g.Contains("BGS 9.5") || g.Contains("9.5"))
            {
                return bgs95 ?? psa9 ?? raw;
            }

            if (g.Contains("PSA 9"))
            {
                return psa9 ?? raw;
            }

            if (g.Contains("PSA 8"))
            {
                return psa8 ?? raw;
            }

            if (g.Contains("PSA 7"))
            {
                return psa7 ?? raw;
            }

            return raw;
        }

        private double? PickGuideBaselineBySelection(string detectedGrade,
                                                     string selection,
                                                     double? raw,
                                                     double? psa7,
                                                     double? psa8,
                                                     double? psa9,
                                                     double? bgs95,
                                                     double? psa10,
                                                     out string chosenLabel)
        {
            chosenLabel = "RAW";

            // Manual selection wins
            if (!string.IsNullOrWhiteSpace(selection) &&
                !selection.StartsWith("Auto", StringComparison.OrdinalIgnoreCase))
            {
                chosenLabel = selection;
                return GetGuidePrice(selection, raw, psa7, psa8, psa9, bgs95, psa10);
            }

            // Otherwise, use detected grade
            chosenLabel = string.IsNullOrWhiteSpace(detectedGrade) ? "RAW" : detectedGrade;
            return GetGuidePrice(detectedGrade, raw, psa7, psa8, psa9, bgs95, psa10);
        }

        private static string BuildPriceGuideLine(string detectedGrade,
                                                  double? raw,
                                                  double? psa7,
                                                  double? psa8,
                                                  double? psa9,
                                                  double? bgs95,
                                                  double? psa10,
                                                  int? salesVolume,
                                                  bool enabled,
                                                  string selection)
        {
            if (!enabled)
            {
                return "Price guide: disabled.";
            }

            if (!raw.HasValue && !psa7.HasValue && !psa8.HasValue && !psa9.HasValue && !bgs95.HasValue && !psa10.HasValue)
            {
                return "Price guide: no match found.";
            }

            string sv = salesVolume.HasValue ? $" • SV: {salesVolume.Value}" : string.Empty;

            return $"Price guide (USD) • Detected: {detectedGrade} • Selected: {selection} • RAW: {Fmt(raw)} • PSA7: {Fmt(psa7)} • PSA8: {Fmt(psa8)} • PSA9: {Fmt(psa9)} • BGS9.5: {Fmt(bgs95)} • PSA10: {Fmt(psa10)}{sv}";
        }

        private static string Fmt(double? v)
        {
            return (v.HasValue && v.Value > 0) ? $"${v.Value:0.00}" : "—";
        }

        private string BuildSummaryText(double? suggested,
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
                return $"Suggested {Fmt(suggested)} (guide {Fmt(guideBaseline)} blended with eBay median {Fmt(ebayMedian)} from {ebayCount} listings).";
            }

            if (guideEnabled && guideBaseline.HasValue)
            {
                return $"Suggested {Fmt(suggested)} based on guide baseline ({Fmt(guideBaseline)}) with detected grade {detectedGrade}.";
            }

            return $"Suggested {Fmt(suggested)} based on {ebayCount} eBay listings (median/avg blend).";
        }

        private double? GetEbayEffectivePrice(EbayListing listing)
        {
            if (listing == null)
            {
                return null;
            }

            if (UseBidForAuctionsSwitch?.IsToggled == true &&
                listing.CurrentBidPrice.HasValue &&
                listing.CurrentBidPrice.Value > 0)
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

        private void UpdateGraph(double? raw,
                                 double? psa7,
                                 double? psa8,
                                 double? psa9,
                                 double? bgs95,
                                 double? psa10,
                                 double? suggestedUsd,
                                 List<double> ebayPrices)
        {
            if (InsightsGraphView == null || graphDrawable == null)
            {
                return;
            }

            Dictionary<string, double?> guide = new Dictionary<string, double?>
            {
                { "raw", raw },
                { "psa7", psa7 },
                { "psa8", psa8 },
                { "psa9", psa9 },
                { "bgs95", bgs95 },
                { "psa10", psa10 }
            };

            double? suggested = suggestedUsd.HasValue ? (double)suggestedUsd.Value : (double?)null;

            double? vol = (priceGuideProduct != null && priceGuideProduct.SalesVolume.HasValue)
                ? (double)priceGuideProduct.SalesVolume.Value
                : (double?)null;

            graphDrawable.SetData(guide, ebayPrices, suggested, vol);
            InsightsGraphView.Invalidate();
        }
    }
}
