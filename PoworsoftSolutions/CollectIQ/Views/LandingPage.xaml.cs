//
//  FILE            : LandingPage.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-21
//  UPDATED         : 2026-06-10
//  DESCRIPTION     :
//      Provides the entry UI for users, offering guest mode access or
//      navigation to authentication screens. The page always reuses the
//      injected authentication service so social authentication stays enabled.
//

using CollectIQ.Interfaces;
using Microsoft.Maui.Controls;

namespace CollectIQ.Views
{
    /// <summary>
    /// Landing page for guest or account access.
    /// </summary>
    public partial class LandingPage : ContentPage
    {
        private readonly IAuthService authService;

        /// <summary>
        /// Initializes a new instance of the LandingPage class.
        /// </summary>
        /// <param name="authService">The configured authentication service.</param>
        public LandingPage(IAuthService authService)
        {
            InitializeComponent();
            this.authService = authService;
        }

        /// <summary>
        /// Handles the Guest access button.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private async void OnGuest(object sender, EventArgs e)
        {
            try
            {
                bool ok = await this.authService.SignInGuestAsync();

                if (!ok)
                {
                    await DisplayAlert("Guest Mode", "Unable to start guest mode.", "OK");
                    return;
                }

                await DisplayAlert(
                    "Guest Mode",
                    "You are continuing as a guest. Some features may be limited.",
                    "OK");

                Application.Current!.MainPage = new AppShell();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Unable to proceed as guest: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Handles the navigation to authentication screen.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private async void OnAuth(object sender, EventArgs e)
        {
            try
            {
                await Navigation.PushAsync(new AuthSheet(this.authService));
            }
            catch (Exception ex)
            {
                await DisplayAlert("Navigation Error", $"Unable to open Auth page: {ex.Message}", "OK");
            }
        }
    }
}
