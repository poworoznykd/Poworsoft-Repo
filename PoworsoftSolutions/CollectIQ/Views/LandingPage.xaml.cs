//
//  FILE            : LandingPage.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-21
//  DESCRIPTION     :
//      Provides the entry UI for users, offering guest mode access or
//      navigation to authentication (sign-in/register) screens.
//
using CollectIQ.Interfaces;
using Microsoft.Maui.Controls;

namespace CollectIQ.Views
{
    public partial class LandingPage : ContentPage
    {
        private readonly IAuthService _authService;

        /// <summary>
        /// Initializes the LandingPage with injected authentication service.
        /// </summary>
        public LandingPage(IAuthService authService)
        {
            InitializeComponent();
            _authService = authService;
        }

        /// <summary>
        /// Handles the Guest access button.
        /// Navigates directly to the main AppShell.
        /// </summary>
        private async void OnGuest(object sender, EventArgs e)
        {
            try
            {
                await DisplayAlert("Guest Mode",
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
        /// Opens the AuthSheet for sign-in/registration.
        /// </summary>
        private async void OnAuth(object sender, EventArgs e)
        {
            try
            {
                // Navigate cleanly to AuthSheet
                await Navigation.PushAsync(new AuthSheet(_authService));
            }
            catch (Exception ex)
            {
                await DisplayAlert("Navigation Error", $"Unable to open Auth page: {ex.Message}", "OK");
            }
        }
    }
}
