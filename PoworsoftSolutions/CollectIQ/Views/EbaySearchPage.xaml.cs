using CollectIQ.Models;
using CollectIQ.Services;
using CollectIQ.Utilities;
using Microsoft.Maui.Controls;
using Plugin.Maui.OCR;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

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
        public string FrontPath { get => _frontPath; set => _frontPath = value; }

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

        public EbaySearchPage(IOcrService ocrService)
        {
            _ocrService = ocrService;
            InitializeComponent();
            BindingContext = this;
        }

        private async Task<string?> RecognizeTextFromImageAsync(string imagePath)
        {
            try
            {
                if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                    return null;

                byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
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

                // --- NEW: Keep only the first 6–8 relevant words to avoid flooding eBay
                var trimmed = text.Replace("\r", " ").Replace("\n", " ");
                var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string reduced = string.Join(' ', words.Take(8));

                // --- NEW: Sanitize but keep digits and key names like Topps/Chrome
                string sanitized = System.Text.RegularExpressions.Regex
                    .Replace(reduced, @"[^a-zA-Z0-9\s]", "")
                    .Trim();

                await PerformSearchAsync(sanitized);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"OCR or search failed: {ex.Message}", "OK");
            }
        }



        // ============================================================
        // UPDATED: Show all results, not just one
        // ============================================================
        private async Task PerformSearchAsync(string query)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                    return;

                // --- ensure spaces not double-escaped
                string cleaned = query.Trim();
                Listings.Clear();

                var results = await _ebayService.SearchListingsAsync(cleaned, 10);
                if (results != null && results.Count > 0)
                {
                    foreach (var item in results)
                        Listings.Add(item);
                }
                else
                {
                    await DisplayAlert("No Results", "No eBay matches found.", "OK");
                }

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

        private async void OnAddToCollectionSwipe(object sender, EventArgs e)
        {
            if (sender is SwipeItem swipeItem && swipeItem.BindingContext is EbayListing listing)
            {
                try
                {
                    // Create a pre-populated Card object for editing
                    var prefilled = new Card
                    {
                        Name = listing.Title,
                        PhotoPath = listing.ImageUrl,
                        EstimatedValue = listing.Price,
                        Set = "eBay Import",
                        GradeCompany = "Raw"
                    };

                    // Navigate to CardPage for confirmation / edit
                    await Navigation.PushAsync(new CardPage(prefilled));
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Navigation Error", ex.Message, "OK");
                }
            }
        }


        private async void OnViewOnEbaySwipe(object sender, EventArgs e)
        {
            if (sender is SwipeItem swipeItem && swipeItem.BindingContext is EbayListing listing && !string.IsNullOrEmpty(listing.Url))
            {
                await Browser.Default.OpenAsync(listing.Url, BrowserLaunchMode.SystemPreferred);
            }
        }

        private async void Add_Manual_Button_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new CardPage());
        }
    }
}
