/*
 * FILE         : DashboardPage.xaml.cs
 * PROJECT      : CollectIQ (Mobile Application)
 * PROGRAMMER   : Darryl Poworoznyk
 * UPDATED      : 2026-01-18
 * DESCRIPTION  :
 *   Code-behind for DashboardPage.
 *   - Builds DashboardViewModel using DI
 *   - Ensures Dashboard binds to the SAME ProfileViewModel singleton used by ProfilePage
 *   - Avatar chip tap navigates to ProfilePage
 */

using System;
using CollectIQ.Helpers;
using CollectIQ.Interfaces;
using CollectIQ.ViewModels;
using Microsoft.Maui.Controls;

namespace CollectIQ.Views
{
    public partial class DashboardPage : ContentPage
    {
        private readonly DashboardViewModel viewModel;

        public DashboardPage()
        {
            InitializeComponent();

            IDatabase database = ServiceHelper.GetService<IDatabase>()
                ?? throw new InvalidOperationException("IDatabase is not registered in the service container.");

            IBrowserService browserService = ServiceHelper.GetService<IBrowserService>()
                ?? throw new InvalidOperationException("IBrowserService is not registered in the service container.");

            IAlertService alertService = ServiceHelper.GetService<IAlertService>()
                ?? throw new InvalidOperationException("IAlertService is not registered in the service container.");

            IProfileService profileService = ServiceHelper.GetService<IProfileService>()
                ?? throw new InvalidOperationException("IProfileService is not registered in the service container.");

            // CRITICAL: The VM now exposes Profile so XAML can bind to Profile.AvatarPath
            viewModel = new DashboardViewModel(database, browserService, alertService, profileService.Profile);
            BindingContext = viewModel;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await viewModel.InitializeAsync();
        }

        private async void OnProfileAvatarTapped(object sender, EventArgs e)
        {
            // Open ProfilePage (uses same singleton profile service)
            await Navigation.PushAsync(new ProfilePage());
        }
    }
}
