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
    // IMPORTANT:
    // We force the compiler to use the ONE TRUE model for PriceChartingProduct.
    // This prevents the "ambiguous reference" problem if a duplicate class exists elsewhere.
    using PriceGuideProduct = CollectIQ.Models.PriceChartingProduct;

    /// <summary>
    /// Reusable overlay for displaying market insights for a card.
    /// </summary>
    public partial class InsightsOverlayControl : ContentView
    {
        public CardInsights? InsightsData { get; private set; }

        private readonly ObservableCollection<EbayListing> insightsListings;
        private EbayListing? currentAnchor;

        private string listingTypeFilter;
        private int daysRangeFilter;

        /// <summary>
        /// Fired after the overlay finishes closing.
        /// </summary>
        public event EventHandler? Closed;

        // =======================================================
        //      VALUE CALLBACK (Used to send value back to page)
        // =======================================================

        // Called by EbaySearchPage when overlay is shown.
        // The overlay will invoke this when user clicks "Apply Suggested Value".
        public Action<decimal?>? OnEstimatedValueReady { get; set; }

        // =======================================================
        //      PRICE GUIDE (PriceCharting)
        // =======================================================

        private readonly PriceChartingService priceChartingService;

        private EbayListing? baseListing;
        private List<EbayListing> baseComps;

        private PriceGuideProduct? priceGuideProduct;
        private string? priceGuideQuery;

        // =======================================================
        //      CALC OPTIONS (toggles)
        // =======================================================

        private bool includeSoldInCalcs;
        private bool includeActiveInCalcs;
        private string guideGradeSelection;

        // =======================================================
        //      GRAPH
        // =======================================================

        private InsightsGraphDrawable? graphDrawable;

        public InsightsOverlayControl()
        {
            InitializeComponent();

            // Defaults (toggles)
            includeSoldInCalcs = true;
            includeActiveInCalcs = true;
            guideGradeSelection = "Auto (Detected)";

            baseComps = new List<EbayListing>();

            // Picker options (manual override). Default = Auto (Detected).
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

            // Services
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
        }

        /// <summary>
        /// Apply suggested value back to the host page.
        /// </summary>
        private void ApplySuggestedValue_Clicked(object sender, EventArgs e)
        {
            try
            {
                if (InsightsData != null && InsightsData.SuggestedPrice.HasValue)
                {
                    OnEstimatedValueReady?.Invoke(InsightsData.SuggestedPrice.Value);
                }
            }
            catch
            {
                // Keep UI resilient.
            }
        }

        public async Task ShowAsync(
            EbayListing anchorListing,
            IEnumerable<EbayListing> comps,
            string listingTypeFilter,
            int daysRangeFilter)
        {
            Debug.WriteLine("[Insights] Entering ShowAsync.");

            if (InsightsOverlay == null || InsightsScrim == null)
            {
                Debug.WriteLine("[Insights] UI not ready. Check x:Name + InitializeComponent().");
                return;
            }

            currentAnchor = anchorListing;

            this.listingTypeFilter = string.IsNullOrWhiteSpace(listingTypeFilter) ? "sold" : listingTypeFilter;
            this.daysRangeFilter = daysRangeFilter <= 0 ? 90 : daysRangeFilter;

            baseListing = anchorListing;
            baseComps = comps?.ToList() ?? new List<EbayListing>();

            // Sync weight label (your slider is 0..1 in XAML)
            if (EbayWeightLabel != null && EbayWeightSlider != null)
            {
                EbayWeightLabel.Text = $"eBay Weight: {(int)(EbayWeightSlider.Value * 100)}%";
            }

            // Load price guide data if enabled (best-effort)
            if (UsePriceGuideSwitch?.IsToggled == true)
            {
                _ = LoadPriceGuideForCurrentAsync();
            }

            // Populate the comps list (this list is your ACTIVE ebay listings list)
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

            RecalculateInsightsFromCurrentComps();

            if (InsightsOverlay.IsVisible)
            {
                Debug.WriteLine("[Insights] Overlay already visible, data refreshed.");
                return;
            }

            // Animate in
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

        private void OnRemoveCompSwipe(object sender, EventArgs e)
        {
            try
            {
                EbayListing? comp = null;

                if (sender is SwipeItem swipeItem && swipeItem.CommandParameter is EbayListing parameterListing)
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

        private void UsePriceGuideSwitch_Toggled(object sender, ToggledEventArgs e)
        {
            try
            {
                if (e.Value)
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

                    RecalculateInsightsFromCurrentComps();
                }
            }
            catch
            {
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
            try
            {
                if (EbayWeightLabel != null)
                {
                    EbayWeightLabel.Text = $"eBay Weight: {(int)(e.NewValue * 100)}%";
                }

                RecalculateInsightsFromCurrentComps();
            }
            catch
            {
            }
        }

        private void GuideGradePicker_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (GuideGradePicker?.SelectedItem is string selected)
                {
                    guideGradeSelection = selected;
                    RecalculateInsightsFromCurrentComps();
                }
            }
            catch
            {
            }
        }

        #endregion

        #region Price Guide Load

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

                PriceGuideProduct? product = await priceChartingService.GetBestMatchAsync(priceGuideQuery);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    priceGuideProduct = product;

                    if (PriceGuideStatusLabel != null)
                    {
                        if (product is null)
                        {
                            PriceGuideStatusLabel.Text = "Price guide: no match found";
                        }
                        else
                        {
                            string sv = product.SalesVolume.HasValue ? $" • SV: {product.SalesVolume.Value}" : string.Empty;
                            PriceGuideStatusLabel.Text = $"Price guide: {product.ProductName}{sv}";
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

            title = title.Replace("|", " ")
                         .Replace("  ", " ")
                         .Trim();

            return title;
        }

        #endregion

        #region Insights Calculation

        private void RecalculateInsightsFromCurrentComps()
        {
            try
            {
                if (baseListing == null)
                {
                    InsightsData = null;
                    return;
                }

                // ----------------------------------------------------------
                // 1) Apply include filters (sold/active) to the CURRENT list
                // ----------------------------------------------------------
                List<EbayListing> filteredListings = ApplyIncludeFilters(insightsListings.ToList());

                // ----------------------------------------------------------
                // 2) Build eBay price list
                // ----------------------------------------------------------
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

                double? ebayMin = ebayPrices.Count > 0 ? ebayPrices.First() : (double?)null;
                double? ebayMax = ebayPrices.Count > 0 ? ebayPrices.Last() : (double?)null;
                double? ebayAvg = ebayPrices.Count > 0 ? ebayPrices.Average() : (double?)null;
                double? ebayMedian = ebayPrices.Count > 0 ? ComputeMedianSorted(ebayPrices) : (double?)null;

                // ----------------------------------------------------------
                // 3) Price guide baseline (RAW/grades)
                // ----------------------------------------------------------
                bool useGuide = UsePriceGuideSwitch?.IsToggled == true && priceGuideProduct is not null;

                double? guideRaw = useGuide ? priceGuideProduct!.LoosePrice : null;
                double? guidePsa7 = useGuide ? priceGuideProduct!.CibPrice : null;
                double? guidePsa8 = useGuide ? priceGuideProduct!.NewPrice : null;
                double? guidePsa9 = useGuide ? priceGuideProduct!.GradedPrice : null;
                double? guideBgs95 = useGuide ? priceGuideProduct!.BoxOnlyPrice : null;
                double? guidePsa10 = useGuide ? priceGuideProduct!.ManualOnlyPrice : null;

                string detectedGrade = DetectGradeFromText(baseListing.Title ?? string.Empty);

                string chosenLabel = "";
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

                // ----------------------------------------------------------
                // 4) Suggested price (blend guide + eBay)
                // ----------------------------------------------------------
                bool blendEbay = BlendEbaySwitch?.IsToggled == true && ebayMedian.HasValue && ebayMedian.Value > 0;

                double ebayWeight = (EbayWeightSlider?.Value ?? 0.50);
                ebayWeight = Math.Max(0, Math.Min(1, ebayWeight));

                double? suggested = null;
                string notes;

                if (useGuide && guideBaseline.HasValue && blendEbay)
                {
                    suggested = (guideBaseline.Value * (1.0 - ebayWeight)) + (ebayMedian!.Value * ebayWeight);
                    notes = $"Guide ({chosenLabel}) blended with eBay median at {(int)(ebayWeight * 100)}% eBay weight.";
                }
                else if (useGuide && guideBaseline.HasValue)
                {
                    suggested = guideBaseline.Value;
                    notes = $"Guide baseline used ({chosenLabel}).";
                }
                else if (ebayMedian.HasValue && ebayAvg.HasValue)
                {
                    suggested = (ebayMedian.Value * 0.7) + (ebayAvg.Value * 0.3);
                    notes = "eBay-only suggested (70% median + 30% avg).";
                }
                else if (ebayMedian.HasValue)
                {
                    suggested = ebayMedian.Value;
                    notes = "eBay-only suggested (median).";
                }
                else
                {
                    notes = "No usable prices were found.";
                }

                // ----------------------------------------------------------
                // 5) Update labels
                // ----------------------------------------------------------
                if (InsightsCountValue != null)
                {
                    InsightsCountValue.Text = ebayPrices.Count.ToString();
                }

                if (InsightsMinValue != null)
                {
                    InsightsMinValue.Text = ebayMin.HasValue ? $"${ebayMin.Value:0.00}" : "—";
                }

                if (InsightsMaxValue != null)
                {
                    InsightsMaxValue.Text = ebayMax.HasValue ? $"${ebayMax.Value:0.00}" : "—";
                }

                if (InsightsAvgValue != null)
                {
                    InsightsAvgValue.Text = ebayAvg.HasValue ? $"${ebayAvg.Value:0.00}" : "—";
                }

                if (InsightsMedianValue != null)
                {
                    InsightsMedianValue.Text = ebayMedian.HasValue ? $"${ebayMedian.Value:0.00}" : "—";
                }

                if (InsightsRangeLabel != null)
                {
                    InsightsRangeLabel.Text =
                        (ebayMin.HasValue && ebayMax.HasValue) ? $"${ebayMin.Value:0.00} - ${ebayMax.Value:0.00}" : "—";
                }

                if (InsightsSuggestedValue != null)
                {
                    InsightsSuggestedValue.Text =
                        (suggested.HasValue && suggested.Value > 0) ? $"${suggested.Value:0.00} USD" : "—";
                }

                if (InsightsBlendLabel != null)
                {
                    InsightsBlendLabel.Text = notes;
                }

                if (InsightsGuideLabel != null)
                {
                    InsightsGuideLabel.Text =
                        useGuide
                            ? $"Price guide (USD): RAW {Fmt(guideRaw)} • PSA7 {Fmt(guidePsa7)} • PSA8 {Fmt(guidePsa8)} • PSA9 {Fmt(guidePsa9)} • BGS9.5 {Fmt(guideBgs95)} • PSA10 {Fmt(guidePsa10)}"
                            : "Price guide: disabled.";
                }

                // ----------------------------------------------------------
                // 6) Build InsightsData for host usage
                // ----------------------------------------------------------
                InsightsData = new CardInsights
                {
                    ListingCount = ebayPrices.Count,
                    MinPrice = (ebayMin ?? 0),
                    MaxPrice = (ebayMax ?? 0),
                    AveragePrice = (ebayAvg ?? 0),
                    MedianPrice = (ebayMedian ?? 0),
                    SuggestedPrice = suggested.HasValue ? (decimal)suggested.Value : 0m,
                    Summary = notes
                };

                // ----------------------------------------------------------
                // 7) Update graph
                // NOTE: UpdateGraph expects List<decimal>, so we convert.
                // ----------------------------------------------------------
                List<decimal> workingValues = ebayPrices.Select(x => (decimal)x).ToList();

                UpdateGraph(
                    guideRaw,
                    guidePsa7,
                    guidePsa8,
                    guidePsa9,
                    guideBgs95,
                    guidePsa10,
                    suggested,
                    workingValues);
            }
            catch
            {
                // Keep overlay resilient - never crash UI thread.
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

            if (t.Contains("BGS 9.5") || t.Contains("BGS9.5") || t.Contains("9.5"))
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
                return psa10 ?? raw;
            }

            if (g.Contains("BGS 9.5"))
            {
                return bgs95 ?? raw;
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

            if (!string.IsNullOrWhiteSpace(selection) &&
                !selection.StartsWith("Auto", StringComparison.OrdinalIgnoreCase))
            {
                chosenLabel = selection;
                return GetGuidePrice(selection, raw, psa7, psa8, psa9, bgs95, psa10);
            }

            chosenLabel = string.IsNullOrWhiteSpace(detectedGrade) ? "RAW" : detectedGrade;
            return GetGuidePrice(detectedGrade, raw, psa7, psa8, psa9, bgs95, psa10);
        }

        private void UpdateGraph(double? raw,
                                 double? psa7,
                                 double? psa8,
                                 double? psa9,
                                 double? bgs95,
                                 double? psa10,
                                 double? suggestedUsd,
                                 List<decimal> workingValues)
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

            List<double> comps = new List<double>();
            foreach (decimal v in workingValues)
            {
                if (v > 0)
                {
                    comps.Add((double)v);
                }
            }

            double? suggested = suggestedUsd.HasValue ? suggestedUsd.Value : (double?)null;

            double? vol = null;
            if (priceGuideProduct is not null && priceGuideProduct.SalesVolume.HasValue)
            {
                vol = priceGuideProduct.SalesVolume.Value;
            }

            graphDrawable.SetData(guide, comps, suggested, vol);

            InsightsGraphView.Invalidate();
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

        private static string Fmt(double? v)
        {
            return v.HasValue && v.Value > 0 ? $"${v.Value:0.00}" : "—";
        }

        #endregion
    }
}
