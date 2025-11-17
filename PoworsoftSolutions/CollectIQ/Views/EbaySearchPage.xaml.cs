/* 
* FILE            : EbaySearchPage.xaml.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2025-11-03
* DESCRIPTION     :
*      Displays eBay search results for sports cards.
*      Supports:
*          - Manual text-based search using the Browse API.
*          - Image-based search using eBay's search_by_image endpoint
*            when a front card image path is passed from ScanPage.
*      Allows the user to view items on eBay and add matches to the
*      local SQLite-backed collection.
*/

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
        // ====================================================================
        // Fields
        // ====================================================================

        /// <summary>
        /// The currently selected eBay listing from the results list.
        /// </summary>
        private EbayListing? selectedEbayListing;

        /// <summary>
        /// Indicates whether a swipe gesture is in progress so that 
        /// selection taps do not double-fire.
        /// </summary>
        private bool isSwipeInProgress = false;

        /// <summary>
        /// Service responsible for calling the eBay Browse API.
        /// </summary>
        private readonly EbayService ebayService = new(new HttpClient());

        /// <summary>
        /// Collection of listings displayed in the results view.
        /// </summary>
        public ObservableCollection<EbayListing> Listings { get; } = new();

        /// <summary>
        /// OCR service used to process text from the back of the card image.
        /// </summary>
        private readonly IOcrService ocrService;

        /// <summary>
        /// Local backing field for the front image path passed from ScanPage.
        /// </summary>
        private string frontImagePath = string.Empty;

        /// <summary>
        /// Local backing field for the back image path passed from ScanPage.
        /// </summary>
        private string backImagePath = string.Empty;

        // ====================================================================
        // Properties (used by Shell query navigation)
        // ====================================================================

        /// <summary>
        /// Gets or sets the path to the front card image.
        /// When set via Shell navigation (frontPath), this will automatically
        /// trigger an image-based search against eBay's search_by_image API.
        /// </summary>
        public string FrontPath
        {
            get => frontImagePath;
            set
            {
                frontImagePath = value;

                if (!string.IsNullOrWhiteSpace(value))
                {
                    // Fire-and-forget image search when the card front path is supplied.
                    _ = PerformImageSearchAsync(value);
                }
            }
        }

        /// <summary>
        /// Gets or sets the path to the back card image.
        /// When set via Shell navigation (backPath), this triggers OCR, sanitizes
        /// the text for eBay, and performs a text-based search.
        /// </summary>
        public string BackPath
        {
            get => backImagePath;
            set
            {
                backImagePath = value;

                if (!string.IsNullOrEmpty(value))
                {
                    // Fire-and-forget OCR + text search on the back image.
                    _ = ProcessBackImageAsync(value);
                }
            }
        }

        // ====================================================================
        // Constructor
        // ====================================================================

        /// <summary>
        /// Initializes a new instance of the <see cref="EbaySearchPage"/> class.
        /// </summary>
        /// <param name="ocrServiceParameter">
        /// The OCR service used to recognize text from the back of the card.
        /// </param>
        public EbaySearchPage(IOcrService ocrServiceParameter)
        {
            ocrService = ocrServiceParameter;

            InitializeComponent();
            BindingContext = this;
        }

        // ====================================================================
        // OCR Helpers (Back-Of-Card Text Search)
        // ====================================================================

        /// <summary>
        /// Performs OCR on the provided image path and returns detected text.
        /// </summary>
        /// <param name="imagePath">The full path to the card image file.</param>
        /// <returns>
        /// The recognized text from the image, or null if OCR fails or the
        /// file cannot be read.
        /// </returns>
        private async Task<string?> RecognizeTextFromImageAsync(string imagePath)
        {
            try
            {
                if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                {
                    return null;
                }

                byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
                var ocrResult = await ocrService.RecognizeTextAsync(imageBytes);

                Debug.WriteLine($"[OCR] Detected Text: {ocrResult.AllText}");

                return ocrResult.AllText.Trim();
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[OCR ERROR]: {exception.Message}");
                await DisplayAlert("OCR Error", exception.Message, "OK");
                return null;
            }
        }

        /// <summary>
        /// Uses the back image to perform OCR, sanitizes the text for eBay,
        /// updates the manual search box, and performs a text-based search.
        /// </summary>
        /// <param name="imagePath">The full path to the back card image.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task ProcessBackImageAsync(string imagePath)
        {
            try
            {
                string? rawText = await RecognizeTextFromImageAsync(imagePath);
                string sanitizedText = await OCRUtility.SanitizeForEbay(rawText) ?? string.Empty;

                ManualSearchBox.Text = sanitizedText;

                if (string.IsNullOrWhiteSpace(sanitizedText))
                {
                    await DisplayAlert("No Text Found", "Could not extract text from the back image.", "OK");
                    return;
                }

                StatusLabel.Text = $"Searching eBay for: {sanitizedText}";
                await PerformSearchAsync(sanitizedText);
            }
            catch (Exception exception)
            {
                await DisplayAlert("Error", $"OCR or search failed: {exception.Message}", "OK");
            }
        }

        // ====================================================================
        // Image-Based Search (Front-Of-Card)
        // ====================================================================

        /// <summary>
        /// Executes an image-based search using eBay's search_by_image endpoint.
        /// The front card image is loaded, converted to Base64, and sent to the
        /// Browse API via <see cref="EbayService.SearchByImageAsync(string, int)"/>.
        /// </summary>
        /// <param name="imagePath">The full path to the front card image.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task PerformImageSearchAsync(string imagePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                {
                    await DisplayAlert("Image Error", "Front image not found on device.", "OK");
                    return;
                }

                // Read image bytes and convert to Base64 as required by eBay.
                byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
                string base64Image = Convert.ToBase64String(imageBytes);

                StatusLabel.Text = "Searching eBay by image...";
                Listings.Clear();

                var searchResults = await ebayService.SearchByImageAsync(base64Image, 10);

                if (searchResults != null && searchResults.Count > 0)
                {
                    foreach (EbayListing ebayListing in searchResults)
                    {
                        Listings.Add(ebayListing);
                    }
                }
                else
                {
                    await DisplayAlert("No Results", "No eBay matches found for this image.", "OK");
                }

                EbayResultsView.ItemsSource = Listings;
            }
            catch (Exception exception)
            {
                await DisplayAlert("Error", $"eBay image search failed: {exception.Message}", "OK");
            }
        }

        // ====================================================================
        // Text-Based Search (Manual or OCR-Derived)
        // ====================================================================

        /// <summary>
        /// Performs a text-based search using the eBay Browse API's search endpoint.
        /// </summary>
        /// <param name="query">The text query to send to eBay.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task PerformSearchAsync(string query)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    return;
                }

                string cleanedQuery = query.Trim();
                StatusLabel.Text = $"Searching eBay for: {cleanedQuery}";
                Listings.Clear();

                var searchResults = await ebayService.SearchListingsAsync(cleanedQuery, 10);

                if (searchResults != null && searchResults.Count > 0)
                {
                    foreach (EbayListing ebayListing in searchResults)
                    {
                        Listings.Add(ebayListing);
                    }
                }
                else
                {
                    await DisplayAlert("No Results", "No eBay matches found.", "OK");
                }

                EbayResultsView.ItemsSource = Listings;
            }
            catch (Exception exception)
            {
                await DisplayAlert("Error", $"eBay search failed: {exception.Message}", "OK");
            }
        }

        // ====================================================================
        // Event Handlers
        // ====================================================================

        /// <summary>
        /// Handles the click event for the manual search button.
        /// Uses the text in the manual search box as the query.
        /// </summary>
        /// <param name="sender">The source of the click event.</param>
        /// <param name="e">The event data.</param>
        private async void OnManualSearchClicked(object sender, EventArgs e)
        {
            string query = ManualSearchBox?.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(query))
            {
                await DisplayAlert("Missing Input", "Enter text to search eBay.", "OK");
                return;
            }

            await PerformSearchAsync(query);
        }

        /// <summary>
        /// Handles result selection from the eBay results list.
        /// If no swipe operation is in progress, opens the item in the system browser.
        /// </summary>
        /// <param name="sender">The CollectionView raising the event.</param>
        /// <param name="e">The selection changed event data.</param>
        private async void OnResultSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.Count == 0)
            {
                return;
            }

            if (e.CurrentSelection[0] is EbayListing listing &&
                !string.IsNullOrEmpty(listing.Url))
            {
                selectedEbayListing = listing;

                if (!isSwipeInProgress)
                {
                    await Browser.Default.OpenAsync(listing.Url, BrowserLaunchMode.SystemPreferred);
                }
            }

            // Clear selection so the same item can be tapped again.
            ((CollectionView)sender).SelectedItem = null;
        }

        /// <summary>
        /// Handles the "Add" swipe action to add the selected eBay listing
        /// to the local card collection.
        /// </summary>
        /// <param name="sender">The SwipeItem raising the event.</param>
        /// <param name="e">The event data.</param>
        private async void OnAddToCollectionSwipe(object sender, EventArgs e)
        {
            if (selectedEbayListing is EbayListing ebayListing)
            {
                try
                {
                    var card = new Card
                    {
                        Name = ebayListing.Title,
                        EstimatedValue = ebayListing.Price,
                        CollectionId = "Default",
                        FrontImagePath = ebayListing.ImageUrl,
                        BackImagePath = ebayListing.ImageUrl,
                        Set = "eBay Import",
                        GradeCompany = "Raw"
                    };

                    await App.Database.AddCardAsync(card);

                    await DisplayAlert("Added", $"{ebayListing.Title} added to your collection.", "OK");
                }
                catch (Exception exception)
                {
                    await DisplayAlert("Error", $"Could not add card: {exception.Message}", "OK");
                }
            }
        }

        /// <summary>
        /// Handles the "View on eBay" swipe action and opens the selected listing
        /// in the system browser.
        /// </summary>
        /// <param name="sender">The SwipeItem raising the event.</param>
        /// <param name="e">The event data.</param>
        private async void OnViewOnEbaySwipe(object sender, EventArgs e)
        {
            if (selectedEbayListing == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(selectedEbayListing.Url))
            {
                await Browser.Default.OpenAsync(selectedEbayListing.Url, BrowserLaunchMode.SystemPreferred);
            }
        }

        /// <summary>
        /// Navigates to the card entry page for manual card creation.
        /// </summary>
        /// <param name="sender">The button raising the event.</param>
        /// <param name="e">The event data.</param>
        private async void Add_Manual_Button_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new CardPage());
        }

        /// <summary>
        /// Marks that a swipe operation has started so that taps do not fire concurrently.
        /// </summary>
        /// <param name="sender">The SwipeView raising the event.</param>
        /// <param name="e">The event data.</param>
        private void SwipeView_SwipeStarted(object sender, SwipeStartedEventArgs e)
        {
            isSwipeInProgress = true;
        }

        /// <summary>
        /// Marks that a swipe operation has ended and re-enables tap actions.
        /// </summary>
        /// <param name="sender">The SwipeView raising the event.</param>
        /// <param name="e">The event data.</param>
        private void SwipeView_SwipeEnded(object sender, SwipeEndedEventArgs e)
        {
            isSwipeInProgress = false;
        }
    }
}
