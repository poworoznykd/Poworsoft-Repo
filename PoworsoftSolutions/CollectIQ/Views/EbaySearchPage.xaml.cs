/*
* FILE: EbaySearchPage.xaml.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-28
* DESCRIPTION:
*     Displays eBay search results using text recognized from OCR
*     or manually entered by the user. Allows selection and saving
*     of a chosen listing into the local SQLite collection.
*/

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Plugin.Maui.OCR;
using CollectIQ.Models;
using CollectIQ.Services;

namespace CollectIQ.Views
{
    [QueryProperty(nameof(FrontPath), "frontPath")]
    [QueryProperty(nameof(BackPath), "backPath")]
    public partial class EbaySearchPage : ContentPage
    {
        private readonly EbayService _ebayService;
        private readonly SqliteDatabase _database = new();
        private readonly IOcrService _ocrService;

        public ObservableCollection<EbayListing> Listings { get; } = new();

        private string _frontPath;
        private string _backPath;

        public string FrontPath
        {
            get => _frontPath;
            set => _frontPath = value;
        }

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

        public EbaySearchPage(IOcrService ocrService)
        {
            _ocrService = ocrService;
            _ebayService = new EbayService(); // uses token already set inside EbayService.cs

            InitializeComponent();
            BindingContext = this;
        }

        // ============================================================
        // OCR PROCESSING
        // ============================================================

        private async Task<string?> RecognizeTextFromImageAsync(string imagePath)
        {
            try
            {
                if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                    return null;

                byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
                var result = await _ocrService.RecognizeTextAsync(imageBytes);

                Debug.WriteLine($"[OCR] Detected Text: {result.AllText}");
                return result.AllText?.Trim();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OCR ERROR]: {ex.Message}");
                await DisplayAlert("OCR Error", ex.Message, "OK");
                return null;
            }
        }

        private async Task ProcessBackImageAsync(string imagePath)
        {
            var text = await RecognizeTextFromImageAsync(imagePath);
            ManualSearchBox.Text = text;

            if (string.IsNullOrWhiteSpace(text))
            {
                await DisplayAlert("No Text Found", "Could not extract text from the back image.", "OK");
                return;
            }

            await PerformSearchAsync(text.Replace("\n", " ").Replace("\r", " "));
        }

        // ============================================================
        // EBAY SEARCH + DISPLAY
        // ============================================================

        private async Task PerformSearchAsync(string query)
        {
            try
            {
                Listings.Clear();
                var results = await _ebayService.SearchListingsAsync(query, 10);

                foreach (var item in results)
                    Listings.Add(item);

                EbayResultsView.ItemsSource = Listings;

                if (Listings.Count == 0)
                    await DisplayAlert("No Results", "No eBay matches found.", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Search failed: {ex.Message}", "OK");
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

            if (e.CurrentSelection[0] is EbayListing listing)
            {
                bool add = await DisplayAlert("Add to Collection?",
                    $"Add '{listing.Title}' to your collection?", "Yes", "No");

                if (add)
                {
                    var card = new Card
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = listing.Title,
                        EstimatedValue = listing.Price ?? 0,
                        PhotoPath = listing.ImageUrl
                    };

                    await _database.AddCardAsync(card);
                    await DisplayAlert("Saved", "Card added to your collection.", "OK");
                }
            }

            ((CollectionView)sender).SelectedItem = null;
        }
    }
}
