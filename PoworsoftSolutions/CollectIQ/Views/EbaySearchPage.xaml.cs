/*
* FILE: EbaySearchPage.xaml.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-25
* DESCRIPTION:
*     Handles logic for displaying and selecting eBay listings.
*     Navigates to the listing URL when a result is tapped.
*/

using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using CollectIQ.Models;
using CollectIQ.Services;

namespace CollectIQ.Views
{
    [QueryProperty(nameof(InitialQuery), "initialQuery")]
    public partial class EbaySearchPage : ContentPage
    {
        private readonly EbayService _ebayService = new(new HttpClient());
        public ObservableCollection<EbayListing> Listings { get; } = new();

        private string _initialQuery = string.Empty;
        public string InitialQuery
        {
            get => _initialQuery;
            set
            {
                _initialQuery = value;
                _ = PerformSearchAsync(value);
            }
        }

        public EbaySearchPage()
        {
            InitializeComponent();
            BindingContext = this;
        }

        /// <summary>
        /// Performs the eBay search (mock or real).
        /// </summary>
        private async Task PerformSearchAsync(string query)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                    return;

                var result = await _ebayService.GetBestMatchAsync(query);
                Listings.Clear();
                if (result != null)
                    Listings.Add(result);

                // ✅ This will now compile correctly
                EbayResultsView.ItemsSource = Listings;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"eBay search failed: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Opens the eBay listing in a browser when tapped.
        /// </summary>
        private async void OnResultSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.Count == 0)
                return;

            if (e.CurrentSelection[0] is EbayListing listing && !string.IsNullOrEmpty(listing.Url))
                await Browser.Default.OpenAsync(listing.Url, BrowserLaunchMode.SystemPreferred);

            ((CollectionView)sender).SelectedItem = null;
        }
    }
}
