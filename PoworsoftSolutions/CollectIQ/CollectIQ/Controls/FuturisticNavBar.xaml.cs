/*
* FILE: FuturisticNavBar.xaml.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-11-05
* DESCRIPTION:
*   Floating neon navigation bar with animated pulse/lift and active-tab glow.
*   Dynamically reacts to Shell navigation state to highlight the active icon.
*/

using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace CollectIQ.Controls
{
    public partial class FuturisticNavBar : ContentView
    {
        /// <summary>
        /// Tracks the currently active icon layout.
        /// </summary>
        private VisualElement _activeElement;

        // ============================================================
        //  CONSTRUCTOR
        // ============================================================

        public FuturisticNavBar()
        {
            InitializeComponent();
            RegisterTapEvents();

            // Default highlight
            HighlightActive(HomeWrapper);

            // Subscribe to navigation changes
            Shell.Current.Navigated += OnShellNavigated;
        }

        // ============================================================
        //  NAVIGATION AND TAP HANDLERS
        // ============================================================

        /// <summary>
        /// Attaches tap handlers to each icon wrapper.
        /// </summary>
        private void RegisterTapEvents()
        {
            HomeWrapper.GestureRecognizers.Add(CreateTapHandler(HomeWrapper, "//DashboardPage"));
            ScanWrapper.GestureRecognizers.Add(CreateTapHandler(ScanWrapper, "//ScanPage"));
            CollectionWrapper.GestureRecognizers.Add(CreateTapHandler(CollectionWrapper, "//CollectionPage"));
            SearchWrapper.GestureRecognizers.Add(CreateTapHandler(SearchWrapper, "//EbaySearchPage"));
        }

        /// <summary>
        /// Creates a tap gesture handler that triggers pulse animation and navigation.
        /// </summary>
        private TapGestureRecognizer CreateTapHandler(VisualElement element, string route)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (s, e) => await HandleTapAsync(element, route);
            return tap;
        }

        /// <summary>
        /// Handles user tapping a nav icon (animation + navigation).
        /// </summary>
        private async Task HandleTapAsync(VisualElement element, string route)
        {
            try
            {
                // Prevent redundant navigation
                if (_activeElement == element)
                    return;

                await AnimatePulse(element);
                HighlightActive(element);

                // Navigate to target page route
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

        /// <summary>
        /// Automatically updates highlight when user navigates via Shell.
        /// </summary>
        private void OnShellNavigated(object sender, ShellNavigatedEventArgs e)
        {
            try
            {
                string currentRoute = e.Current?.Location?.ToString() ?? string.Empty;

                if (currentRoute.Contains("DashboardPage"))
                    HighlightActive(HomeWrapper);
                else if (currentRoute.Contains("ScanPage"))
                    HighlightActive(ScanWrapper);
                else if (currentRoute.Contains("CollectionPage"))
                    HighlightActive(CollectionWrapper);
                else if (currentRoute.Contains("EbaySearchPage"))
                    HighlightActive(SearchWrapper);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NavBar] Route tracking error: {ex.Message}");
            }
        }

        // ============================================================
        //  VISUAL EFFECTS
        // ============================================================

        /// <summary>
        /// Creates a lift + pulse animation for visual feedback.
        /// </summary>
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

        /// <summary>
        /// Applies neon glow to the active icon, resetting others to base color.
        /// </summary>
        private void HighlightActive(VisualElement element)
        {
            try
            {
                ResetIconColors();

                if (element is Layout layout)
                {
                    foreach (var child in layout.Children)
                    {
                        if (child is Microsoft.Maui.Controls.Shapes.Path path)
                            path.Stroke = new SolidColorBrush(Color.FromArgb("#39FF14")); // neon green
                        if (child is Label label)
                            label.TextColor = Color.FromArgb("#39FF14");
                    }
                }

                _activeElement = element;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NavBar] Highlight error: {ex.Message}");
            }
        }

        /// <summary>
        /// Restores all icons to base electric blue.
        /// </summary>
        private void ResetIconColors()
        {
            SetIconBaseColor(HomeWrapper);
            SetIconBaseColor(ScanWrapper);
            SetIconBaseColor(CollectionWrapper);
            SetIconBaseColor(SearchWrapper);
        }

        /// <summary>
        /// Sets the base color for an icon wrapper.
        /// </summary>
        private void SetIconBaseColor(VisualElement wrapper)
        {
            if (wrapper is Layout layout)
            {
                foreach (var child in layout.Children)
                {
                    if (child is Microsoft.Maui.Controls.Shapes.Path path)
                        path.Stroke = new SolidColorBrush(Color.FromArgb("#0acaf9")); // cyan base
                    if (child is Label label)
                        label.TextColor = Color.FromArgb("#0acaf9");
                }
            }
        }
    }
}
