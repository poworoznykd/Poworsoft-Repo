//
//  FILE            : EbaySearchPage.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-28
//  UPDATED         : 2025-11-23
//  DESCRIPTION     :
//      Displays eBay search results for a scanned or manually entered
//      card query. The search/list/status state is handled by the
//      EbaySearchViewModel. The code-behind only handles view-specific
//      work such as navigation, alerts, browser launches, and overlay UI.
//

using CollectIQ.Models;
using CollectIQ.Models.SportsCardsPro;
using CollectIQ.Services;
using CollectIQ.Utilities;
using CollectIQ.ViewModels;
using Microsoft.Maui.Controls;
using System;
using System.Collections.Generic;
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
        #region Private Members

        private readonly EbaySearchViewModel viewModel;
        private readonly SportsCardsProService sportsCardsProService;
        private EbayListing? selectedListing;
        private string frontImagePathInternal;

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets or sets the path to the scanned front image passed in through Shell.
        /// </summary>
        public string FrontImagePath
        {
            get => frontImagePathInternal;
            set => frontImagePathInternal = Uri.UnescapeDataString(value ?? string.Empty);
        }

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the EbaySearchPage class.
        /// </summary>
        public EbaySearchPage()
        {
            InitializeComponent();

            viewModel = new EbaySearchViewModel(new EbayService(new HttpClient()));
            sportsCardsProService = new SportsCardsProService(new HttpClient());
            BindingContext = viewModel;

            selectedListing = null;
            frontImagePathInternal = string.Empty;
        }

        #endregion

        #region Navigation / Startup Search

        /// <summary>
        /// Automatically performs an image search when this page receives a front image path.
        /// </summary>
        /// <param name="args">Navigation arguments.</param>
        protected override void OnNavigatedTo(NavigatedToEventArgs args)
        {
            base.OnNavigatedTo(args);

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (!string.IsNullOrWhiteSpace(FrontImagePath) &&
                    File.Exists(FrontImagePath))
                {
                    string message = await viewModel.PerformImageSearchAsync(FrontImagePath);

                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        await DisplayAlert("No matches", message, "OK");
                    }
                }
            });
        }

        #endregion

        #region Manual Search and Navigation

        private async void OnSearchGradeSelectorTapped(object sender, TappedEventArgs e)
        {
            string[] labels = viewModel.GradeOptions.Select(option => option.Label).ToArray();
            string? selection = await DisplayActionSheet(
                "Search Grade / Condition",
                "Cancel",
                null,
                labels);

            if (string.IsNullOrWhiteSpace(selection) || selection == "Cancel")
                return;

            SportsCardsProGradeOption? option = viewModel.GradeOptions
                .FirstOrDefault(item => string.Equals(item.Label, selection, StringComparison.Ordinal));
            if (option != null)
                viewModel.SelectedGrade = option;
        }

        /// <summary>
        /// Handles the manual eBay search button click.
        /// </summary>
        private async void OnManualSearchClicked(object sender, EventArgs e)
        {
            string query = ManualSearchBox?.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(query))
            {
                await DisplayAlert("Missing Input", "Enter text to search eBay.", "OK");
                return;
            }

            string message = await viewModel.PerformManualSearchAsync(query);

            if (!string.IsNullOrWhiteSpace(message))
            {
                await DisplayAlert("Error", message, "OK");
            }
        }

        /// <summary>
        /// Opens the CardPage for adding a new card manually.
        /// </summary>
        private async void Add_Manual_Button_Clicked(object sender, EventArgs e)
        {
            Card card = new Card();
            (viewModel.SelectedGrade ?? SportsCardsProGradeCatalog.Ungraded).ApplyToCard(card);
            await Navigation.PushAsync(new CardPage(card));
        }

        #endregion

        #region Result Selection and Swipe Actions

        /// <summary>
        /// Handles row taps from the result list.
        /// </summary>
        private void OnItemTapped(object sender, TappedEventArgs e)
        {
            if (e.Parameter is not EbayListing listing)
            {
                return;
            }

            SelectListing(listing);
            ManualSearchBox.Text = listing.Title;
        }

        /// <summary>
        /// Handles the old SwipeView start event if another template still calls it.
        /// Kept intentionally for compatibility with existing XAML references.
        /// </summary>
        private void SwipeView_SwipeStarted(object sender, SwipeStartedEventArgs e)
        {
            if (sender is SwipeView swipe &&
                swipe.BindingContext is EbayListing listing)
            {
                SelectListing(listing);
            }
        }

        /// <summary>
        /// Handles the old SwipeView end event if another template still calls it.
        /// Kept intentionally for compatibility with existing XAML references.
        /// </summary>
        private void SwipeView_SwipeEnded(object sender, SwipeEndedEventArgs e)
        {
            // No state is required for the ListView context action implementation.
        }

        /// <summary>
        /// Handles the Add swipe/context action and adds the listing to the collection.
        /// </summary>
        private async void OnAddToCollectionSwipe(object sender, EventArgs e)
        {
            try
            {
                EbayListing? listing = GetListingFromActionSender(sender);

                if (listing == null)
                {
                    return;
                }

                SelectListing(listing);

                Card card = CardMetadataParser.Parse(listing);
                SportsCardsProGradeOption selectedGrade = viewModel.SelectedGrade ?? SportsCardsProGradeCatalog.Ungraded;
                selectedGrade.ApplyToCard(card);
                card.FrontImagePath = FrontImagePath;

                // Prefer the SportsCardsPro value for the exact grade the user selected.
                // If that grade has no published price, keep the eBay estimate instead.
                decimal? sportsCardsProPrice = null;
                try
                {
                    sportsCardsProPrice = await sportsCardsProService.GetBestMatchPriceForGradeAsync(
                        listing.Title ?? string.Empty,
                        selectedGrade);
                }
                catch (Exception priceEx)
                {
                    Console.WriteLine($"[EbaySearchPage] SportsCardsPro grade price lookup failed: {priceEx.Message}");
                }

                decimal resolvedValue = sportsCardsProPrice ?? listing.EstimatedValue ?? listing.Price ?? 0.00m;
                card.Insights.SuggestedPrice = resolvedValue;
                card.EstimatedValue = resolvedValue;

                await App.Database.AddCardAsync(card);

                string priceText = sportsCardsProPrice.HasValue
                    ? $" SportsCardsPro {selectedGrade.Label} value: {sportsCardsProPrice.Value:C2} USD."
                    : string.Empty;

                await DisplayAlert(
                    "Added",
                    $"{listing.Title} added to your collection as {selectedGrade.Label}.{priceText}",
                    "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Could not add card: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Handles the eBay swipe/context action and opens the listing in the browser.
        /// </summary>
        private async void OnViewOnEbaySwipe(object sender, EventArgs e)
        {
            try
            {
                EbayListing? listing = GetListingFromActionSender(sender);

                if (listing == null || string.IsNullOrWhiteSpace(listing.Url))
                {
                    return;
                }

                SelectListing(listing);

                await Browser.Default.OpenAsync(
                    listing.Url,
                    BrowserLaunchMode.SystemPreferred);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Unable to open eBay: {ex.Message}", "OK");
            }
        }

        #endregion

        #region Insights Overlay

        /// <summary>
        /// Opens the insights overlay for the selected listing.
        /// </summary>
        private async void OnInsightsIconTapped(object sender, TappedEventArgs e)
        {
            if (e?.Parameter is not EbayListing listing)
            {
                return;
            }

            SelectListing(listing);

            List<EbayListing> comps = viewModel.GetCurrentListings();

            string type = string.Equals(
                viewModel.ListingTypeFilter,
                "sold",
                StringComparison.OrdinalIgnoreCase)
                ? "sold"
                : "active";

            int days = viewModel.DaysRangeFilter <= 0 ? 90 : viewModel.DaysRangeFilter;

            if (InsightsOverlayControl == null)
            {
                return;
            }

            InsightsOverlayControl.OnEstimatedValueReady = async value =>
            {
                if (!value.HasValue)
                {
                    return;
                }

                if (selectedListing != null)
                {
                    selectedListing.Price = value.Value;
                    selectedListing.EstimatedValue =
                        InsightsOverlayControl?.InsightsData?.SuggestedPrice ?? 0.00m;

                    viewModel.RefreshSelectedListing();
                }

                await InsightsOverlayControl.HideAsync();
            };

            await InsightsOverlayControl.ShowAsync(listing, comps, type, days);
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Selects the supplied listing in the ViewModel and keeps a local reference
        /// for view-only operations such as adding or opening from context actions.
        /// </summary>
        /// <param name="listing">Listing to select.</param>
        private void SelectListing(EbayListing listing)
        {
            selectedListing = listing;
            viewModel.SelectListing(listing);
        }

        /// <summary>
        /// Extracts the listing from either a ListView context action or the older SwipeView action.
        /// </summary>
        /// <param name="sender">Action sender.</param>
        /// <returns>The listing associated with the action, or null.</returns>
        private EbayListing? GetListingFromActionSender(object sender)
        {
            if (sender is MenuItem menuItem &&
                menuItem.CommandParameter is EbayListing menuListing)
            {
                return menuListing;
            }

            if (sender is SwipeItem swipeItem &&
                swipeItem.CommandParameter is EbayListing swipeListing)
            {
                return swipeListing;
            }

            return selectedListing;
        }

        #endregion
    }
}
