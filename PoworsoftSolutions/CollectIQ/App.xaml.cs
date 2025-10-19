using CollectIQ.Interfaces;

namespace CollectIQ
{
    public partial class App : Application
    {
        /// <summary>
        /// App ctor – initialize the DB (fire-and-forget) and set start page.
        /// </summary>
        public App(IDatabase db)
        {
            InitializeComponent();
            _ = db.InitializeAsync(); // fire-and-forget
            MainPage = new Views.LandingPage();
        }

    }
}
