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
using CollectIQ.Models.SportsCardsPro;

namespace CollectIQ.Controls
{
    /// <summary>
    /// Reusable overlay for displaying market insights for a card.
    /// </summary>
    public partial class InsightsOverlayControl : ContentView
    {
        #region Properties and Fields
        private readonly SportsCardsProService sportsCardsProService = new SportsCardsProService(new HttpClient());
        private readonly InsightsGraphDrawable graphDrawable = new InsightsGraphDrawable();
        private string listingTypeFilter;
        private int daysRangeFilter;
        private readonly ObservableCollection<EbayListing> ebayListings;
        private List<EbayListing> baseComps = new List<EbayListing>();
        private string? priceGuideQuery;


        private EbayListing? baseListing;

        public EbayListing? BaseListing
        {
            get { return baseListing; }
            set { baseListing = value; }
        }

        private CardInsights? insightsData;

        public CardInsights? InsightsData
        {
            get { return insightsData; }
            set { insightsData = value; }
        }

        #endregion


        public InsightsOverlayControl()
        {
            InitializeComponent();

            ebayListings = new ObservableCollection<EbayListing>();
            InsightsListView.ItemsSource = ebayListings;

            listingTypeFilter = "active";
            daysRangeFilter = 180;

            InsightsOverlay.IsVisible = false;
            InsightsScrim.IsVisible = false;
            InsightsOverlay.Opacity = 0;
            InsightsScrim.Opacity = 0;

            // Chart uses a custom drawable so it can look "PriceCharting-ish" without heavy libs.
            if (InsightsGraphView != null)
            {
                InsightsGraphView.Drawable = graphDrawable;
            }
        }

        // =======================================================
        //      VALUE CALLBACK (Used to send value back to page)
        // =======================================================

        // Called by EbaySearchPage when overlay is shown.
        // The overlay will invoke this when user closes the overlay
        // or clicks the "Apply Suggested Value" button.
        public Action<decimal?> OnEstimatedValueReady { get; set; }

        public async Task ShowAsync(
            EbayListing anchorListing,
            IEnumerable<EbayListing> comps,
            string listingTypeFilter = "active",
            int daysRangeFilter = 180)
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
            baseListing = anchorListing;
            insightsData = new CardInsights();
            insightsData.UsedListing = anchorListing;

            // Title is a pure UI convenience: we still treat the eBay listing + comps as the "source" for
            // what the user is deciding on.
            if (InsightsTitleLabel != null)
            {
                InsightsTitleLabel.Text = anchorListing?.Title ?? "Card Title";
            }
            this.listingTypeFilter = string.IsNullOrWhiteSpace(listingTypeFilter)
                ? "sold"
                : listingTypeFilter;
            this.daysRangeFilter = daysRangeFilter <= 0 ? 90 : daysRangeFilter;
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
            ebayListings.Clear();

            if (comps != null)
            {
                foreach (EbayListing comp in comps)
                {
                    if (comp != null)
                    {
                        ebayListings.Add(comp);
                    }
                }
            }

            // 4. Recalculate insights based on the current comps
            await RecalculateInsightsFromCurrentComps();

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

        #region Insights Calculation

        /// <summary>
        /// Recomputes all metrics (count, min, max, avg, median, suggested,
        /// volatility) and refreshes the graph.
        /// </summary>

        private async Task RecalculateInsightsFromCurrentComps()
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

                foreach (EbayListing l in ebayListings)
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
                string detectedGrade = DetectGradeFromText(baseListing.Title ?? string.Empty);

                // -----------------------------------------------------------------
                // 2) SportsCardsPro price guide snapshot (best-effort)
                // -----------------------------------------------------------------
                SportCardsProItem? scpItem = null;
                SportCardsProPricesSnapshot? scp = null;

                if (UsePriceGuideSwitch?.IsToggled == true)
                {
                    // Keep it non-blocking for the UI, but still await so graph + labels update together.
                    await sportsCardsProService.InitializeAsync();

                    string q = BuildSportsCardProQuery(baseListing);

                    if (!string.IsNullOrWhiteSpace(q))
                    {
                        scpItem = await sportsCardsProService.GetBestMatchAsync(q, cancellationToken: default);
                        scp = scpItem?.CardSnapShot;

                        if (insightsData != null)
                        {
                            insightsData.SportsCardsProItem = scpItem;
                        }
                    }
                }

