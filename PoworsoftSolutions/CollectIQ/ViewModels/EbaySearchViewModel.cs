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

                byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
                lastImageBase64 = Convert.ToBase64String(imageBytes);

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

                const int BatchSize = 1;

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
                    await Task.Delay(175);
                }

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    StatusText = $"Finalizing {Listings.Count} result cards...";
                });

                await Task.Delay(1500);

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    StatusText = $"Loaded {Listings.Count} results. Cards may finish rendering as you scroll.";
                });

                await Task.Delay(1000);
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