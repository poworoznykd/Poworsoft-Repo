//
//  FILE            : FuturisticNavBar.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-11-05
//  UPDATED         : 2025-12-14
//  DESCRIPTION     :
//      Floating neon bottom navigation bar for CollectIQ.
//      - Uses AuroraGlass styling from XAML.
//      - Image-based icons inside glowable circular borders.
//      - Pulse + lift animation on tap.
//      - Tracks Shell navigation to keep highlight in sync.
//      - Listens to AppModeService so labels + icons + colours
//        change for Collect / Inspect / Trade.
//      - Slot 2 goes to InspectCenteringPage when in Inspect mode.
//
using System;
using System.Threading.Tasks;
using CollectIQ.Navigation;
using CollectIQ.Services;
using CollectIQ.Utilities;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace CollectIQ.Controls
{
    public partial class FuturisticNavBar : ContentView
    {
        /// <summary>
        /// Tracks the currently active icon layout.
        /// </summary>
        private VisualElement activeElement;

        /// <summary>
        /// Central app mode service used to keep the nav bar aligned with
        /// the currently selected lane (Collect / Inspect / Trade).
        /// </summary>
        private readonly AppModeService appModeService;

        // ============================================================
        //  CONSTRUCTOR
        // ============================================================

        public FuturisticNavBar()
        {
            InitializeComponent();

            appModeService = ServiceHelper.Services?.GetService(typeof(AppModeService)) as AppModeService;

            RegisterTapEvents();

            // Default highlight slot 1 (Home / Overview / Deals)
            HighlightActive(Slot1Wrapper);

            Shell.Current.Navigated += OnShellNavigated;

            if (appModeService != null)
            {
                ApplyModeToNavBar(appModeService.CurrentMode);
                appModeService.ModeChanged += OnAppModeChanged;
            }
            else
            {
                ApplyModeToNavBar(AppMode.Collect);
            }
        }

        // ============================================================
        //  APP MODE INTEGRATION
        // ============================================================

        private void OnAppModeChanged(object sender, AppMode mode)
        {
            ApplyModeToNavBar(mode);
            ResetIconVisuals();

            // Re-highlight current element with new mode colours
            HighlightActive(activeElement ?? Slot1Wrapper);
        }

        /*
         * FUNCTION     : ApplyModeToNavBar
         * DESCRIPTION  :
         *     Changes the label text and icon for each slot in the bottom nav
         *     based on the specified application mode.
         */
        private void ApplyModeToNavBar(AppMode mode)
        {
            try
            {
                switch (mode)
                {
                    case AppMode.Collect:
                        SetSlotLabel(Slot1Wrapper, "Home");
                        SetSlotLabel(Slot2Wrapper, "Scan");
                        SetSlotLabel(Slot3Wrapper, "Collection");
                        SetSlotLabel(Slot4Wrapper, "Search");

                        SetSlotIcon(Slot1Wrapper, "home_icon.png");
                        SetSlotIcon(Slot2Wrapper, "scan_icon.png");
                        SetSlotIcon(Slot3Wrapper, "collection_icon.png");
                        SetSlotIcon(Slot4Wrapper, "search_icon.png");
                        break;

                    case AppMode.Inspect:
                        SetSlotLabel(Slot1Wrapper, "Overview");
                        SetSlotLabel(Slot2Wrapper, "Centering");
                        SetSlotLabel(Slot3Wrapper, "Surface");
                        SetSlotLabel(Slot4Wrapper, "Corners");

                        SetSlotIcon(Slot1Wrapper, "overview_icon.png");
                        SetSlotIcon(Slot2Wrapper, "centering_icon.png");
                        SetSlotIcon(Slot3Wrapper, "surface_icon.png");
                        SetSlotIcon(Slot4Wrapper, "corners_icon.png");
                        break;

                    case AppMode.BuySellTrade:
                        SetSlotLabel(Slot1Wrapper, "Deals");
                        SetSlotLabel(Slot2Wrapper, "Sell");
                        SetSlotLabel(Slot3Wrapper, "Buy");
                        SetSlotLabel(Slot4Wrapper, "Trade Block");

                        // For now, reuse Collect icon set for Trade.
                        SetSlotIcon(Slot1Wrapper, "home_icon.png");
                        SetSlotIcon(Slot2Wrapper, "scan_icon.png");
                        SetSlotIcon(Slot3Wrapper, "collection_icon.png");
                        SetSlotIcon(Slot4Wrapper, "search_icon.png");
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FuturisticNavBar] ApplyModeToNavBar error: {ex.Message}");
            }
        }

        private static void SetSlotLabel(Layout wrapper, string text)
        {
            foreach (var child in wrapper.Children)
            {
                if (child is Label label)
                {
                    label.Text = text;
                    break;
                }
            }
        }

        private static void SetSlotIcon(Layout wrapper, string iconSource)
        {
            foreach (var child in wrapper.Children)
            {
                if (child is Border border && border.Content is Image image)
                {
                    image.Source = iconSource;
                    break;
                }
            }
        }

        // ============================================================
        //  NAVIGATION AND TAP HANDLERS
        // ============================================================

        private void RegisterTapEvents()
        {
            // Slot 1 – Home / Overview / Deals
            Slot1Wrapper.GestureRecognizers.Add(
                CreateTapHandler(
                    Slot1Wrapper,
                    mode =>
                    {
                        return "//DashboardPage";
                    }));

            // Slot 2 – Scan / Centering / Sell
            Slot2Wrapper.GestureRecognizers.Add(
                CreateTapHandler(
                    Slot2Wrapper,
                    mode =>
                    {
                        if (mode == AppMode.Inspect)
                        {
                            return "//InspectCenteringPage";
                        }

                        return "//ScanPage";
                    }));

            // Slot 3 – Collection / Surface / Buy
            Slot3Wrapper.GestureRecognizers.Add(
                CreateTapHandler(
                    Slot3Wrapper,
                    mode =>
                    {
                        return "//CollectionPage";
                    }));

            // Slot 4 – Search / Corners / Trade Block
            Slot4Wrapper.GestureRecognizers.Add(
                CreateTapHandler(
                    Slot4Wrapper,
                    mode =>
                    {
                        return "//EbaySearchPage";
                    }));
        }

        private TapGestureRecognizer CreateTapHandler(
            VisualElement element,
            Func<AppMode, string> routeSelector)
        {
            var tap = new TapGestureRecognizer();

            tap.Tapped += async (s, e) =>
            {
                var mode = appModeService?.CurrentMode ?? AppMode.Collect;
                var route = routeSelector(mode);

                if (string.IsNullOrWhiteSpace(route))
                {
                    return;
                }

                await HandleTapAsync(element, route);
            };

            return tap;
        }

        private async Task HandleTapAsync(VisualElement element, string route)
        {
            try
            {
                if (activeElement == element)
                {
                    return;
                }

                await AnimatePulse(element);
                HighlightActive(element);

                await Shell.Current.GoToAsync(route, false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NavBar] Navigation error: {ex.Message}");
            }
        }

        // ============================================================
        //  ACTIVE PAGE TRACKING
        // ============================================================

        private void OnShellNavigated(object sender, ShellNavigatedEventArgs e)
        {
            try
            {
                string currentRoute = e.Current?.Location?.ToString() ?? string.Empty;

                if (currentRoute.Contains("DashboardPage", StringComparison.OrdinalIgnoreCase))
                {
                    HighlightActive(Slot1Wrapper);
                }
                else if (currentRoute.Contains("ScanPage", StringComparison.OrdinalIgnoreCase) ||
                         currentRoute.Contains("InspectCenteringPage", StringComparison.OrdinalIgnoreCase))
                {
                    HighlightActive(Slot2Wrapper);
                }
                else if (currentRoute.Contains("CollectionPage", StringComparison.OrdinalIgnoreCase))
                {
                    HighlightActive(Slot3Wrapper);
                }
                else if (currentRoute.Contains("EbaySearchPage", StringComparison.OrdinalIgnoreCase))
                {
                    HighlightActive(Slot4Wrapper);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NavBar] Route tracking error: {ex.Message}");
            }
        }

        // ============================================================
        //  VISUAL EFFECTS
        // ============================================================

        private async Task AnimatePulse(VisualElement element)
        {
            try
            {
                await Task.WhenAll(
                    element.ScaleTo(1.15, 120, Easing.CubicOut),
                    element.TranslateTo(0, -5, 120, Easing.CubicOut)
                );

                await Task.WhenAll(
                    element.ScaleTo(1.0, 200, Easing.CubicIn),
                    element.TranslateTo(0, 0, 200, Easing.CubicIn)
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NavBar] Animation error: {ex.Message}");
            }
        }

        private void HighlightActive(VisualElement element)
        {
            try
            {
                var mode = appModeService?.CurrentMode ?? AppMode.Collect;

                ResetIconVisuals();

                if (element is Layout layout)
                {
                    Color activeColor = GetActiveColor(mode);

                    foreach (var child in layout.Children)
                    {
                        if (child is Border border && border.Content is Image image)
                        {
                            image.Opacity = 1.0;

                            // Neon ring + glow
                            border.StrokeThickness = 2;
                            border.Stroke = new SolidColorBrush(activeColor);
                            border.Shadow = new Shadow
                            {
                                Brush = new SolidColorBrush(activeColor),
                                Radius = 24,
                                Offset = new Point(0, 8)
                            };
                        }

                        if (child is Label label)
                        {
                            label.TextColor = activeColor;
                            label.FontAttributes = FontAttributes.Bold;
                        }
                    }
                }

                activeElement = element;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NavBar] Highlight error: {ex.Message}");
            }
        }

        private void ResetIconVisuals()
        {
            AppMode mode = appModeService?.CurrentMode ?? AppMode.Collect;

            ResetSlotVisuals(Slot1Wrapper, mode);
            ResetSlotVisuals(Slot2Wrapper, mode);
            ResetSlotVisuals(Slot3Wrapper, mode);
            ResetSlotVisuals(Slot4Wrapper, mode);
        }

        private void ResetSlotVisuals(VisualElement wrapper, AppMode mode)
        {
            Color baseColor = GetBaseColor(mode);

            if (wrapper is Layout layout)
            {
                foreach (var child in layout.Children)
                {
                    if (child is Border border && border.Content is Image image)
                    {
                        image.Opacity = 0.85;
                        border.StrokeThickness = 0;
                        border.Stroke = new SolidColorBrush(Colors.Transparent);
                        border.Shadow = null;
                    }

                    if (child is Label label)
                    {
                        label.TextColor = baseColor;
                        label.FontAttributes = FontAttributes.None;
                    }
                }
            }
        }

        // ============================================================
        //  COLOUR HELPERS (PER MODE)
        // ============================================================

        private static Color GetBaseColor(AppMode mode)
        {
            // Collect: cyan, Inspect: purple, Trade: orange
            return mode switch
            {
                AppMode.Inspect => Color.FromArgb("#C084FC"), // purple base
                AppMode.BuySellTrade => Color.FromArgb("#F97316"),   // orange base
                _ => Color.FromArgb("#0ACAF9")                // cyan base
            };
        }

        private static Color GetActiveColor(AppMode mode)
        {
            // Collect: bright blue, Inspect: brighter purple, Trade: lighter orange
            return mode switch
            {
                AppMode.Inspect => Color.FromArgb("#E879F9"),
                AppMode.BuySellTrade => Color.FromArgb("#FDBA74"),
                _ => Color.FromArgb("#00E0FF")                // Collect active blue
            };
        }
    }
}
