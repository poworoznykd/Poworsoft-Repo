//
//  FILE            : App.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  DESCRIPTION     : Application entry point using the single DI-owned SQLite/auth instances.
//

using CollectIQ.Interfaces;
using CollectIQ.Services;
using CollectIQ.Views;

namespace CollectIQ
{
    public partial class App : Application
    {
        /// <summary>
        /// Legacy access point used by existing pages. It is the exact same singleton
        /// SqliteDatabase instance registered with dependency injection.
        /// </summary>
        public static SqliteDatabase Database { get; private set; } = null!;

        private readonly IAuthService authService;
        private readonly ISyncService syncService;

        public App(SqliteDatabase database, IAuthService authService, ISyncService syncService)
        {
            InitializeComponent();
            Database = database ?? throw new ArgumentNullException(nameof(database));
            this.authService = authService ?? throw new ArgumentNullException(nameof(authService));
            this.syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
            MainPage = new LandingPage(this.authService);
        }

        protected override async void OnStart()
        {
            base.OnStart();

            try
            {
                await Database.InitializeAsync();
                bool isSignedIn = await authService.IsSignedInAsync();

                if (isSignedIn)
                {
                    MainPage = new AppShell();
                    _ = syncService.SyncAsync();
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CollectIQ STARTUP] {ex}");
            }

            MainPage = new NavigationPage(new AuthSheet(authService))
            {
                BarBackgroundColor = Color.FromArgb("#0B0B0D"),
                BarTextColor = Color.FromArgb("#00B4FF")
            };
        }
        protected override void OnResume()
        {
            base.OnResume();

            // Retry any offline changes whenever the app returns to the foreground.
            _ = syncService.SyncAsync();
        }

    }
}