                // Map SportsCardsPro keys into a stable "grade" dictionary used by the graph.
                // RAW ~= loose-price, PSA7 ~= cib-price, PSA8 ~= new-price, PSA9 ~= graded-price,
                // BGS9.5 ~= box-only-price, PSA10 ~= manual-only-price
                Dictionary<string, double?> guidePrices = new Dictionary<string, double?>
                {
                    { "raw", ToUsd(scp?.LoosePrice) },
                    { "psa7", ToUsd(scp?.CibPrice) },
                    { "psa8", ToUsd(scp?.NewPrice) },
                    { "psa9", ToUsd(scp?.GradedPrice) },
                    { "bgs95", ToUsd(scp?.BoxOnlyPrice) },
                    { "psa10", ToUsd(scp?.ManualOnlyPrice) },
                };

                double? guideBaseline = PickGuideBaseline(
                    detectedGrade,
                    guidePrices["raw"],
                    guidePrices["psa9"],
                    guidePrices["psa10"],
                    guidePrices["bgs95"],
                    guidePrices["psa10"],
                    guidePrices["psa10"],
                    guidePrices["psa10"]);

                // -----------------------------------------------------------------
                // 3) Suggested price (blend SportsCardsPro baseline + eBay median)
                // -----------------------------------------------------------------
                bool blendEbay = BlendEbaySwitch?.IsToggled == true && ebayMedian.HasValue && ebayMedian.Value > 0;

                double ebayWeight = (EbayWeightSlider?.Value ?? 30) / 100.0;
                ebayWeight = Math.Max(0, Math.Min(1, ebayWeight));

                double? suggested = null;

                if (UsePriceGuideSwitch?.IsToggled == true && guideBaseline.HasValue && guideBaseline.Value > 0)
                {
                    if (blendEbay && ebayMedian.HasValue)
                    {
                        suggested = (guideBaseline.Value * (1.0 - ebayWeight)) + (ebayMedian.Value * ebayWeight);
                    }
                    else
                    {
                        suggested = guideBaseline.Value;
                    }
                }
                else
                {
                    // No guide available: fall back to eBay-only stats.
                    suggested = ebayMedian ?? ebayAvg;
                }

                // Stamp suggested back onto the existing InsightsData object (do NOT create a new CardInsights
                // instance here; this project has multiple call sites expecting their existing reference).
                if (insightsData != null)
                {
                    insightsData.SuggestedPrice = suggested.HasValue ? (decimal)suggested.Value : 0m;
                }

                // -----------------------------------------------------------------
                // 4) Update UI labels + summary
                // -----------------------------------------------------------------
                if (InsightsSuggestedValue != null)
                {
                    InsightsSuggestedValue.Text =
                        (suggested.HasValue && suggested.Value > 0) ? $"${suggested.Value:0.00} USD" : "—";
                }

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
                    string a = ebayMin.HasValue ? $"${ebayMin.Value:0.00}" : "—";
                    string b = ebayMax.HasValue ? $"${ebayMax.Value:0.00}" : "—";
                    InsightsRangeLabel.Text = $"{a} - {b}";
                }

                if (InsightsStatsLabel != null)
                {
                    InsightsStatsLabel.Text =
                        (ebayPrices.Count > 0)
                            ? $"Listings: {ebayPrices.Count} • Median: ${(ebayMedian ?? 0):0.00} • Avg: ${(ebayAvg ?? 0):0.00}"
                            : "No listing prices available.";
                }

                if (InsightsBlendLabel != null)
                {
                    if (UsePriceGuideSwitch?.IsToggled == true && guideBaseline.HasValue && guideBaseline.Value > 0)
                    {
                        string blendTxt = blendEbay && ebayMedian.HasValue
                            ? $"Blend: {Math.Round(ebayWeight * 100, 0)}% eBay + {Math.Round((1 - ebayWeight) * 100, 0)}% Guide"
                            : "Blend: Guide only";

                        InsightsBlendLabel.Text = $"Guide baseline: {Fmt(guideBaseline)} • {blendTxt}";
                    }
                    else
                    {
                        InsightsBlendLabel.Text = "Blend: eBay only";
                    }
                }

