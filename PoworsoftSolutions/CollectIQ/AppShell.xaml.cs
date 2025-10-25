using CollectIQ.Views;

namespace CollectIQ
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Shell.SetBackgroundColor(this, Color.FromArgb("#0B0B0D")); // deep black
            Shell.SetTabBarTitleColor(this, Color.FromArgb("#00B4FF"));      // neon blue
            Routing.RegisterRoute(nameof(LandingPage), typeof(LandingPage));
            Routing.RegisterRoute(nameof(AuthSheet), typeof(AuthSheet));
            Routing.RegisterRoute(nameof(DashboardPage), typeof(DashboardPage));
            Routing.RegisterRoute(nameof(ScanPage), typeof(ScanPage));
            Routing.RegisterRoute(nameof(EbaySearchPage), typeof(EbaySearchPage));
            Routing.RegisterRoute(nameof(CollectionPage), typeof(CollectionPage));
        }
    }
}
