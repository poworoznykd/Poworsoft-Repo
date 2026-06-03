using CollectIQ.Data;

namespace CollectIQ2._0
{
    public partial class App : Application
    {
        #region Public Properties

        /// <summary>
        /// Gets the shared local SQLite database service.
        /// </summary>
        public static CollectIqDatabase Database
        {
            get;
            private set;
        }

        #endregion

        #region Constructor

        /******************************************************************************
         *
         * METHOD      : App
         *
         * DESCRIPTION :
         *
         * Initializes the application, creates the local database service, and loads
         * the application shell.
         *
         *****************************************************************************/
        public App()
        {
            InitializeComponent();

            Database = new CollectIqDatabase();

            MainPage = new AppShell();
        }

        #endregion

        #region Protected Methods

        /******************************************************************************
         *
         * METHOD      : OnStart
         *
         * DESCRIPTION :
         *
         * Initializes the local SQLite database when the application starts.
         *
         * NOTE:
         *
         * This method is async void because it overrides the MAUI application
         * lifecycle method.
         *
         *****************************************************************************/
        protected override async void OnStart()
        {
            base.OnStart();

            await Database.InitializeAsync();
        }

        #endregion
    }
}