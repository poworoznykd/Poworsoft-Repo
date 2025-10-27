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
        public App()
        {
            InitializeComponent();
            MainPage = new AppShell();
        }

    }
}
