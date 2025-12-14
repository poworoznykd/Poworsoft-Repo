/*
* FILE: InsightsOverlayControl.xaml.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-11-28
* UPDATED: 2025-11-29
* DESCRIPTION:
*     Code-behind for the Insights overlay component. Provides animation,
*     filtering (Active / Sold / Both), lightweight stats calculation,
*     and graph rendering. Self-contained and requires no external
*     static resources for gesture handlers.
*/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CollectIQ.Models;
using Microsoft.Maui.Controls;

namespace CollectIQ.Controls
{
    public partial class InsightsOverlayControl : ContentView
    {
        private readonly ObservableCollection<EbayListing> _allListings;
        private readonly ObservableCollection<EbayListing> _visibleListings;

        private EbayListing? _anchor;

        // filter mode: "active", "sold", "both"
        private string _filterMode = "sold";

        // visual buttons
        private Border BtnActive;
        private Border BtnSold;
        private Border BtnBoth;

        public event EventHandler? Closed;

        public InsightsOverlayControl()
        {
            InitializeComponent();

            _allListings = new ObservableCollection<EbayListing>();
            _visibleListings = new ObservableCollection<EbayListing>();

            InsightsListView.ItemsSource = _visibleListings;

            // bind to XAML visual elements (created after InitializeComponent)
            BtnActive = this.FindByName<Border>("BtnActive");
            BtnSold = this.FindByName<Border>("BtnSold");
            BtnBoth = this.FindByName<Border>("BtnBoth");

            // assign gestures programmatically (avoids resource issues)
            BtnActive.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => SetFilter("active")) });
            BtnSold.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => SetFilter("sold")) });
            BtnBoth.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => SetFilter("both")) });

            InsightsOverlay.IsVisible = false;
            InsightsScrim.IsVisible = false;
            InsightsOverlay.Opacity = 0;
            InsightsScrim.Opacity = 0;
        }

        // ================================================================
        // PUBLIC API
        // ================================================================

        public async Task ShowAsync(
            EbayListing anchorListing,
            IEnumerable<EbayListing> comps,
            string defaultFilterMode = "sold")
        {
            _anchor = anchorListing;
            _filterMode = defaultFilterMode;

            _allListings.Clear();
            foreach (var c in comps)
                if (c != null)
                    _allListings.Add(c);

            ApplyFilter(_filterMode);

            UpdateStatsAndGraph();

            if (!InsightsOverlay.IsVisible)
            {
                InsightsOverlay.IsVisible = true;
                InsightsScrim.IsVisible = true;

                InsightsOverlay.Opacity = 0;
                InsightsOverlay.TranslationY = 60;
                InsightsScrim.Opacity = 0;

                await Task.WhenAll(
                    InsightsOverlay.FadeTo(1, 200, Easing.CubicOut),
                    InsightsOverlay.TranslateTo(0, 0, 200, Easing.CubicOut),
                    InsightsScrim.FadeTo(1, 200, Easing.CubicOut)
                );
            }
        }

        public async Task HideAsync()
        {
            if (!InsightsOverlay.IsVisible)
                return;

            await Task.WhenAll(
                InsightsScrim.FadeTo(0, 150, Easing.CubicIn),
                InsightsOverlay.FadeTo(0, 150, Easing.CubicIn),
                InsightsOverlay.TranslateTo(0, 60, 150, Easing.CubicIn)
            );

            InsightsOverlay.IsVisible = false;
            InsightsScrim.IsVisible = false;

            Closed?.Invoke(this, EventArgs.Empty);
        }

        // ================================================================
        // FILTERING
        // ================================================================

        private void SetFilter(string mode)
        {
            _filterMode = mode;
            ApplyFilter(mode);
            UpdateStatsAndGraph();
        }

        private void ApplyFilter(string mode)
        {
            _visibleListings.Clear();

            IEnumerable<EbayListing> filtered = mode switch
            {
                "active" => _allListings.Where(x => !x.IsSold),
                "sold" => _allListings.Where(x => x.IsSold),
                "both" => _allListings,
                _ => _allListings
            };

            foreach (var l in filtered)
                _visibleListings.Add(l);

            // OPTIONAL: soft highlight for the selected filter
            HighlightSelectedFilter(mode);
        }

        private void HighlightSelectedFilter(string mode)
        {
            ResetFilterBorders();

            switch (mode)
            {
                case "active":
                    BtnActive.BorderColor = Color.FromArgb("#7CF9FF");
                    break;

                case "sold":
                    BtnSold.BorderColor = Color.FromArgb("#7CF9FF");
                    break;

                case "both":
                    BtnBoth.BorderColor = Color.FromArgb("#7CF9FF");
                    break;
            }
        }

        private void ResetFilterBorders()
        {
            BtnActive.BorderColor = Colors.Transparent;
            BtnSold.BorderColor = Colors.Transparent;
            BtnBoth.BorderColor = Colors.Transparent;
        }

        // ================================================================
        // EVENT HANDLERS
        // ================================================================

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
                EbayListing? listing = null;

                if (sender is SwipeItem swipe &&
                    swipe.CommandParameter is EbayListing l)
                    listing = l;

                if (listing == null)
                    return;

                if (_allListings.Contains(listing))
                    _allListings.Remove(listing);

                if (_visibleListings.Contains(listing))
                    _visibleListings.Remove(listing);

                UpdateStatsAndGraph();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[INSIGHTS REMOVE] {ex}");
            }
        }

        // ================================================================
        // METRICS + GRAPH
        // ================================================================

        private void UpdateStatsAndGraph()
        {
            InsightsGraphLayout.Children.Clear();

            var items = _visibleListings.Where(x => x.Price.HasValue && x.Price.Value > 0)
                                        .ToList();

            if (items.Count == 0)
            {
                InsightsTitleLabel.Text = _anchor?.Title ?? "Selected Card";
                InsightsSummaryLabel.Text = "No market data available.";
                InsightsCountValue.Text = "0";
                InsightsMinValue.Text = "$0.00";
                InsightsMaxValue.Text = "$0.00";
                InsightsAvgValue.Text = "$0.00";
                InsightsMedianValue.Text = "$0.00";
                InsightsSuggestedValue.Text = "$0.00";
                return;
            }

            var prices = items.Select(x => x.Price!.Value).OrderBy(p => p).ToList();

            int count = prices.Count;
            decimal min = prices.First();
            decimal max = prices.Last();
            decimal avg = prices.Average();
            decimal median = Median(prices);
            decimal suggested = Math.Round(median * 0.95m, 2);

            InsightsCountValue.Text = count.ToString();
            InsightsMinValue.Text = $"${min:F2}";
            InsightsMaxValue.Text = $"${max:F2}";
            InsightsAvgValue.Text = $"${avg:F2}";
            InsightsMedianValue.Text = $"${median:F2}";
            InsightsSuggestedValue.Text = $"${suggested:F2}";

            InsightsTitleLabel.Text = _anchor?.Title ?? "Selected Card";
            InsightsSummaryLabel.Text =
                $"Median ${median:F2}. Range ${min:F2}–${max:F2}. Avg ${avg:F2}.";

            BuildBars(prices, min, max, median);
        }

        private static decimal Median(List<decimal> data)
        {
            if (data.Count == 0) return 0;

            int mid = data.Count / 2;

            if (data.Count % 2 == 1)
                return data[mid];
            else
                return (data[mid - 1] + data[mid]) / 2m;
        }

        private void BuildBars(List<decimal> sortedPrices, decimal min, decimal max, decimal median)
        {
            double minHeight = 18;
            double maxHeight = 60;

            void AddBar(string label, decimal price, string colorHex)
            {
                if (price <= 0) return;

                double ratio = (double)((price - min) / (max - min));
                double height = minHeight + ratio * (maxHeight - minHeight);

                var bar = new BoxView
                {
                    WidthRequest = 14,
                    HeightRequest = height,
                    Color = Color.FromArgb(colorHex),
                    CornerRadius = 4,
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

                var stack = new VerticalStackLayout
                {
                    Spacing = 2,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.End
                };

                stack.Children.Add(bar);
                stack.Children.Add(priceLabel);
                stack.Children.Add(new Label
                {
                    Text = label,
                    FontSize = 10,
                    TextColor = Colors.White,
                    HorizontalTextAlignment = TextAlignment.Center
                });

                InsightsGraphLayout.Children.Add(stack);
            }

            AddBar("Min", min, "#7CFC7C");
            AddBar("Median", median, "#00E5FF");
            AddBar("Max", max, "#FFB347");
        }
    }
}
