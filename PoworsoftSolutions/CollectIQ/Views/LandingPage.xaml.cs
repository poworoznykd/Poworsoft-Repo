using System;
using CollectIQ.Helpers;
using CollectIQ.Interfaces;
using Microsoft.Maui.Controls;

namespace CollectIQ.Views
{
    /// <summary>
    /// Landing page with guest entry and Sign In / Register options.
    /// </summary>
    public partial class LandingPage : ContentPage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LandingPage"/> class.
        /// </summary>
        public LandingPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Enters the app as a guest user (no account).
        /// </summary>
        private void OnGuest(object sender, EventArgs e)
        {
            Application.Current.MainPage = new AppShell();
        }

        /// <summary>
        /// Opens the authentication sheet (Sign In / Register).
        /// </summary>
        private async void OnAuth(object sender, EventArgs e)
        {
            var auth = ServiceHelper.GetService<IAuthService>();
            if (auth is null)
            {
                await DisplayAlert("Error", "Auth service not available.", "OK");
                return;
            }

            await Navigation.PushModalAsync(new AuthSheet(auth));
        }
    }
}
