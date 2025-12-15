/*
* FILE: AppShell.xaml.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-25
* UPDATED: 2025-12-14
* DESCRIPTION:
*     Defines the global navigation structure and visual theme
*     for the CollectIQ mobile application. Registers routes for
*     all pages and applies consistent shell color styling,
*     including the Inspect centering flow.
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
            Routing.RegisterRoute(nameof(CardPage), typeof(CardPage));
            Routing.RegisterRoute(nameof(ImageViewerPage), typeof(ImageViewerPage));

            // These are driven by the floating FuturisticNavBar:
            // DashboardPage, ScanPage, EbaySearchPage, CollectionPage.

            // Inspect lane – centering inspector page
            Routing.RegisterRoute(nameof(InspectCenteringPage), typeof(InspectCenteringPage));
        }
    }
}
