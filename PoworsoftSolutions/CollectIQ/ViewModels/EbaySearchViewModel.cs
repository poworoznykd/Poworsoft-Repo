//
//  FILE            : EbaySearchViewModel.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-12-06
//  DESCRIPTION     :
//      View model for the EbaySearchPage. Owns the eBay result list,
//      selected listing, status text, search state, and eBay search logic.
//      The view binds to this class instead of manually setting the list
//      source from the code-behind.
//

using CollectIQ.Models;
using CollectIQ.Services;
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
    /// View model backing the eBay search page.
    /// </summary>
    public class EbaySearchViewModel : INotifyPropertyChanged
    {
        #region Private Constants

        /// <summary>
        /// Maximum amount of rows rendered in the result list.
        /// This keeps Android scrolling responsive while still showing enough results.
        /// </summary>
        private const int MaxResultsToDisplay = 25;

        #endregion

        #region Private Members

        private readonly EbayService ebayService;

        private ObservableCollection<EbayListing> listings;
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

        /// <summary>
        /// Raised when a bindable property changes.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new instance of the eBay search view model.
        /// </summary>
        /// <param name="ebayServiceInstance">Service used for eBay searches.</param>
        public EbaySearchViewModel(EbayService ebayServiceInstance)
        {
            ebayService = ebayServiceInstance ?? throw new ArgumentNullException(nameof(ebayServiceInstance));

            listings = new ObservableCollection<EbayListing>();
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

        /// <summary>
        /// Results shown in the swipe list.
        /// The collection is replaced as one operation to avoid excessive UI refreshes.
        /// </summary>
        public ObservableCollection<EbayListing> Listings
        {
            get => listings;
            private set
            {
                if (!ReferenceEquals(listings, value))
                {
                    listings = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// The currently selected listing.
        /// </summary>
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

        /// <summary>
        /// Status text shown in the bottom ribbon.
        /// </summary>
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

        /// <summary>
        /// Indicates whether a search is currently running.
        /// </summary>
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

        /// <summary>
        /// The current listing type filter. Expected values are "sold" or "active".
        /// </summary>
        public string ListingTypeFilter
        {
            get => listingTypeFilter;
            set => listingTypeFilter = string.IsNullOrWhiteSpace(value) ? "sold" : value;
        }

        /// <summary>
        /// Number of days used for eBay lookback filtering.
        /// </summary>
        public int DaysRangeFilter
        {
            get => daysRangeFilter;
            set => daysRangeFilter = value <= 0 ? 90 : value;
        }

        /// <summary>
        /// Number of top priced records used for average calculation.
        /// </summary>
        public int AverageCountFilter
        {
            get => averageCountFilter;
            set => averageCountFilter = value <= 0 ? 10 : value;
        }

        /// <summary>
        /// Last manual query run from the search box.
        /// </summary>
        public string LastManualQuery => lastManualQuery;

        #endregion

        #region Public Methods

        /// <summary>
        /// Performs an image search from the supplied image path.
        /// </summary>
        /// <param name="imagePath">Path to the scanned card image.</param>
        /// <returns>A user-facing message when the page should show an alert; otherwise empty.</returns>
        public async Task<string> PerformImageSearchAsync(string imagePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                {
                    return string.Empty;
                }

                int lookbackDays = GetSafeLookbackDays();

                IsSearching = true;
                StatusText = IsSoldMode()
                    ? "Identifying card and retrieving sold comps..."
                    : "Identifying card and retrieving listings...";

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
                    ClearResults("No results found.");

                    return IsSoldMode()
                        ? "Could not identify this card or find sold comps."
                        : "Could not identify this card from the image.";
                }

                ApplyResults(results);
                UpdateStatusForResults(results);

                return string.Empty;
            }
            catch (Exception ex)
            {
                ClearResults("Search failed.");
                return $"Image search failed: {ex.Message}";
            }
            finally
            {
                IsSearching = false;
            }
        }

        /// <summary>
        /// Performs a manual text based search.
        /// </summary>
        /// <param name="query">Manual eBay search text.</param>
        /// <returns>A user-facing message when the page should show an alert; otherwise empty.</returns>
        public async Task<string> PerformManualSearchAsync(string query)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    return "Enter text to search eBay.";
                }

                lastManualQuery = query.Trim();

                IsSearching = true;
                StatusText = $"Searching eBay for: {lastManualQuery}";

                List<EbayListing> results = await ebayService.SearchListingsAsync(
                    lastManualQuery,
                    limit: Math.Max(averageCountFilter, MaxResultsToDisplay),
                    listingTypeFilter: listingTypeFilter,
                    daysRange: daysRangeFilter);

                if (results == null || results.Count == 0)
                {
                    ClearResults("No results found.");
                    return string.Empty;
                }

                ApplyResults(results);
                UpdateStatusForResults(results);

                return string.Empty;
            }
            catch (Exception ex)
            {
                ClearResults("Search failed.");
                return $"eBay search failed: {ex.Message}";
            }
            finally
            {
                IsSearching = false;
            }
        }

        /// <summary>
        /// Applies the supplied results to the list by replacing the whole collection.
        /// This is much cheaper than sending one UI change notification per item.
        /// </summary>
        /// <param name="results">Search results returned from eBay.</param>
        public void ApplyResults(List<EbayListing> results)
        {
            SelectedListing = null;

            if (results == null || results.Count == 0)
            {
                Listings = new ObservableCollection<EbayListing>();
                StatusText = "No results found.";
                return;
            }

            List<EbayListing> displayResults = results
                .Where(r => r != null)
                .Take(MaxResultsToDisplay)
                .ToList();

            foreach (EbayListing listing in displayResults)
            {
                listing.IsSelected = false;

                if (!listing.EstimatedValue.HasValue)
                {
                    listing.EstimatedValue = listing.Price;
                }
            }

            Listings = new ObservableCollection<EbayListing>(displayResults);
        }

        /// <summary>
        /// Clears all current results and resets the selected listing.
        /// </summary>
        /// <param name="newStatusText">Optional status text to show after clearing.</param>
        public void ClearResults(string newStatusText = "READY")
        {
            Listings = new ObservableCollection<EbayListing>();
            SelectedListing = null;
            StatusText = newStatusText;
        }

        /// <summary>
        /// Selects a listing without looping through the entire result list.
        /// </summary>
        /// <param name="listing">Listing selected by tap, swipe, or insights button.</param>
        public void SelectListing(EbayListing? listing)
        {
            if (listing == null)
            {
                return;
            }

            if (SelectedListing != null && !ReferenceEquals(SelectedListing, listing))
            {
                SelectedListing.IsSelected = false;
            }

            listing.IsSelected = true;
            SelectedListing = listing;
        }

        /// <summary>
        /// Gets a copy of the current listing collection for the insights overlay.
        /// </summary>
        /// <returns>Current listings as a list.</returns>
        public List<EbayListing> GetCurrentListings()
        {
            return Listings.ToList();
        }

        /// <summary>
        /// Refreshes the selected listing display after the insights overlay updates pricing.
        /// </summary>
        public void RefreshSelectedListing()
        {
            OnPropertyChanged(nameof(SelectedListing));
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Performs the sold-comps pipeline for image search.
        /// </summary>
        /// <param name="lookbackDays">Safe lookback days.</param>
        /// <returns>Sold comps, or active identified listings when sold comps are unavailable.</returns>
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
                .Where(r => !string.IsNullOrWhiteSpace(r.Title) &&
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

                    if (soldForTitle != null && soldForTitle.Count > 0)
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

        /// <summary>
        /// Calculates the average price from the current results based on
        /// the configured top N average count filter.
        /// </summary>
        /// <param name="results">Results to calculate average from.</param>
        /// <returns>Average price, or zero when no valid prices exist.</returns>
        private decimal CalculateAveragePrice(List<EbayListing> results)
        {
            if (results == null || results.Count == 0)
            {
                return 0m;
            }

            List<EbayListing> pricedItems = results
                .Where(r => r.Price.HasValue && r.Price.Value > 0m)
                .OrderBy(r => r.Price!.Value)
                .Take(averageCountFilter)
                .ToList();

            if (pricedItems.Count == 0)
            {
                return 0m;
            }

            return pricedItems.Average(r => r.Price!.Value);
        }

        /// <summary>
        /// Updates the status ribbon after a search using the supplied results.
        /// </summary>
        /// <param name="results">Results returned from eBay.</param>
        private void UpdateStatusForResults(List<EbayListing> results)
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

        /// <summary>
        /// Gets a safe eBay lookback value.
        /// </summary>
        /// <returns>Lookback days capped to 90.</returns>
        private int GetSafeLookbackDays()
        {
            int lookbackDays = daysRangeFilter <= 0 ? 90 : daysRangeFilter;

            if (lookbackDays > 90)
            {
                lookbackDays = 90;
            }

            return lookbackDays;
        }

        /// <summary>
        /// Determines whether the current filter mode is sold comps.
        /// </summary>
        /// <returns>True when sold mode is active.</returns>
        private bool IsSoldMode()
        {
            return string.Equals(listingTypeFilter, "sold", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Raises a property changed notification.
        /// </summary>
        /// <param name="propertyName">Name of the changed property.</param>
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
