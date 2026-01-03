using Microsoft.Maui.Controls;
using System;

namespace CollectIQ.Views
{
    public partial class ProfilePage : ContentPage
    {
        public ProfilePage()
        {
            InitializeComponent();
        }

        private async void OnBackClicked(object sender, EventArgs e)
        {
            if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("..");
                return;
            }

            await Navigation.PopAsync();
        }

        private async void OnSettingsClicked(object sender, EventArgs e)
        {
            await DisplayAlert("Profile", "Settings coming soon.", "OK");
        }
    }
}
