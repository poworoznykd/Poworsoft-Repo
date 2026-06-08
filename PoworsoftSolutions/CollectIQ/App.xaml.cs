//
//  FILE            : App.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-25
//  DESCRIPTION     :
//      Entry point for the CollectIQ application. Initializes
//      the SQLite database, applies the dark neon theme,
//      and determines whether to launch LandingPage, 
//      AuthSheet, or AppShell based on authentication state.
//
using CollectIQ.Views;
using CollectIQ.Services;
using Microsoft.Maui.Controls;
using System.Threading.Tasks;

namespace CollectIQ
{
    public partial class App : Application
    {
        // ============================================================
        //  GLOBAL DATABASE ACCESS
        // ============================================================
        public static SqliteDatabase Database { get; } = new SqliteDatabase();

        // ============================================================
        //  PRIVATE FIELDS
        // ============================================================
        private readonly LocalAuthService _authService;

        // ============================================================
        //  CONSTRUCTOR
        // ============================================================
        public App()
        {
            InitializeComponent();
            // Do NOT wipe SecureStorage at startup; this breaks persisted login.
            // SecureStorage.RemoveAll();
            // Initialize authentication and database service
            _authService = new LocalAuthService(Database);

            // Display the landing screen initially
            MainPage = new LandingPage(_authService);
        }

        // ============================================================
        //  LIFECYCLE EVENT - OnStart
        // ============================================================
        protected override async void OnStart()
        {
            base.OnStart();

            // Ensure SQLite tables exist
            await Database.InitializeAsync();

            // Check if the user is signed in using LocalAuthService
            bool isSignedIn = await _authService.IsSignedInAsync();

            if (isSignedIn)
            {
                // --------------------------------------------------------
                // Authenticated user → Load main app shell (Dashboard)
                // --------------------------------------------------------
                MainPage = new AppShell();
            }
            else
            {
                // --------------------------------------------------------
                // Unauthenticated user → Show login/auth sheet
                // --------------------------------------------------------
                MainPage = new NavigationPage(new AuthSheet(_authService))
                {
                    BarBackgroundColor = Color.FromArgb("#0B0B0D"),
                    BarTextColor = Color.FromArgb("#00B4FF")
                };
            }
        }
    }
}
