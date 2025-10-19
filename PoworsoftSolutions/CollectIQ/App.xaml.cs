//
//  FILE            : App.xaml.cs
//  PROJECT         : CollectIQ
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-21
//  DESCRIPTION     :
//      Entry point for the CollectIQ application.
//      Loads LandingPage first, then transitions to AuthSheet.
//
using CollectIQ.Views;

namespace CollectIQ
{
    public partial class App : Application
    {
        public App(LandingPage landingPage)
        {
            InitializeComponent();

            // Use a NavigationPage wrapper so we can navigate cleanly.
            MainPage = new NavigationPage(landingPage)
            {
                BarBackgroundColor = Colors.Transparent,
                BarTextColor = Colors.White
            };
        }
    }
}
