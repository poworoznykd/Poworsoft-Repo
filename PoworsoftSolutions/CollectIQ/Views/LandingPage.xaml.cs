using System;
using Microsoft.Maui.Controls;

namespace CollectIQ.Views
{
    public partial class LandingPage : ContentPage
    {
        public LandingPage()
        {
            InitializeComponent();
        }

        private void OnEnter(object sender, EventArgs e)
        {
            // After we see the page working, you can switch to Shell here:
            Application.Current.MainPage = new AppShell();
        }
    }
}
