/*
 * FILE         : DashboardPage.xaml.cs
 * PROJECT      : CollectIQ (Mobile Application)
 * PROGRAMMER   : Darryl Poworoznyk
 * FIRST VERSION: 2025-10-29
 * UPDATED      : 2025-12-04
 * DESCRIPTION  :
 *   Dashboard page code-behind. Wires the view to the
 *   DashboardViewModel and forwards lifecycle events.
 */

using System;
using CollectIQ.Interfaces;
using CollectIQ.Services;
using CollectIQ.ViewModels;
using CollectIQ.Helpers;
using Microsoft.Maui.Controls;

namespace CollectIQ.Views
{
    /// <summary>
    /// Dashboard page displaying high-level collection metrics
    /// and curated shortcuts.
    /// </summary>
    public partial class DashboardPage : ContentPage
    {
        private readonly DashboardViewModel viewModel;

        /// <summary>
        /// Default constructor used by Shell and XAML.
        /// Resolves required services via ServiceHelper and
        /// constructs the DashboardViewModel.
        /// </summary>
        public DashboardPage()
        {
            InitializeComponent();

            IDatabase? database = ServiceHelper.GetService<IDatabase>();
            IBrowserService? browserService = ServiceHelper.GetService<IBrowserService>();
            IAlertService? alertService = ServiceHelper.GetService<IAlertService>();

            if (database == null)
            {
                throw new InvalidOperationException("IDatabase is not registered in the service container.");
            }

            // Fallbacks are defensive; under normal conditions these
            // will be resolved from DI as well.
            browserService ??= new BrowserService();
            alertService ??= new AlertService();

            viewModel = new DashboardViewModel(database, browserService, alertService);
            BindingContext = viewModel;
        }

        /// <summary>
        /// Ensures the view model loads dashboard statistics when the page appears.
        /// </summary>
        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await viewModel.InitializeAsync();
        }
    }
}
