//
//  FILE            : AppModeToggleBar.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-12-13
//  DESCRIPTION     :
//      Futuristic top lane toggle for CollectIQ.
//      - Three neon round buttons: COLLECT, INSPECT, TRADE
//      - Visually highlights the active lane
//      - Raises ModeChanged so pages can react later
//

using System;
using CollectIQ.Navigation;
using Microsoft.Maui.Controls;

namespace CollectIQ.Controls
{
    public partial class AppModeToggleBar : ContentView
    {
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
            ApplyModeVisuals(AppMode.Collect);
        }

        // ------------------------------------------------------------
        //  BUTTON CLICK HANDLERS
        // ------------------------------------------------------------

        private void OnCollectClicked(object sender, EventArgs e)
        {
            SetMode(AppMode.Collect);
        }

        private void OnInspectClicked(object sender, EventArgs e)
        {
            SetMode(AppMode.Inspect);
        }

        private void OnTradeClicked(object sender, EventArgs e)
        {
            SetMode(AppMode.Trade);
        }

        // ------------------------------------------------------------
        //  MODE MANAGEMENT
        // ------------------------------------------------------------

        private void SetMode(AppMode newMode)
        {
            if (CurrentMode == newMode)
            {
                return;
            }

            CurrentMode = newMode;
            ApplyModeVisuals(newMode);

            ModeChanged?.Invoke(this, newMode);

            // If you wire up an AppModeService later, you can also do:
            // var svc = Helpers.ServiceHelper.GetService<AppModeService>();
            // svc?.SetMode(newMode);
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
                    CurrentModeLabel.Text = "Collecting lane – home, scan, search, and collection.";
                    CollectButton.Opacity = 1.0;
                    CollectLabel.TextColor = activeColor;
                    CollectLabel.FontAttributes = FontAttributes.Bold;
                    break;

                case AppMode.Inspect:
                    CurrentModeLabel.Text = "Inspecting lane – centering, corners, surface checks.";
                    InspectButton.Opacity = 1.0;
                    InspectLabel.TextColor = activeColor;
                    InspectLabel.FontAttributes = FontAttributes.Bold;
                    break;

                case AppMode.Trade:
                    CurrentModeLabel.Text = "Trading lane – deals, offers, and trade block (coming soon).";
                    TradeButton.Opacity = 1.0;
                    TradeLabel.TextColor = activeColor;
                    TradeLabel.FontAttributes = FontAttributes.Bold;
                    break;
            }
        }
    }
}
