/*
* FILE: EbaySearchPage.xaml.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-18
* DESCRIPTION:
*     Displays eBay search results based on OCR text extracted from
*     front and back card images or manual text input.
*     Uses Plugin.Maui.OCR cross-platform abstraction.
*/

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using CollectIQ.Models;
using CollectIQ.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using Plugin.Maui.OCR;

namespace CollectIQ.Views
{
    [QueryProperty(nameof(FrontPath), "frontPath")]
    [QueryProperty(nameof(BackPath), "backPath")]
    public partial class EbaySearchPage : ContentPage
    {
        private readonly EbayService _ebayService = new(new HttpClient());
        public ObservableCollection<EbayListing> Listings { get; } = new();

        private readonly IOcrService _ocrService;

        private string _frontPath = string.Empty;
        public string FrontPath
        {
            get => _frontPath;
            set => _frontPath = value;
        }

        private string _backPath = string.Empty;
        public string BackPath
        {
            get => _backPath;
            set
            {
                _backPath = value;
                if (!string.IsNullOrEmpty(value))
                    _ = ProcessBackImageAsync(value);
            }
        }

        // The OCR service is injected by MAUI via UseOcr()
        public EbaySearchPage(IOcrService ocrService)
        {
            _ocrService = ocrService;
            InitializeComponent();
            BindingContext = this;
        }

        // ============================================================
        // OCR via Plugin.Maui.OCR
        // ============================================================
        private async Task<string?> RecognizeTextFromImageAsync(string imagePath)
        {
            try
            {
                if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                    return null;

                byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);

                // Recognize text using the injected OCR service
                var result = await _ocrService.RecognizeTextAsync(imageBytes);

                Debug.WriteLine($"[OCR] Detected Text: {result.AllText}");
                return result.AllText.Trim();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OCR ERROR]: {ex.Message}");
                await DisplayAlert("OCR Error", ex.Message, "OK");
                return null;
            }
        }


        // ============================================================
        // OCR + eBay Search Integration
        // ============================================================
        private async Task ProcessBackImageAsync(string imagePath)
        {
            try
            {
                var text = await RecognizeTextFromImageAsync(imagePath);
                ManualSearchBox.Text = text;
                if (string.IsNullOrWhiteSpace(text))
                {
                    await DisplayAlert("No Text Found", "Could not extract text from the back image.", "OK");
                    return;
                }

                var cleaned = text.Replace("\n", " ").Replace("\r", " ");
                await PerformSearchAsync(cleaned);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"OCR or search failed: {ex.Message}", "OK");
            }
        }

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
                else
                    await DisplayAlert("No Results", "No eBay matches found.", "OK");

                EbayResultsView.ItemsSource = Listings;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"eBay search failed: {ex.Message}", "OK");
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
            if (e.CurrentSelection.Count == 0)
                return;

            if (e.CurrentSelection[0] is EbayListing listing && !string.IsNullOrEmpty(listing.Url))
                await Browser.Default.OpenAsync(listing.Url, BrowserLaunchMode.SystemPreferred);

            ((CollectionView)sender).SelectedItem = null;
        }
    }
}
