//
//  FILE            : EbaySearchViewModel.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-12-06
//  DESCRIPTION     :
//      View model for the EbaySearchPage.
//
//      This class owns:
//
//      - eBay search execution
//      - search state
//      - status text
//      - selected listing
//      - result collection
//
//      PERFORMANCE OPTIMIZATIONS:
//
//      1. Heavy result preparation happens off the UI thread.
//      2. The UI collection is created once and updated in small batches.
//      3. MainThread updates are batched.
//      4. Selection does not refresh the entire list.
//      5. Results are capped for Android performance.
//

using CollectIQ.Models;
using CollectIQ.Services;
using Microsoft.Maui.ApplicationModel;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Image = SixLabors.ImageSharp.Image;

namespace CollectIQ.ViewModels
{
    /// <summary>
    /// View model backing the Ebay search page.
    /// </summary>
    public class EbaySearchViewModel : INotifyPropertyChanged
    {
        #region Private Constants


        /// <summary>
        /// Maximum amount of time allowed for preparing an image before falling back to the original image.
        /// </summary>
        private const int ImagePreparationTimeoutMilliseconds = 8000;

        /// <summary>
        /// Maximum size allowed for fallback raw image upload.
        /// </summary>
        private const long MaxRawFallbackImageBytes = 5 * 1024 * 1024;

        /// <summary>
        /// Maximum amount of rows rendered.
        /// Keep this low while testing Android performance.
        /// </summary>
        private const int MaxResultsToDisplay = 15;

        /// <summary>
        /// Maximum width or height sent to eBay image search.
        /// </summary>
        private const int MaxEbayImageDimension = 1200;

        /// <summary>
        /// JPEG quality used for image-search payloads.
        /// </summary>
        private const int EbayImageJpegQuality = 82;

        #endregion

        #region Private Members

        private readonly EbayService ebayService;

        private EbayListing? selectedListing;
        private string statusText;
        private bool isSearching;

        private string listingTypeFilter;
        private int daysRangeFilter;
        private int averageCountFilter;
        private string lastImageBase64;
        private string lastManualQuery;

        #endregion

        #region Public Events

        public event PropertyChangedEventHandler? PropertyChanged;

        #endregion

        #region Constructor

        public EbaySearchViewModel(EbayService ebayServiceInstance)
        {
            ebayService = ebayServiceInstance ?? throw new ArgumentNullException(nameof(ebayServiceInstance));

            Listings = new ObservableCollection<EbayListing>();

            statusText = "READY";
            isSearching = false;
            listingTypeFilter = "sold";
            daysRangeFilter = 90;
            averageCountFilter = 10;
            lastImageBase64 = string.Empty;
            lastManualQuery = string.Empty;
        }

        #endregion

        #region Public Properties

        private bool isLoadingResults;

