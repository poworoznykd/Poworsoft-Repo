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
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace CollectIQ.ViewModels
{
    /// <summary>
    /// View model backing the Ebay search page.
    /// </summary>
    public class EbaySearchViewModel : INotifyPropertyChanged
    {
        #region Private Constants

        /// <summary>
        /// Maximum amount of rows rendered.
        /// Keep this low while testing Android performance.
        /// </summary>
        private const int MaxResultsToDisplay = 15;

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
        /// Gets a value indicating whether the page is currently performing an eBay search
        /// or building the result deck.
        /// </summary>
        public bool IsBusy => IsSearching || IsLoadingResults;

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

        /// <summary>
        /// Performs the image search using the captured image path.
        /// </summary>
        /// <param name="imagePath">The path to the taken image.</param>
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
                    await ClearResultsAsync("Image file was not found.");
                    return "Image file was not found.";
                }

                IsSearching = true;

                StatusText = IsSoldMode()
                    ? "Preparing original image for eBay visual search..."
                    : "Preparing original image for eBay visual search...";

                // Give the UI one frame to show the spinner/status text before file I/O starts.
                await Task.Delay(100);

                FileInfo fileInfo = new FileInfo(imagePath);
                Debug.WriteLine($"[eBay IMAGE] Source path: {imagePath}");
                Debug.WriteLine($"[eBay IMAGE] Source bytes: {fileInfo.Length}");

                byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
                lastImageBase64 = Convert.ToBase64String(imageBytes);

                Debug.WriteLine($"[eBay IMAGE] Base64 chars: {lastImageBase64.Length}");

                StatusText = IsSoldMode()
                    ? "Identifying card from image, then checking sold comps..."
                    : "Searching eBay by image...";

                List<EbayListing> results;

                if (IsSoldMode())
                {
                    results = await SearchSoldCompsFromImageAsync(daysRangeFilter);
                }
                else
                {
                    results = await ebayService.SearchByImageAsync(
                        lastImageBase64,
                        limit: Math.Max(averageCountFilter, MaxResultsToDisplay),
                        listingTypeFilter: "active",
                        daysRange: daysRangeFilter);
                }

                if (results == null || results.Count == 0)
                {
                    await ClearResultsAsync("No image matches found.");

                    return IsSoldMode()
                        ? "eBay did not return image matches or sold comps for this photo."
                        : "eBay did not return image matches for this photo.";
                }

                await ApplyResultsAsync(results);

                await UpdateStatusForResults(results);

                return string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[eBay IMAGE SEARCH ERROR] {ex}");
                await ClearResultsAsync("Image search failed.");
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

                    // Allows Android to draw/respond between batches without turning one search into a long wait.
                    await Task.Delay(50);
                }

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    StatusText = $"Loaded {Listings.Count} results.";
                });
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

        private async Task<List<EbayListing>> SearchSoldCompsFromImageAsync(int lookbackDays)
        {
            List<EbayListing> soldResults = new List<EbayListing>();

            StatusText = "Identifying the card from the photo...";

            List<EbayListing> identified = await ebayService.SearchByImageAsync(
                lastImageBase64,
                limit: 5,
                listingTypeFilter: "active",
                daysRange: lookbackDays);

            if (identified == null || identified.Count == 0)
            {
                Debug.WriteLine("[eBay IMAGE] Visual image search returned zero identified listings.");
                return soldResults;
            }

            string? bestTitle = identified
                .Where(r =>
                    !string.IsNullOrWhiteSpace(r.Title) &&
                    !r.Title.Contains("your pick", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Title!.Trim())
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(bestTitle))
            {
                Debug.WriteLine("[eBay IMAGE] Could not get a usable title from visual matches. Returning visual matches.");
                return identified;
            }

            StatusText = $"Checking sold comps for: {bestTitle}";
            Debug.WriteLine($"[eBay IMAGE] Best visual match title: {bestTitle}");

            try
            {
                List<EbayListing> soldForTitle = await EbayService.SearchSoldAsync(
                    bestTitle,
                    limit: Math.Max(averageCountFilter * 3, 30),
                    daysRange: lookbackDays);

                if (soldForTitle != null &&
                    soldForTitle.Count > 0)
                {
                    soldResults.AddRange(soldForTitle);
                }
            }
            catch (Exception soldEx)
            {
                Debug.WriteLine($"[eBay IMAGE] Sold search failed for '{bestTitle}': {soldEx.Message}");
            }

            if (soldResults.Count == 0)
            {
                StatusText = "No sold comps found. Showing visual image matches instead.";
                soldResults.AddRange(identified);
            }

            return soldResults;
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