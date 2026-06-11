//
//  FILE            : App.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-25
//  UPDATED         : 2026-06-10
//  DESCRIPTION     :
//      Entry point for the CollectIQ application. Initializes the SQLite
//      database, applies the dark neon theme, and starts the authentication
//      flow using the same social-auth-enabled authentication service used
//      by dependency injection.
//

using CollectIQ.Services;
using CollectIQ.Views;
using Microsoft.Maui.Controls;

namespace CollectIQ
{
    /// <summary>
    /// Main CollectIQ application class.
    /// </summary>
    public partial class App : Application
    {
        #region Public Properties

        /// <summary>
        /// Gets the local SQLite database used by the app as the current user's authorized cache.
        /// </summary>
        public static SqliteDatabase Database { get; } = new SqliteDatabase();

        #endregion

        #region Private Fields

        private readonly LocalAuthService authService;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the App class.
        /// </summary>
        public App()
        {
            InitializeComponent();

            // Do not wipe SecureStorage at startup. That would break remembered sessions.
            // SecureStorage.RemoveAll();

            // Important: this must include the real Supabase social broker. If this is created
            // with only new LocalAuthService(Database), Google/Facebook will return
            // "Social authentication service is not available.".
            this.authService = new LocalAuthService(Database, new SupabaseAuthService());

            // Keep the initial page simple and stable for Android startup.
            MainPage = new LandingPage(this.authService);
        }

        #endregion

        #region Lifecycle Methods

        /// <summary>
        /// Initializes the database and routes the user based on sign-in state.
        /// </summary>
        protected override async void OnStart()
        {
            base.OnStart();

            await Database.InitializeAsync();

            bool isSignedIn = await this.authService.IsSignedInAsync();

            if (isSignedIn)
            {
                MainPage = new AppShell();
                return;
            }

            MainPage = new NavigationPage(new AuthSheet(this.authService))
            {
                BarBackgroundColor = Color.FromArgb("#0B0B0D"),
                BarTextColor = Color.FromArgb("#00B4FF")
            };
        }

        #endregion
    }
}
