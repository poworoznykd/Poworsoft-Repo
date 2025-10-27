/*
* FILE: AppShell.xaml.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-25
* DESCRIPTION:
*     Defines the global navigation structure and visual theme
*     for the CollectIQ mobile application. Registers routes for
*     all pages and applies consistent shell color styling.
*/

using CollectIQ.Views;
using Microsoft.Maui.Controls;

namespace CollectIQ
{
    public partial class AppShell : Shell
    {
        /// <summary>
        /// Initializes the app shell, theme, and all navigation routes.
        /// </summary>
        public AppShell()
        {
            InitializeComponent();

            // --- Theme and Navigation Bar Styling ---
            Shell.SetBackgroundColor(this, Color.FromArgb("#0B0B0D"));     // Deep black background
            Shell.SetTabBarTitleColor(this, Color.FromArgb("#00B4FF"));    // Neon blue tab text
            Shell.SetTabBarUnselectedColor(this, Color.FromArgb("#4A4A4A"));
            Shell.SetForegroundColor(this, Color.FromArgb("#00B4FF"));

            // --- Page Route Registrations ---
            Routing.RegisterRoute(nameof(LandingPage), typeof(LandingPage));
            Routing.RegisterRoute(nameof(AuthSheet), typeof(AuthSheet));
            Routing.RegisterRoute(nameof(DashboardPage), typeof(DashboardPage));
            Routing.RegisterRoute(nameof(ScanPage), typeof(ScanPage));
            Routing.RegisterRoute(nameof(EbaySearchPage), typeof(EbaySearchPage));
            Routing.RegisterRoute(nameof(CollectionPage), typeof(CollectionPage));
        }
    }
}
