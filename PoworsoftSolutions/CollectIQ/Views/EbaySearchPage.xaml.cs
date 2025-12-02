//
//  FILE            : EbaySearchPage.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-28
//  UPDATED         : 2025-11-23
//  DESCRIPTION     :
//      Displays eBay search results for a scanned or manually
//      entered card query. Supports search-by-image, manual search,
//      swipe actions for "View on eBay" and "Add to Collection",
//      a futuristic bottom-sheet filter panel, and a floating
//      sold-comps overlay (with a simple price graph) that appears
//      when a result is swiped open.
//

using CollectIQ.Controls;
using CollectIQ.Models;
using CollectIQ.Services;
using CollectIQ.Utilities;
using FreakyKit.Utils;
using Microsoft.Maui.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CollectIQ.Views
{
    /// <summary>
    /// Interaction logic for the eBay search results page.
    /// </summary>
    [QueryProperty(nameof(FrontImagePath), "frontPath")]
    public partial class EbaySearchPage : ContentPage
    {
        /// <summary>
        /// Represents the type of search that last ran.
        /// </summary>
        private enum SearchMode
        {
            None,
            Image,
            Text
        }

        /// <summary>
        /// Holds the listing that the current Insights overlay is anchored to.
        /// This is used when recalculating insights (for example, after the
        /// user removes comps from the Insights list).
        /// </summary>
        private EbayListing? currentInsightAnchor;



        private readonly EbayService ebayService;
        private readonly ObservableCollection<EbayListing> listings;
        // Insights (sold comps) list
        private readonly ObservableCollection<EbayListing> insightsListings;

        private EbayListing? selectedListing;
        private bool isSwipeInProgress;
        private SearchMode lastSearchMode;

        private string listingTypeFilter;
        private int daysRangeFilter;
        private int averageCountFilter;

        private string lastImageBase64;
        private string lastManualQuery;
        private string frontImagePathInternal;

        /// <summary>
        /// Gets or sets the path to the scanned front image passed in via Shell.
        /// </summary>
        public string FrontImagePath
        {
            get => frontImagePathInternal;
            set => frontImagePathInternal = Uri.UnescapeDataString(value ?? string.Empty);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EbaySearchPage"/> class.
        /// </summary>
        public EbaySearchPage()
        {
            InitializeComponent();

            ebayService = new EbayService(new HttpClient());
            listings = new ObservableCollection<EbayListing>();
            insightsListings = new ObservableCollection<EbayListing>();

            EbayResultsView.ItemsSource = listings;
            // Default filter: sold, last 90 days, top 10 comps
            listingTypeFilter = "sold";
            daysRangeFilter = 90;
            averageCountFilter = 10;

            lastSearchMode = SearchMode.None;
            lastImageBase64 = string.Empty;
            lastManualQuery = string.Empty;
            frontImagePathInternal = string.Empty;

            InitializeFilterPickers();
            UpdateFilterSummaryLabel();
        }

        private async void OnInsightsIconTapped(object sender, TappedEventArgs e)
        {
            if (e?.Parameter is not EbayListing listing)
            {
                return;
            }

            // Use whatever the CollectionView is currently showing as the comps set.
            var items = EbayResultsView?.ItemsSource as IEnumerable<EbayListing>;
            var comps = items?.ToList() ?? new List<EbayListing>();

            // Reuse your existing filters so the overlay text matches.
            string type = string.Equals(listingTypeFilter, "sold", StringComparison.OrdinalIgnoreCase)
                ? "sold"
                : "active";

            int days = daysRangeFilter <= 0 ? 90 : daysRangeFilter;

            await InsightsOverlayControl.ShowAsync(listing, comps, type, days);
            InsightsOverlayControl.OnEstimatedValueReady = (value) =>
            {
                if (value.HasValue)
                {
                    selectedListing.Price = value.Value;
                    selectedListing.EstimatedValue = value.Value;
                }
            };

        }

        #region Filter Initialization

        /// <summary>
        /// Populates the filter pickers with default options.
        /// </summary>
        private void InitializeFilterPickers()
        {
            // Listing type options
            ListingTypePicker.Items.Clear();
            ListingTypePicker.Items.Add("Sold (last sold)");
            ListingTypePicker.Items.Add("Active (for sale)");
            ListingTypePicker.SelectedIndex = 0;

            // Days range options (used only for sold)
            DaysRangePicker.Items.Clear();
            DaysRangePicker.Items.Add("30");
            DaysRangePicker.Items.Add("90");
            DaysRangePicker.Items.Add("180");
            DaysRangePicker.Items.Add("365");
            DaysRangePicker.SelectedIndex = 1; // 90 by default

            // Average count (top N results)
            AverageCountPicker.Items.Clear();
            AverageCountPicker.Items.Add("5");
            AverageCountPicker.Items.Add("10");
            AverageCountPicker.Items.Add("20");
            AverageCountPicker.Items.Add("50");
            AverageCountPicker.SelectedIndex = 1; // 10 by default
        }

        /// <summary>
        /// Updates the small filter summary label under the manual search controls.
        /// </summary>
        private void UpdateFilterSummaryLabel()
        {
            string typeLabel = string.Equals(listingTypeFilter, "sold", StringComparison.OrdinalIgnoreCase)
                ? "Sold"
                : "Active";

            if (string.Equals(listingTypeFilter, "sold", StringComparison.OrdinalIgnoreCase))
            {
                FilterSummaryLabel.Text =
                    $"Filters: {typeLabel}, last {daysRangeFilter} days, avg top {averageCountFilter}";
            }
            else
            {
                FilterSummaryLabel.Text =
                    $"Filters: {typeLabel}, avg top {averageCountFilter}";
            }
        }

        #endregion

        #region Search Helpers

        /// <summary>
        /// Enables or disables the search spinner and sets status text.
        /// </summary>
        private void SetSearchingState(bool isSearching, string statusText = "")
        {
            SearchActivityIndicator.IsVisible = isSearching;
            SearchActivityIndicator.IsRunning = isSearching;

            if (!string.IsNullOrEmpty(statusText))
            {
                StatusLabel.Text = statusText;
            }
        }

        /// <summary>
        /// Calculates the average price from the current results based on
        /// the configured top N averageCountFilter.
        /// </summary>
        private decimal CalculateAveragePrice(List<EbayListing> results)
        {
            if (results == null || results.Count == 0)
            {
                return 0m;
            }

            var pricedItems = results
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
        /// Updates the StatusLabel after a search using the given results.
        /// </summary>
        private void UpdateStatusForResults(List<EbayListing> results)
        {
            if (results == null || results.Count == 0)
            {
                StatusLabel.Text = "No results found.";
                return;
            }

            decimal averagePrice = CalculateAveragePrice(results);

            string typeLabel = string.Equals(listingTypeFilter, "sold", StringComparison.OrdinalIgnoreCase)
                ? "Sold comps"
                : "Active listings";

            if (averagePrice <= 0m)
            {
                StatusLabel.Text =
                    $"{typeLabel}: {results.Count} results (no valid prices for average).";
            }
            else
            {
                StatusLabel.Text =
                    $"{typeLabel}: {results.Count} results, avg top {averageCountFilter}: {averagePrice:C2}";
            }
        }

        /// <summary>
        /// Applies the new results to the observable collection.
        /// </summary>
        private void ApplyResultsToCollection(List<EbayListing> results)
        {
            listings.Clear();

            if (results == null)
            {
                return;
            }

            foreach (EbayListing listing in results)
            {
                listings.Add(listing);
            }
        }

        #endregion

        #region Image Search

        /// <summary>
        /// Performs a search-by-image using the front card image path.
        /// For "Sold" mode, we:
        ///   1) Identify the card by image (active listings),
        ///   2) Use the top titles to fetch sold comps by text.
        /// </summary>
        private async Task PerformImageSearchAsync(string imagePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                {
                    return;
                }

                int lookbackDays = daysRangeFilter <= 0 ? 90 : daysRangeFilter;
                if (lookbackDays > 90)
                {
                    lookbackDays = 90;
                }

                byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
                lastImageBase64 = Convert.ToBase64String(imageBytes);
                lastSearchMode = SearchMode.Image;

                var results = new List<EbayListing>();

                if (string.Equals(listingTypeFilter, "sold", StringComparison.OrdinalIgnoreCase))
                {
                    SetSearchingState(true, "Identifying card and retrieving sold comps...");

                    // Step 1: identify card using active listings via image search.
                    var identified = await ebayService.SearchByImageAsync(
                        lastImageBase64,
                        limit: 5,
                        listingTypeFilter: "active",
                        daysRange: lookbackDays);

                    if (identified == null || identified.Count == 0)
                    {
                        await DisplayAlert(
                            "No matches",
                            "Could not identify this card from the image.",
                            "OK");
                    }
                    else
                    {
                        // Step 2: take top distinct titles and search sold comps by text.
                        var topTitles = identified
                            .Where(r => !string.IsNullOrWhiteSpace(r.Title) && !r.Title.Contains("your pick"))
                            .Select(r => r.Title!)
                            .Distinct()
                            .Take(3)
                            .ToList();

                        foreach (string title in topTitles)
                        {
                            var soldForTitle = await EbayService.SearchSoldAsync(
                                title,
                                limit: Math.Max(averageCountFilter * 3, 30),
                                daysRange: lookbackDays);

                            if (soldForTitle != null && soldForTitle.Count > 0)
                            {
                                results.AddRange(soldForTitle);
                            }
                        }

                        if (results.Count == 0)
                        {
                            await DisplayAlert(
                                "No sold comps",
                                $"No sold results found in the last {lookbackDays} days for the identified card title(s). Showing active listings instead.",
                                "OK");

                            listingTypeFilter = "active";
                            UpdateFilterSummaryLabel();
                            results = identified;
                        }
                    }
                }
                else
                {
                    SetSearchingState(true, "Identifying card and retrieving listings...");

                    results = await ebayService.SearchByImageAsync(
                        lastImageBase64,
                        limit: Math.Max(averageCountFilter, 25),
                        listingTypeFilter: listingTypeFilter,
                        daysRange: lookbackDays);
                }

                ApplyResultsToCollection(results);
                UpdateStatusForResults(results);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Image search failed: {ex.Message}", "OK");
            }
            finally
            {
                SetSearchingState(false);
            }
        }

        #endregion

        #region Manual Search

        /// <summary>
        /// Performs a manual text-based search using the configured filters.
        /// </summary>
        private async Task PerformManualSearchAsync(string query
        )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    return;
                }

                lastManualQuery = query.Trim();
                lastSearchMode = SearchMode.Text;

                SetSearchingState(true, $"Searching eBay for: {lastManualQuery}");

                var results = await ebayService.SearchListingsAsync(
                    lastManualQuery,
                    limit: Math.Max(averageCountFilter, 25),
                    listingTypeFilter: listingTypeFilter,
                    daysRange: daysRangeFilter);

                ApplyResultsToCollection(results);
                UpdateStatusForResults(results);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"eBay search failed: {ex.Message}", "OK");
            }
            finally
            {
                SetSearchingState(false);
            }
        }

        #endregion

        #region Event Handlers - Manual Search and Filters

        /// <summary>
        /// Handles the manual SEARCH button click.
        /// </summary>
        private async void OnManualSearchClicked(object sender, EventArgs e)
        {
            string query = ManualSearchBox?.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(query))
            {
                await DisplayAlert("Missing Input", "Enter text to search eBay.", "OK");
                return;
            }

            await PerformManualSearchAsync(query);
        }

        /// <summary>
        /// Opens the CardPage for adding a new card manually.
        /// </summary>
        private async void Add_Manual_Button_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new CardPage(new Card()));
        }

        /// <summary>
        /// Displays the bottom-sheet filter overlay.
        /// </summary>
        private void OnFilterButtonClicked(object sender, EventArgs e)
        {
            FilterOverlay.IsVisible = true;
        }

        #region Begin Searching
        /// <summary>
        /// On navigation to this page, if a front image path was provided,
        /// automatically perform a search-by-image using the current filters.
        /// </summary>
        /// <param name="args">Navigation arguments.</param>
        protected override void OnNavigatedTo(NavigatedToEventArgs args)
        {
            base.OnNavigatedTo(args);

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                // Give Shell enough time to apply query params the first time
                await Task.Delay(50);

                if (!string.IsNullOrWhiteSpace(FrontImagePath) &&
                    File.Exists(FrontImagePath))
                {
                    await PerformImageSearchAsync(FrontImagePath);
                }
            });
        }

        /// <summary>
        /// Applies selected filters and re-runs the last search.
        /// </summary>
        private async void OnApplyFiltersClicked(object sender, EventArgs e)
        {
            try
            {
                // Listing type
                if (ListingTypePicker.SelectedIndex == 0)
                {
                    listingTypeFilter = "sold";
                }
                else
                {
                    listingTypeFilter = "active";
                }

                // Days range (only really used for sold)
                int selectedDays = daysRangeFilter;
                if (DaysRangePicker.SelectedItem is string daysText &&
                    int.TryParse(daysText, out int parsedDays))
                {
                    selectedDays = parsedDays;
                }

                daysRangeFilter = selectedDays;

                // Average count (top N)
                int selectedAverageCount = averageCountFilter;
                if (AverageCountPicker.SelectedItem is string avgText &&
                    int.TryParse(avgText, out int parsedAvg))
                {
                    selectedAverageCount = Math.Max(1, parsedAvg);
                }

                averageCountFilter = selectedAverageCount;

                UpdateFilterSummaryLabel();
                FilterOverlay.IsVisible = false;

                // Re-run the last search with updated filters
                if (lastSearchMode == SearchMode.Image && !string.IsNullOrEmpty(lastImageBase64))
                {
                    SetSearchingState(true, "Updating image search with new filters...");

                    // Reuse existing base64 rather than re-reading from disk
                    var results = await ebayService.SearchByImageAsync(
                        lastImageBase64,
                        limit: Math.Max(averageCountFilter, 25),
                        listingTypeFilter: listingTypeFilter,
                        daysRange: daysRangeFilter);
                    
                    ApplyResultsToCollection(results);
                    UpdateStatusForResults(results);
                }
                else if (lastSearchMode == SearchMode.Text && !string.IsNullOrEmpty(lastManualQuery))
                {
                    await PerformManualSearchAsync(lastManualQuery);
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to apply filters: {ex.Message}", "OK");
            }
            finally
            {
                SetSearchingState(false);
            }
        }

        #endregion

        /// <summary>
        /// Hides the filter overlay without applying changes.
        /// </summary>
        private void OnCancelFiltersClicked(object sender, EventArgs e)
        {
            FilterOverlay.IsVisible = false;
        }

        #endregion

        #region Event Handlers - Result Selection and Swipes

        private async Task AnimateInsightsIconAsync(View icon)
        {
            try
            {
                await icon.ScaleTo(1.15, 120, Easing.CubicOut);
                await icon.ScaleTo(1.0, 120, Easing.CubicIn);
            }
            catch { /* ignore */ }
        }

        private void OnItemTapped(object sender, TappedEventArgs e)
        {
            if (e.Parameter is not EbayListing tapped)
                return;

            foreach (var item in listings)
                item.IsSelected = false;

            tapped.IsSelected = true;
            selectedListing = tapped;

            ManualSearchBox.Text = tapped.Title;
        }

        /// <summary>
        /// Swipe start handler – track that a swipe is in progress and show comps for this item.
        /// </summary>
        private async void SwipeView_SwipeStarted(object sender, SwipeStartedEventArgs e)
        {
            isSwipeInProgress = true;

            if (sender is SwipeView swipe &&
                swipe.BindingContext is EbayListing listing)
            {
                selectedListing = listing;
            }
        }

        /// <summary>
        /// Swipe end handler to clear the swipe-in-progress flag.
        /// </summary>
        private void SwipeView_SwipeEnded(object sender, SwipeEndedEventArgs e)
        {
            isSwipeInProgress = false;
        }

        /// <summary>
        /// Handles the "Add" swipe action and adds the selected card
        /// to the user's collection.
        /// </summary>
        private async void OnAddToCollectionSwipe(object sender, EventArgs e)
        {
            try
            {
                EbayListing? listing = null;

                if (sender is SwipeItem swipeItem &&
                    swipeItem.CommandParameter is EbayListing paramListing)
                {
                    listing = paramListing;
                }
                else if (selectedListing != null)
                {
                    listing = selectedListing;
                }

                if (listing == null)
                {
                    return;
                }

                Card card = CardMetadataParser.Parse(listing);
                card.EstimatedValue = listing.EstimatedValue;

                await App.Database.AddCardAsync(card);
                await DisplayAlert("Added", $"{listing.Title} added to your collection.", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Could not add card: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Handles the "Ebay" swipe action and opens the listing in the browser.
        /// </summary>
        private async void OnViewOnEbaySwipe(object sender, EventArgs e)
        {
            try
            {
                EbayListing? listing = null;

                if (sender is SwipeItem swipeItem &&
                    swipeItem.CommandParameter is EbayListing paramListing)
                {
                    listing = paramListing;
                }
                else if (selectedListing != null)
                {
                    listing = selectedListing;
                }

                if (listing == null || string.IsNullOrWhiteSpace(listing.Url))
                {
                    return;
                }

                await Browser.Default.OpenAsync(listing.Url, BrowserLaunchMode.SystemPreferred);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Unable to open eBay: {ex.Message}", "OK");
            }
        }

        #endregion

    }
}