        public bool IsLoadingResults
        {
            get => isLoadingResults;

            private set
            {
                if (isLoadingResults != value)
                {
                    isLoadingResults = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsBusy));
                }
            }
        }

        /// <summary>
        /// Main result collection bound to the UI.
        /// This collection is created once and then cleared/filled.
        /// Do not assign a new collection after construction.
        /// </summary>
        public ObservableCollection<EbayListing> Listings
        {
            get;
        }

        public EbayListing? SelectedListing
        {
            get => selectedListing;

            private set
            {
                if (!ReferenceEquals(selectedListing, value))
                {
                    selectedListing = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StatusText
        {
            get => statusText;

            set
            {
                if (!string.Equals(statusText, value, StringComparison.Ordinal))
                {
                    statusText = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSearching
        {
            get => isSearching;

            private set
            {
                if (isSearching != value)
                {
                    isSearching = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsBusy));
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the search page is actively doing work.
        /// This remains true while eBay is being called and while the result deck is being built.
        /// </summary>
        public bool IsBusy => IsSearching || IsLoadingResults;

        public string ListingTypeFilter
        {
            get => listingTypeFilter;

            set
            {
                listingTypeFilter = string.IsNullOrWhiteSpace(value) ? "sold" : value;
            }
        }

        public int DaysRangeFilter
        {
            get => daysRangeFilter;

            set
            {
                daysRangeFilter = value <= 0 ? 90 : value;
            }
        }

        public int AverageCountFilter
        {
            get => averageCountFilter;

            set
            {
                averageCountFilter = value <= 0 ? 10 : value;
            }
        }

        public string LastManualQuery => lastManualQuery;

        #endregion

        #region Public Search Methods

        public async Task<string> PerformManualSearchAsync(string query)
        {
            if (IsSearching)
            {
                return string.Empty;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    return "Enter text to search eBay.";
                }

                lastManualQuery = query.Trim();

                IsSearching = true;
                StatusText = $"Searching eBay for: {lastManualQuery}";

                //// Give the UI one frame to show the spinner/status text.
                //await Task.Delay(100);
               
                List<EbayListing> results = await ebayService.SearchListingsAsync(
                    lastManualQuery,
                    limit: Math.Max(averageCountFilter, MaxResultsToDisplay),
                    listingTypeFilter: listingTypeFilter,
                    daysRange: daysRangeFilter);

                if (results == null || results.Count == 0)
                {
                    await ClearResultsAsync("No results found.");
                    return string.Empty;
                }

                await ApplyResultsAsync(results);

                await UpdateStatusForResults(results);

                return string.Empty;
            }
            catch (Exception ex)
            {
                await ClearResultsAsync("Search failed.");
                return $"eBay search failed: {ex.Message}";
            }
            finally
            {
                IsSearching = false;
            }
        }

        public async Task<string> PerformImageSearchAsync(string imagePath)
        {
            if (IsSearching)
            {
                return string.Empty;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(imagePath) ||
                    !File.Exists(imagePath))
                {
                    return string.Empty;
                }

                int lookbackDays = daysRangeFilter <= 0 ? 90 : daysRangeFilter;

                if (lookbackDays > 90)
                {
                    lookbackDays = 90;
                }

                IsSearching = true;

                StatusText = IsSoldMode()
                    ? "Identifying card and retrieving sold comps..."
                    : "Identifying card and retrieving listings...";

                // Give the UI one frame to show the spinner/status text.
                await Task.Delay(100);

                lastImageBase64 = await CreateEbayReadyImageBase64Async(imagePath);

                if (string.IsNullOrWhiteSpace(lastImageBase64))
                {
                    await ClearResultsAsync("Image could not be prepared for eBay search.");
                    return "Image could not be prepared for eBay search.";
                }

                List<EbayListing> results;

                if (IsSoldMode())
                {
                    results = await SearchSoldCompsFromImageAsync(lookbackDays);
                }
                else
                {
                    results = await ebayService.SearchByImageAsync(
                        lastImageBase64,
                        limit: Math.Max(averageCountFilter, MaxResultsToDisplay),
                        listingTypeFilter: listingTypeFilter,
                        daysRange: lookbackDays);
                }

                if (results == null || results.Count == 0)
                {
                    await ClearResultsAsync("No results found.");

                    return IsSoldMode()
                        ? "Could not identify this card or find sold comps."
                        : "Could not identify this card from the image.";
                }

                await ApplyResultsAsync(results);

                UpdateStatusForResults(results);

                return string.Empty;
            }
            catch (Exception ex)
            {
                await ClearResultsAsync("Search failed.");
                return $"Image search failed: {ex.Message}";
            }
            finally
            {
                IsSearching = false;
            }
        }

        #endregion

        #region Public Collection Methods

        /// <summary>
        /// Applies results to the UI in small batches.
        /// This keeps Android responsive while rows are being created.
        /// </summary>
        /// <param name="results">Results returned from eBay.</param>
        public async Task ApplyResultsAsync(List<EbayListing> results)
        {
            IsLoadingResults = true;

            try
            {
                List<EbayListing> preparedResults = await Task.Run(() =>
                {
                    if (results == null)
                    {
                        return new List<EbayListing>();
                    }

                    return results
                        .Where(r => r != null)
                        .Take(MaxResultsToDisplay)
                        .Select(listing =>
                        {
                            listing.IsSelected = false;

                            if (!listing.EstimatedValue.HasValue)
                            {
                                listing.EstimatedValue = listing.Price;
                            }

                            return listing;
                        })
                        .ToList();
                });

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    SelectedListing = null;
                    Listings.Clear();
                    StatusText = $"Preparing {preparedResults.Count} results...";
                });

                const int BatchSize = 5;

                for (int i = 0; i < preparedResults.Count; i += BatchSize)
                {
                    List<EbayListing> batch = preparedResults
                        .Skip(i)
                        .Take(BatchSize)
                        .ToList();

                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        foreach (EbayListing listing in batch)
                        {
                            Listings.Add(listing);
                        }

                        StatusText =
                            $"Loading result {Listings.Count} of {preparedResults.Count}...";
                    });

                    // Allows Android to draw/respond between each row.
                    await Task.Delay(25);
                }

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    StatusText = $"Finalizing {Listings.Count} result cards...";
                });

                await Task.Delay(25);

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    StatusText = $"Loaded {Listings.Count} results. Cards may finish rendering as you scroll.";
                });

                await Task.Delay(25);
            }
            finally
            {
                IsLoadingResults = false;
            }
        }

        /// <summary>
        /// Clears all current results.
        /// IMPORTANT: Listings is read-only, so we clear it instead of replacing it.
        /// </summary>
        public async Task ClearResultsAsync(string newStatusText = "READY")
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Listings.Clear();
                SelectedListing = null;
                StatusText = newStatusText;
            });
        }

        public void SelectListing(EbayListing? listing)
        {
            if (listing == null)
            {
                return;
            }

            if (SelectedListing != null &&
                !ReferenceEquals(SelectedListing, listing))
            {
                SelectedListing.IsSelected = false;
            }

            listing.IsSelected = true;
            SelectedListing = listing;
        }

        public List<EbayListing> GetCurrentListings()
        {
            return Listings.ToList();
        }

        public void RefreshSelectedListing()
        {
            OnPropertyChanged(nameof(SelectedListing));
        }

        #endregion

        #region Private Search Helpers

        /// <summary>
        /// Creates a base64 image payload suitable for the eBay image-search endpoint.
        /// </summary>
        /// <param name="imagePath">The local image path captured by the camera workflow.</param>
        /// <returns>A base64 encoded image.</returns>
        private static async Task<string> CreateEbayReadyImageBase64Async(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) ||
                !File.Exists(imagePath))
            {
                Debug.WriteLine("[eBay IMAGE PREP] Image path is empty or file does not exist.");
                return string.Empty;
            }

            try
            {
                FileInfo fileInfo = new FileInfo(imagePath);

                Debug.WriteLine($"[eBay IMAGE PREP] Source path: {imagePath}");
                Debug.WriteLine($"[eBay IMAGE PREP] Source bytes: {fileInfo.Length}");

                Task<string> resizeTask = Task.Run(() => CreateResizedImageBase64(imagePath));
                Task timeoutTask = Task.Delay(ImagePreparationTimeoutMilliseconds);

                Task completedTask = await Task.WhenAny(resizeTask, timeoutTask);

                if (completedTask == resizeTask)
                {
                    string resizedBase64 = await resizeTask;

                    if (!string.IsNullOrWhiteSpace(resizedBase64))
                    {
                        Debug.WriteLine($"[eBay IMAGE PREP] Resized base64 chars: {resizedBase64.Length}");
                        return resizedBase64;
                    }
                }

                Debug.WriteLine("[eBay IMAGE PREP] Resize timed out or returned empty. Falling back to original image.");

                if (fileInfo.Length > MaxRawFallbackImageBytes)
                {
                    Debug.WriteLine("[eBay IMAGE PREP] Original image is too large for fallback upload.");
                    return string.Empty;
                }

                byte[] originalBytes = await File.ReadAllBytesAsync(imagePath);
                Debug.WriteLine($"[eBay IMAGE PREP] Fallback original bytes: {originalBytes.Length}");

                return Convert.ToBase64String(originalBytes);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[eBay IMAGE PREP ERROR] {ex}");

                try
                {
                    FileInfo fileInfo = new FileInfo(imagePath);

                    if (fileInfo.Length <= MaxRawFallbackImageBytes)
                    {
                        byte[] originalBytes = await File.ReadAllBytesAsync(imagePath);
                        Debug.WriteLine($"[eBay IMAGE PREP] Exception fallback original bytes: {originalBytes.Length}");

                        return Convert.ToBase64String(originalBytes);
                    }
                }
                catch (Exception fallbackEx)
                {
                    Debug.WriteLine($"[eBay IMAGE PREP FALLBACK ERROR] {fallbackEx}");
                }

                return string.Empty;
            }
        }

        /// <summary>
        /// Creates a resized JPEG image using ImageSharp.
        /// </summary>
        /// <param name="imagePath">The local image path.</param>
        /// <returns>A base64 encoded resized JPEG image.</returns>
        private static string CreateResizedImageBase64(string imagePath)
        {
            using SixLabors.ImageSharp.Image image = SixLabors.ImageSharp.Image.Load(imagePath);

            int maxSide = Math.Max(image.Width, image.Height);

            Debug.WriteLine($"[eBay IMAGE PREP] Loaded image dimensions: {image.Width}x{image.Height}");

            if (maxSide > MaxEbayImageDimension)
            {
                double scale = (double)MaxEbayImageDimension / maxSide;
                int newWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
                int newHeight = Math.Max(1, (int)Math.Round(image.Height * scale));

                Debug.WriteLine($"[eBay IMAGE PREP] Resizing to: {newWidth}x{newHeight}");

                image.Mutate(context => context.Resize(newWidth, newHeight));
            }

            using MemoryStream memoryStream = new MemoryStream();

            JpegEncoder encoder = new JpegEncoder
            {
                Quality = EbayImageJpegQuality
            };

            image.SaveAsJpeg(memoryStream, encoder);

            byte[] imageBytes = memoryStream.ToArray();

            Debug.WriteLine($"[eBay IMAGE PREP] Prepared JPEG bytes: {imageBytes.Length}");

            return Convert.ToBase64String(imageBytes);
        }

        private async Task<List<EbayListing>> SearchSoldCompsFromImageAsync(int lookbackDays)
        {
            var localResults = new List<EbayListing>();

            List<EbayListing> identified = await ebayService.SearchByImageAsync(
                lastImageBase64,
                limit: 5,
                listingTypeFilter: "active",
                daysRange: lookbackDays);

            if (identified == null || identified.Count == 0)
            {
                return localResults;
            }

            List<string> topTitles = identified
                .Where(r =>
                    !string.IsNullOrWhiteSpace(r.Title) &&
                    !r.Title.Contains("your pick", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Title!)
                .Distinct()
                .Take(3)
                .ToList();

            foreach (string title in topTitles)
            {
                try
                {
                    List<EbayListing> soldForTitle = await EbayService.SearchSoldAsync(
                        title,
                        limit: Math.Max(averageCountFilter * 3, 30),
                        daysRange: lookbackDays);

                    if (soldForTitle != null &&
                        soldForTitle.Count > 0)
                    {
                        localResults.AddRange(soldForTitle);
                    }
                }
                catch (Exception soldEx)
                {
                    Debug.WriteLine($"[eBay] Sold search failed for '{title}': {soldEx.Message}");
                }
            }

            if (localResults.Count == 0)
            {
                localResults.AddRange(identified);
            }

            return localResults;
        }

        private bool IsSoldMode()
        {
            return string.Equals(
                listingTypeFilter,
                "sold",
                StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region Private Status Helpers

        private decimal CalculateAveragePrice(List<EbayListing> results)
        {
            if (results == null || results.Count == 0)
            {
                return 0m;
            }

            List<EbayListing> pricedItems = results
                .Where(r =>
                    r.Price.HasValue &&
                    r.Price.Value > 0m)
                .OrderBy(r => r.Price!.Value)
                .Take(averageCountFilter)
                .ToList();

            if (pricedItems.Count == 0)
            {
                return 0m;
            }

            return pricedItems.Average(r => r.Price!.Value);
        }

        public async Task UpdateStatusForResults(List<EbayListing> results)
        {
            if (results == null || results.Count == 0)
            {
                StatusText = "No results found.";
                return;
            }

            decimal averagePrice = CalculateAveragePrice(results);

            string typeLabel = IsSoldMode()
                ? "Sold comps"
                : "Active listings";

            if (averagePrice <= 0m)
            {
                StatusText = $"{typeLabel}: {results.Count} results (no valid prices for average).";
            }
            else
            {
                StatusText = $"{typeLabel}: {results.Count} results, avg top {averageCountFilter}: {averagePrice:C2}";
            }
        }

        #endregion

        #region Notify Property Changed

        private void OnPropertyChanged(
            [CallerMemberName] string? propertyName = null)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return;
            }

            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}