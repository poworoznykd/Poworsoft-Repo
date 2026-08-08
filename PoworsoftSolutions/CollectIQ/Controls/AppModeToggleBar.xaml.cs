//
//  FILE            : AppModeToggleBar.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-12-13
//  DESCRIPTION     :
//      Futuristic top lane toggle for CollectIQ.
//      - Three neon round buttons: COLLECT, INSPECT, TRADE
//      - Visually highlights the active lane
//      - Syncs with AppModeService so the rest of the app (e.g. bottom nav)
//        can react to mode changes.
//      - Raises ModeChanged for any listeners on the page.
//
using System;
using CollectIQ.Helpers;
using CollectIQ.Navigation;
using CollectIQ.Services;
using CollectIQ.Utilities;
using CollectIQ.Views;
using Microsoft.Maui.Controls;

namespace CollectIQ.Controls
{
    public partial class AppModeToggleBar : ContentView
    {
        /// <summary>
        /// Backing service that holds the global app mode.
        /// </summary>
        private readonly AppModeService appModeService;

        /// <summary>
        /// Currently selected lane.
        /// </summary>
        public AppMode CurrentMode { get; private set; } = AppMode.Collect;

        /// <summary>
        /// Fired whenever the user changes lanes.
        /// Subscribers can show/hide tabs or switch views.
        /// </summary>
        public event EventHandler<AppMode>? ModeChanged;

        public AppModeToggleBar()
        {
            InitializeComponent();

            // Resolve the central mode service from DI via ServiceHelper.
            appModeService = ServiceHelper.Services?.GetService(typeof(AppModeService)) as AppModeService;

            if (appModeService != null)
            {
                // Start from whatever the service says.
                CurrentMode = appModeService.CurrentMode;
                ApplyModeVisuals(CurrentMode);

                // Listen for mode changes coming from somewhere else (e.g. future settings page).
                appModeService.ModeChanged += OnAppModeServiceModeChanged;
            }
            else
            {
                // Fallback: default to Collect if service is not available.
                ApplyModeVisuals(AppMode.Collect);
            }
        }

        /*
         * FUNCTION     : OnAppModeServiceModeChanged
         * DESCRIPTION  :
         *     Handles mode changes raised by AppModeService so that the
         *     top toggle stays in sync even if some other component
         *     changes the mode.
         */
        private void OnAppModeServiceModeChanged(object sender, AppMode mode)
        {
            if (CurrentMode == mode)
            {
                return;
            }

            CurrentMode = mode;
            ApplyModeVisuals(mode);

            // Bubble the event up to any subscribers.
            ModeChanged?.Invoke(this, mode);
        }

        // ------------------------------------------------------------
        //  BUTTON CLICK HANDLERS
        // ------------------------------------------------------------

        private void OnCollectClicked(object sender, EventArgs e)
        {
            // Collect is the only fully-enabled lane for now.
            SetMode(AppMode.Collect);
        }

        private async void OnInspectClicked(object sender, EventArgs e)
        {
            SetMode(AppMode.Inspect);

            if (Shell.Current?.CurrentPage is InspectHubPage)
            {
                return;
            }

            await Shell.Current.GoToAsync(nameof(InspectHubPage));
        }

        private async void OnTradeClicked(object sender, EventArgs e)
        {
            // TRADE lane is not ready yet � show reusable under construction screen.
            await ShowUnderConstructionAsync("Trade lane");
        }

        // ------------------------------------------------------------
        //  MODE MANAGEMENT
        // ------------------------------------------------------------

        /*
         * FUNCTION     : SetMode
         * DESCRIPTION  :
         *     Central helper for changing the current mode from this
         *     control. Updates visuals, raises the local ModeChanged
         *     event, and pushes the new mode into AppModeService so the
         *     rest of the app (e.g. bottom nav) updates.
         */
        private void SetMode(AppMode newMode)
        {
            if (CurrentMode == newMode)
            {
                return;
            }

            CurrentMode = newMode;
            ApplyModeVisuals(newMode);

            // Notify any page-level subscribers.
            ModeChanged?.Invoke(this, newMode);

            // Push into the central mode service so all other listeners (bottom nav, etc.)
            // get notified and update their UI.
            if (appModeService != null && appModeService.CurrentMode != newMode)
            {
                appModeService.CurrentMode = newMode;
            }
        }

        /// <summary>
        /// Applies glow / color / text changes so the selected lane
        /// looks "hot" and others look idle.
        /// </summary>
        private void ApplyModeVisuals(AppMode mode)
        {
            // Active vs inactive label colors.
            var activeColor = Microsoft.Maui.Graphics.Color.FromArgb("#4BE7F2");
            var inactiveColor = Microsoft.Maui.Graphics.Color.FromArgb("#64748B");

            // Reset all first
            CollectButton.Opacity = 0.55;
            InspectButton.Opacity = 0.55;
            TradeButton.Opacity = 0.55;

            CollectLabel.TextColor = inactiveColor;
            InspectLabel.TextColor = inactiveColor;
            TradeLabel.TextColor = inactiveColor;

            CollectLabel.FontAttributes = FontAttributes.None;
            InspectLabel.FontAttributes = FontAttributes.None;
            TradeLabel.FontAttributes = FontAttributes.None;

            // Small descriptor under "CollectIQ lanes"
            switch (mode)
            {
                case AppMode.Collect:
                    CurrentModeLabel.Text = "Collecting lane � home, scan, search, and collection.";
                    CollectButton.Opacity = 1.0;
                    CollectLabel.TextColor = activeColor;
                    CollectLabel.FontAttributes = FontAttributes.Bold;
                    break;

                case AppMode.Inspect:
                    CurrentModeLabel.Text = "Inspecting lane � centering, corners, surface checks.";
                    InspectButton.Opacity = 1.0;
                    InspectLabel.TextColor = activeColor;
                    InspectLabel.FontAttributes = FontAttributes.Bold;
                    break;

                case AppMode.BuySellTrade:
                    CurrentModeLabel.Text = "Trading lane � deals, offers, and trade block (coming soon).";
                    TradeButton.Opacity = 1.0;
                    TradeLabel.TextColor = activeColor;
                    TradeLabel.FontAttributes = FontAttributes.Bold;
                    break;
            }
        }

        /// <summary>
        /// Shows the modular under-construction screen as a modal page.
        /// The user can dismiss it and will land back wherever they were.
        /// </summary>
        private static async System.Threading.Tasks.Task ShowUnderConstructionAsync(string contextLabel)
        {
            if (Application.Current?.MainPage == null)
            {
                return;
            }

            var page = new UnderConstructionPage(contextLabel);
            await Application.Current.MainPage.Navigation.PushModalAsync(page, animated: true);
        }
    }
}
