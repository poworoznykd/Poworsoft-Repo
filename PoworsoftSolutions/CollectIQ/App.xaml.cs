//
//  FILE            : App.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-25
//  UPDATED         : 2026-06-05
//  DESCRIPTION     :
//      Entry point for the CollectIQ application. Uses dependency injection
//      for database and authentication services, initializes local storage,
//      and chooses the correct startup page based on the current session.
//

using CollectIQ.Interfaces;
using CollectIQ.Views;
using Microsoft.Maui.Controls;

namespace CollectIQ
{
    /// <summary>
    /// Represents the root MAUI application object for CollectIQ.
    /// </summary>
    public partial class App : Application
    {
        #region Public Properties

        /// <summary>
        /// Gets the application database service. This keeps older pages working
        /// while the app is migrated toward repository-based access.
        /// </summary>
        public static IDatabase Database { get; private set; } = null!;

        #endregion

        #region Private Members

        private readonly IAuthService authService;
        private readonly IDatabase database;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes the application using services supplied by the MAUI
        /// dependency injection container.
        /// </summary>
        /// <param name="authService">The authentication service.</param>
        /// <param name="database">The local database service.</param>
        public App(IAuthService authService, IDatabase database)
        {
            InitializeComponent();

            this.authService = authService ?? throw new ArgumentNullException(nameof(authService));
            this.database = database ?? throw new ArgumentNullException(nameof(database));

            Database = this.database;

            MainPage = new NavigationPage(new LandingPage(this.authService))
            {
                BarBackgroundColor = Color.FromArgb("#0B0B0D"),
                BarTextColor = Color.FromArgb("#00B4FF")
            };
        }

        #endregion

        #region Lifecycle Events

        /// <summary>
        /// Initializes local storage and routes the user to the correct startup page.
        /// </summary>
        protected override async void OnStart()
        {
            base.OnStart();

            await database.InitializeAsync();

            bool isSignedIn = await authService.IsSignedInAsync();

            if (isSignedIn)
            {
                MainPage = new AppShell();
                return;
            }

            MainPage = new NavigationPage(new AuthSheet(authService))
            {
                BarBackgroundColor = Color.FromArgb("#0B0B0D"),
                BarTextColor = Color.FromArgb("#00B4FF")
            };
        }

        #endregion
    }
}
