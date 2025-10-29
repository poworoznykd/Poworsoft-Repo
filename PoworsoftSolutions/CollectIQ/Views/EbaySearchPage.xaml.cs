/*
* FILE: EbaySearchPage.xaml.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-18
* UPDATED: 2025-10-28
* DESCRIPTION:
*     Displays eBay search results based on OCR text extracted from
*     front and back card images or manual input.
*/

using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using CollectIQ.Models;
using CollectIQ.Services;
using CollectIQ.Utilities;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;

namespace CollectIQ.Views
{
    [QueryProperty(nameof(FrontPath), "frontPath")]
    [QueryProperty(nameof(BackPath), "backPath")]
    public partial class EbaySearchPage : ContentPage
    {
        private readonly EbayService _ebayService = new(new HttpClient());
        public ObservableCollection<EbayListing> Listings { get; } = new();

        private string _frontPath = string.Empty;
        public string FrontPath { get => _frontPath; set => _frontPath = value; }

        private string _backPath = string.Empty;
        public string BackPath
        {
            get => _backPath;
            set
            {
                _backPath = value;
                if (!string.IsNullOrEmpty(value))
                    _ = ProcessImagesAsync();
            }
        }

        public EbaySearchPage()
        {
            InitializeComponent();
            BindingContext = this;
        }

        private async Task ProcessImagesAsync()
        {
            try
            {
                string? text = await OCRUtility.ExtractTextFromFrontAndBackAsync(_frontPath, _backPath);
                string? sanitizedText = await OCRUtility.SanitizeForEbay(text ?? string.Empty);
                if (string.IsNullOrWhiteSpace(sanitizedText))
                {
                    await DisplayAlert("No Text Found", "OCR could not extract text.", "OK");
                    return;
                }

                ManualSearchBox.Text = sanitizedText;
                await PerformSearchAsync(sanitizedText);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EbaySearchPage] {ex.Message}");
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }

        private async Task PerformSearchAsync(string query)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query)) return;

                var result = await _ebayService.GetBestMatchAsync(query);
                Listings.Clear();

                if (result != null)
                    Listings.Add(result);
                else
                    await DisplayAlert("No Results", "No eBay matches found.", "OK");

                EbayResultsView.ItemsSource = Listings;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Search Error", ex.Message, "OK");
            }
        }

        private async void OnManualSearchClicked(object sender, EventArgs e)
        {
            string query = ManualSearchBox?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                await DisplayAlert("Missing Input", "Enter text to search eBay.", "OK");
                return;
            }

            await PerformSearchAsync(query);
        }

        private async void OnResultSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.Count == 0) return;

            if (e.CurrentSelection[0] is EbayListing listing && !string.IsNullOrEmpty(listing.Url))
                await Browser.Default.OpenAsync(listing.Url);

            ((CollectionView)sender).SelectedItem = null;
        }
    }
}
