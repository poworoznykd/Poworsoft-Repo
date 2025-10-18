using Microsoft.Maui.Controls;

namespace CollectIQ
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            // Force the app to start on LandingPage (no Shell yet)
            MainPage = new CollectIQ.Views.LandingPage();
            // If you prefer a nav bar later: MainPage = new NavigationPage(new CollectIQ.Views.LandingPage());
        }
    }
}