                if (InsightsSummaryLabel != null)
                {
                    double? selected = GetEbayEffectivePrice(baseListing);
                    string buySignal = (selected.HasValue && suggested.HasValue && suggested.Value > 0)
                        ? (selected.Value <= suggested.Value * 0.80 ? "Strong buy" : selected.Value <= suggested.Value * 0.95 ? "Buy" : selected.Value <= suggested.Value * 1.10 ? "Fair" : "Overpriced")
                        : "—";

                    string guideLine = BuildPriceGuideLine(
                        detectedGrade,
                        guidePrices["raw"],
                        guidePrices["psa9"],
                        guidePrices["psa10"],
                        guidePrices["psa10"],
                        guidePrices["psa10"],
                        guidePrices["psa10"],
                        Convert.ToInt32(scp?.SalesVolume),
                        UsePriceGuideSwitch?.IsToggled == true);

                    InsightsSummaryLabel.Text = $"Signal: {buySignal} • {BuildSummaryText(suggested, guideBaseline, ebayMedian, ebayPrices.Count, detectedGrade, UsePriceGuideSwitch?.IsToggled == true, blendEbay)}";

                    if (InsightsGuideLabel != null)
                    {
                        InsightsGuideLabel.Text = guideLine;
                    }
                }

                // -----------------------------------------------------------------
                // 5) Update quick guide chips + chart
                // -----------------------------------------------------------------
                if (GuideRawLabel != null)
                {
                    GuideRawLabel.Text = Fmt(guidePrices["raw"]);
                }

                if (GuidePsa8Label != null)
                {
                    GuidePsa8Label.Text = Fmt(guidePrices["psa8"]);
                }

                if (GuidePsa10Label != null)
                {
                    GuidePsa10Label.Text = Fmt(guidePrices["psa10"]);
                }

                double volume = scp?.SalesVolume.HasValue == true
                    ? scp.SalesVolume.Value
                    : ebayPrices.Count;

                graphDrawable.SetData(
                    guidePrices,
                    compsUsd: ebayPrices,
                    suggestedUsd: suggested,
                    volume: volume);

                InsightsGraphView?.Invalidate();
            }
            catch (Exception ex)
            {
                // Keep overlay resilient - never crash UI thread.
                // But DO log it, otherwise it looks like "nothing works".
                Debug.WriteLine("[Insights] Recalculate failed: " + ex);
            }
        }

        private static double? ToUsd(long? pennies)
        {
            if (!pennies.HasValue)
            {
                return null;
            }

            if (pennies.Value <= 0)
            {
                return null;
            }

            return pennies.Value / 100.0;
        }

        private async Task LoadPriceGuideForCurrentAsync()
        {
            try
            {
                if (baseListing == null)
                {
                    return;
                }

                priceGuideQuery = BuildSportsCardProQuery(baseListing);

                if (string.IsNullOrWhiteSpace(priceGuideQuery))
                {
                    return;
                }

               
            }
            catch
            {
                // ignore
            }
        }

        private static string BuildSportsCardProQuery(EbayListing listing)
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

        private async void OnOptionToggled(object sender, ToggledEventArgs e)
        {
            try
            {
                await RecalculateInsightsFromCurrentComps();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Insights] Toggle recalc failed: " + ex.Message);
            }
        }

        private async void OnEbayWeightChanged(object sender, ValueChangedEventArgs e)
        {
            try
            {
                if (EbayWeightLabel != null)
                {
                    EbayWeightLabel.Text = $"{(int)e.NewValue}%";
                }

                await RecalculateInsightsFromCurrentComps();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Insights] Weight recalc failed: " + ex.Message);
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

        // NOTE: We used to render a simple bar chart using a FlexLayout.
        // That approach was replaced by GraphicsView + InsightsGraphDrawable so we can show:
        // - eBay scatter points (all comps)
        // - SportsCardsPro "grade" curve
        // - a suggested value line
        // without maintaining a pile of BoxViews.

        #endregion


        #region Event Handlers

        /// <summary>
        /// Fired after the overlay finishes closing.
        /// </summary>
        public event EventHandler? Closed;


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

                if (ebayListings.Contains(comp))
                {
                    ebayListings.Remove(comp);
                }

                _ = RecalculateInsightsFromCurrentComps();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[INSIGHTS REMOVE ERROR] {ex.Message}");
            }
        }

        private void ApplySuggestedValue_Clicked(object sender, EventArgs e)
        {
            if (InsightsData != null && InsightsData.SuggestedPrice.HasValue)
            {
                // Send value back to the page
                OnEstimatedValueReady?.Invoke((decimal)InsightsData.SuggestedPrice);
            }
        }



        #endregion

    }
}