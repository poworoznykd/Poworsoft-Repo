//
//  FILE            : CardPage.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-11-xx
//  UPDATED         : 2025-12-13
//  DESCRIPTION     :
//      Detail / edit page for a single card. Shows front/back images,
//      editable fields, and live eBay-based pricing insights via the
//      shared InsightsOverlayControl. This version also wires up image
//      capture and gallery picking for front/back images, saving them
//      into the app's local storage. When "Take Photos" is used, it
//      reuses the ScanPage camera UI and returns results via
//      NavigationCache for a consistent look-and-feel.
//
//      2025-12-09:
//      - Added per-side camera and gallery handlers so that small icon
//        buttons under each image can capture or pick FRONT / BACK
//        independently using ScanPage (FrontOnly / BackOnly) or
//        FilePicker, respectively.
//
//      2025-12-13:
//      - Integrated Player / HighlightReel composition with Card.
//      - Added YouTube highlight search for `Player.Name career highlights`.
//      - Refactored save logic into SaveCardInternalAsync so highlights
//        can be persisted without re-entrancy issues.
//

using CollectIQ.Controls;
using CollectIQ.Models;
using CollectIQ.Models.SportsCardsPro;
using CollectIQ.Services;
using CollectIQ.Services.Session;
using CollectIQ.Utilities;
using CollectIQ.ViewModels;
using FreakyKit.Utils;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace CollectIQ.Views
{
    public partial class CardPage : ContentPage
    {
        private readonly CardPageViewModel viewModel;
        private readonly SqliteDatabase database = new();
        private readonly EbayService ebayService;

        private string frontPath;
        private string backPath;
        private bool isNewCard;
        private bool isBusy;

        /*
         * FUNCTION     : CardPage
         * DESCRIPTION  :
         *     Default constructor – delegates to the main constructor
         *     with a null card, allowing XAML preview and simple usage.
         * PARAMETERS   :
         *     none
         * RETURNS      :
         *     none
         */
        public CardPage()
            : this(null)
        {
        }

        /*
         * FUNCTION     : CardPage
         * DESCRIPTION  :
         *     Primary constructor which accepts an existing Card
         *     instance or null to create a new card. Initializes
         *     the view model, eBay service, and image paths.
         * PARAMETERS   :
         *     card - The card to edit, or null for a new card.
         * RETURNS      :
         *     none
         */
        public CardPage(Card card)
        {
            InitializeComponent();
            viewModel = new CardPageViewModel(card);
            ebayService = new EbayService(new HttpClient());
            BindingContext = viewModel;

            frontPath = viewModel.SelectedCard.FrontImagePath;
            backPath = viewModel.SelectedCard.BackImagePath;
        }

        /*
         * FUNCTION     : OnAppearing
         * DESCRIPTION  :
         *     When the page becomes visible, this function ensures
         *     that the front and back image previews reflect the
         *     current paths stored on the selected card.
         *     If we have just returned from ScanPage in "CardPage
         *     workflow" mode, it pulls the captured front/back image
         *     paths from NavigationCache and applies them first.
         * PARAMETERS   :
         *     sender - Event source
         *     e      - Event arguments
         * RETURNS      :
         *     void
         */
        private void OnAppearing(object sender, EventArgs e)
        {
            try
            {
                // First: did ScanPage send us new paths via NavigationCache?
                var navData = NavigationCache.Get<Dictionary<string, string?>>(nameof(CardPage));

                if (navData != null)
                {
                    if (navData.TryGetValue("FrontPath", out var scannedFront) &&
                        !string.IsNullOrWhiteSpace(scannedFront))
                    {
                        viewModel.SelectedCard.FrontImagePath = scannedFront;
                        frontPath = scannedFront;
                        FrontImagePreview.Source = ImageSource.FromFile(scannedFront);
                    }

                    if (navData.TryGetValue("BackPath", out var scannedBack) &&
                        !string.IsNullOrWhiteSpace(scannedBack))
                    {
                        viewModel.SelectedCard.BackImagePath = scannedBack;
                        backPath = scannedBack;
                        BackImagePreview.Source = ImageSource.FromFile(scannedBack);
                    }

                    // Clear so it doesn't reapply on future appearances.
                    NavigationCache.Clear(nameof(CardPage));
                }
                else
                {
                    // No new scan data; just reflect whatever is already on the card.
                    if (!string.IsNullOrWhiteSpace(viewModel.SelectedCard.FrontImagePath))
                    {
                        frontPath = viewModel.SelectedCard.FrontImagePath;
                        FrontImagePreview.Source = ImageSource.FromFile(viewModel.SelectedCard.FrontImagePath);
                    }

                    if (!string.IsNullOrWhiteSpace(viewModel.SelectedCard.BackImagePath))
                    {
                        backPath = viewModel.SelectedCard.BackImagePath;
                        BackImagePreview.Source = ImageSource.FromFile(viewModel.SelectedCard.BackImagePath);
                    }
                }
            }
            catch (Exception ex)
            {
                // TODO - Optional: log ex using your logging strategy
                Console.WriteLine($"[CardPage] OnAppearing failed to load images: {ex}");
            }
        }

        /*
         * FUNCTION     : OnFrontImageTapped
         * DESCRIPTION  :
         *     Handles tap on the front image preview. If a front
         *     image path exists, opens the image viewer so the user
         *     can zoom and annotate the front image.
         */
        private async void OnFrontImageTapped(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(frontPath))
            {
                await DisplayAlert("No image", "No front image set for this card yet.", "OK");
                return;
            }

            await Navigation.PushModalAsync(new ImageViewerPage(frontPath));
        }

        /*
         * FUNCTION     : OnBackImageTapped
         * DESCRIPTION  :
         *     Handles tap on the back image preview. If a back
         *     image path exists, opens the image viewer so the user
         *     can zoom and annotate the back image.
         */
        private async void OnBackImageTapped(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(backPath))
            {
                await DisplayAlert("No image", "No back image set for this card yet.", "OK");
                return;
            }

            await Navigation.PushModalAsync(new ImageViewerPage(backPath));
        }

        /*
         * FUNCTION     : SavePhotoToLocalAsync
         * DESCRIPTION  :
         *     Copies a captured or picked photo into the app's local
         *     storage under a "CardImages" folder and returns the new
         *     absolute file path.
         */
        private async Task<string?> SavePhotoToLocalAsync(FileResult photo, string filePrefix)
        {
            if (photo == null)
            {
                return null;
            }

            try
            {
                string appDataDir = FileSystem.AppDataDirectory;
                string imagesDir = Path.Combine(appDataDir, "CardImages");

                if (!Directory.Exists(imagesDir))
                {
                    Directory.CreateDirectory(imagesDir);
                }

                string extension = Path.GetExtension(photo.FileName);
                if (string.IsNullOrWhiteSpace(extension))
                {
                    extension = ".jpg";
                }

                string fileName =
                    $"{filePrefix}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}{extension}";

                string destinationPath = Path.Combine(imagesDir, fileName);

                await using (Stream sourceStream = await photo.OpenReadAsync())
                await using (FileStream destinationStream = File.OpenWrite(destinationPath))
                {
                    await sourceStream.CopyToAsync(destinationStream);
                }

                return destinationPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CardPage] SavePhotoToLocalAsync failed: {ex}");
                await DisplayAlert("Image Save Error",
                    "Unable to save the selected photo. Please try again.",
                    "OK");
                return null;
            }
        }

        /*
         * FUNCTION     : OnTakePhotos
         * DESCRIPTION  :
         *     Legacy handler that reuses ScanPage camera UI to capture
         *     NEW front and back images for this card in a single flow.
         */
        private async void OnTakePhotos(object sender, EventArgs e)
        {
            if (isBusy)
            {
                return;
            }

            try
            {
                isBusy = true;

                await Navigation.PushAsync(new ScanPage(nameof(CardPage)));
            }
            catch (Exception ex)
            {
                await DisplayAlert("Navigation Error",
                    $"Unable to open the scan camera page: {ex.Message}",
                    "OK");
            }
            finally
            {
                isBusy = false;
            }
        }

        private async void OnTakeFrontPhoto(object sender, EventArgs e)
        {
            if (isBusy)
            {
                return;
            }

            try
            {
                isBusy = true;
                await Navigation.PushAsync(new ScanPage(nameof(CardPage), "FrontOnly"));
            }
            catch (Exception ex)
            {
                await DisplayAlert("Navigation Error",
                    $"Unable to open the scan camera page: {ex.Message}",
                    "OK");
            }
            finally
            {
                isBusy = false;
            }
        }

        private async void OnTakeBackPhoto(object sender, EventArgs e)
        {
            if (isBusy)
            {
                return;
            }

            try
            {
                isBusy = true;
                await Navigation.PushAsync(new ScanPage(nameof(CardPage), "BackOnly"));
            }
            catch (Exception ex)
            {
                await DisplayAlert("Navigation Error",
                    $"Unable to open the scan camera page: {ex.Message}",
                    "OK");
            }
            finally
            {
                isBusy = false;
            }
        }

        /*
         * FUNCTION     : OnPickPhotos
         * DESCRIPTION  :
         *     Legacy handler that attempts to pick both FRONT and BACK
         *     images in a single multi-select flow.
         */
        private async void OnPickPhotos(object sender, EventArgs e)
        {
            if (isBusy)
            {
                return;
            }

            try
            {
                isBusy = true;

                PickOptions options = new PickOptions
                {
                    PickerTitle = "Select front and back images for this card",
                    FileTypes = FilePickerFileType.Images
                };

                var pickResults = await FilePicker.PickMultipleAsync(options);

                if (pickResults == null)
                {
                    // user cancelled
                    return;
                }

                var photos = pickResults.ToList();
                if (photos.Count == 0)
                {
                    return;
                }

                // FRONT from first image
                FileResult frontPhoto = photos[0];
                string? savedFrontPath = await SavePhotoToLocalAsync(frontPhoto, "front");

                if (!string.IsNullOrWhiteSpace(savedFrontPath))
                {
                    viewModel.SelectedCard.FrontImagePath = savedFrontPath;
                    frontPath = savedFrontPath;
                    FrontImagePreview.Source = ImageSource.FromFile(savedFrontPath);
                }

                // BACK from second image if present
                if (photos.Count > 1)
                {
                    FileResult backPhoto = photos[1];
                    string? savedBackPath = await SavePhotoToLocalAsync(backPhoto, "back");

                    if (!string.IsNullOrWhiteSpace(savedBackPath))
                    {
                        viewModel.SelectedCard.BackImagePath = savedBackPath;
                        backPath = savedBackPath;
                        BackImagePreview.Source = ImageSource.FromFile(savedBackPath);
                    }
                }
            }
            catch (PermissionException)
            {
                await DisplayAlert("Permissions Required",
                    "Storage/photos permission is required to pick images.",
                    "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Pick Error",
                    $"An error occurred while picking photos: {ex.Message}",
                    "OK");
            }
            finally
            {
                isBusy = false;
            }
        }

        private async void OnScanFrontClicked(object sender, EventArgs e)
        {
            if (isBusy)
            {
                return;
            }

            try
            {
                isBusy = true;

                await Navigation.PushAsync(
                    new ScanPage(nameof(CardPage), "FrontOnly"));
            }
            catch (Exception ex)
            {
                await DisplayAlert("Navigation Error",
                    $"Unable to open the scan camera page for the front image: {ex.Message}",
                    "OK");
            }
            finally
            {
                isBusy = false;
            }
        }

        private async void OnScanBackClicked(object sender, EventArgs e)
        {
            if (isBusy)
            {
                return;
            }

            try
            {
                isBusy = true;

                await Navigation.PushAsync(
                    new ScanPage(nameof(CardPage), "BackOnly"));
            }
            catch (Exception ex)
            {
                await DisplayAlert("Navigation Error",
                    $"Unable to open the scan camera page for the back image: {ex.Message}",
                    "OK");
            }
            finally
            {
                isBusy = false;
            }
        }

        // ---------------------------------------------------------------------
        // LIVE CARD HEADER / GRADE SELECTION
        // ---------------------------------------------------------------------

        private void OnCardDetailChanged(object sender, TextChangedEventArgs e)
        {
            // Nested card fields such as Player.FullName and Team.Name do not
            // automatically notify this page's view model. Rebuild the visual
            // header whenever the editable metadata changes so the top of the
            // page reflects what the user is actually saving.
            viewModel.RefreshHeader();
        }

        private void OnTitleChanged(object sender, TextChangedEventArgs e)
        {
            // A manually edited title should immediately replace the stale
            // marketplace/source title in the large header.
            if (!string.IsNullOrWhiteSpace(viewModel.SelectedCard?.Title))
            {
                CardTitleLabel.Text = viewModel.SelectedCard.Title;
            }
            else
            {
                viewModel.RefreshHeader();
            }
        }

        private async void OnGradeSelectorTapped(object sender, TappedEventArgs e)
        {
            string[] labels = viewModel.GradeOptions.Select(option => option.Label).ToArray();
            string? selection = await DisplayActionSheet(
                "Grade / Condition",
                "Cancel",
                null,
                labels);

            if (string.IsNullOrWhiteSpace(selection) || selection == "Cancel")
                return;

            SportsCardsProGradeOption? option = viewModel.GradeOptions
                .FirstOrDefault(item => string.Equals(item.Label, selection, StringComparison.Ordinal));
            if (option != null)
                viewModel.SelectedGradeOption = option;
        }

        // ---------------------------------------------------------------------
        // SAVE / DELETE
        // ---------------------------------------------------------------------

        /*
         * FUNCTION     : SaveCardInternalAsync
         * DESCRIPTION  :
         *     Core save logic shared by OnSave and OnHighlightClicked.
         *     Does not manipulate isBusy or show UI alerts.
         */
        private async Task SaveCardInternalAsync()
        {
            Card? card = viewModel?.SelectedCard;
            if (card == null)
            {
                throw new InvalidOperationException("There is no card to save.");
            }

            await database.InitializeAsync();
            SQLite.SQLiteAsyncConnection connection = await database.GetConnectionAsync();

            // BaseModel creates a GUID before the card has ever been inserted.
            // Therefore Id != null is not proof that a SQLite row exists.
            Card? existing = await connection.Table<Card>()
                .Where(c => c.Id == card.Id)
                .FirstOrDefaultAsync();

            if (existing == null)
            {
                if (string.IsNullOrWhiteSpace(card.CollectionId))
                {
                    string? userAccountId = UserSession.CurrentUser?.UserAccountId;
                    if (string.IsNullOrWhiteSpace(userAccountId))
                    {
                        throw new InvalidOperationException(
                            "No signed-in user account is active, so CollectIQ cannot safely choose a collection for this card.");
                    }

                    CardCollection defaultCollection =
                        await database.GetOrCreateDefaultCollectionAsync(userAccountId);
                    card.CollectionId = defaultCollection.Id;
                }

                int inserted = await database.AddCardAsync(card);
                if (inserted <= 0)
                {
                    throw new InvalidOperationException("SQLite reported that zero card rows were inserted.");
                }

                isNewCard = false;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(card.CollectionId))
                {
                    card.CollectionId = existing.CollectionId;
                }

                int updated = await database.UpdateCardAsync(card);
                if (updated <= 0)
                {
                    throw new InvalidOperationException("SQLite reported that zero card rows were updated.");
                }
            }

            // Do not show a success message unless the exact saved row can actually
            // be read back and is attached to a real collection.
            Card? verified = await connection.Table<Card>()
                .Where(c => c.Id == card.Id && !c.IsDeleted)
                .FirstOrDefaultAsync();

            if (verified == null)
            {
                throw new InvalidOperationException(
                    "The save call completed, but the card could not be read back from SQLite.");
            }

            if (string.IsNullOrWhiteSpace(verified.CollectionId))
            {
                throw new InvalidOperationException(
                    "The card exists in SQLite but is not assigned to a collection.");
            }

            CardCollection? verifiedCollection = await connection.Table<CardCollection>()
                .Where(c => c.Id == verified.CollectionId && !c.IsDeleted)
                .FirstOrDefaultAsync();

            if (verifiedCollection == null)
            {
                throw new InvalidOperationException(
                    "The card was saved, but its collection does not exist or is deleted.");
            }
        }

        /*
         * FUNCTION     : OnSave
         * DESCRIPTION  :
         *     Saves the current card (insert or update) to the SQLite
         *     database and returns to the previous page on success.
         */
        private async void OnSave(object sender, EventArgs e)
        {
            if (isBusy)
            {
                return;
            }

            try
            {
                isBusy = true;

                await SaveCardInternalAsync();

                await DisplayAlert("Saved", "Card has been saved to your collection.", "OK");
                await Navigation.PopAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to save card: {ex.Message}", "OK");
            }
            finally
            {
                isBusy = false;
            }
        }

        /*
         * FUNCTION     : OnDelete
         * DESCRIPTION  :
         *     Deletes the current card from the SQLite database after
         *     prompting the user for confirmation.
         */
        private async void OnDelete(object sender, EventArgs e)
        {
            if (viewModel.SelectedCard == null)
            {
                await DisplayAlert("Delete", "This card has not been saved yet.", "OK");
                return;
            }

            bool confirm = await DisplayAlert("Delete Card",
                "Are you sure you want to remove this card from your collection?",
                "Delete",
                "Cancel");

            if (!confirm)
            {
                return;
            }

            try
            {
                await database.DeleteCardAsync(viewModel.SelectedCard.Id);
                await Navigation.PopAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to delete card: {ex.Message}", "OK");
            }
        }

        // ---------------------------------------------------------------------
        // INSIGHTS (EBAY)
        // ---------------------------------------------------------------------

        private void BuildInsightsFromPrices(IReadOnlyList<double> prices, string currency, string query)
        {
            if (prices == null || prices.Count == 0)
            {
                viewModel.SelectedCard.Insights =
                    new CardInsights
                    {
                        ListingCount = 0,
                        Currency = currency,
                        QueryUsed = query,
                        Summary = "No prices available for this query."
                    };

                return;
            }

            double min = prices.First();
            double max = prices.Last();
            double avg = prices.Average();
            double median;

            int count = prices.Count;
            if (count % 2 == 1)
            {
                median = prices[count / 2];
            }
            else
            {
                median = (prices[count / 2 - 1] + prices[count / 2]) / 2.0;
            }

            double suggested = (median * 0.7) + (avg * 0.3);
            double confidence = Math.Min(1.0, Math.Log10(count + 1) / 1.2);

            string summary = $"Based on {count} recent listings between ${min} USD and ${max} USD, " +
                             $"a fair value for this card is around ${suggested} USD.";

            viewModel.SelectedCard.Insights =
                new CardInsights
                {
                    MinPrice = min,
                    MaxPrice = max,
                    AveragePrice = avg,
                    MedianPrice = median,
                    SuggestedPrice = (decimal)suggested,
                    ListingCount = count,
                    Currency = currency,
                    LastUpdatedUtc = DateTime.UtcNow,
                    QueryUsed = query,
                    ConfidenceScore = confidence,
                    Summary = summary
                };
        }

        private string BuildDefaultQueryFromCard()
        {
            List<string> parts = new List<string>();

            if (viewModel.SelectedCard.Year.HasValue)
            {
                parts.Add(viewModel.SelectedCard.Year.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(viewModel.SelectedCard.Player.FullName))
            {
                parts.Add(viewModel.SelectedCard.Player.FullName);
            }

            if (!string.IsNullOrWhiteSpace(viewModel.SelectedCard.Set) &&
                !string.Equals(viewModel.SelectedCard.Set.Trim(), "eBay Import", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(viewModel.SelectedCard.Set);
            }

            if (!string.IsNullOrWhiteSpace(viewModel.SelectedCard.Number))
            {
                parts.Add($"#{viewModel.SelectedCard.Number}");
            }

            if (!string.IsNullOrWhiteSpace(viewModel.SelectedCard.Grading.Company))
            {
                parts.Add(viewModel.SelectedCard.Grading.Company);
            }

            if (viewModel.SelectedCard.Grading.Grade.HasValue)
            {
                parts.Add(viewModel.SelectedCard.Grading.Grade.Value.ToString("0.0#", CultureInfo.InvariantCulture));
            }

            return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private async void OnRefreshInsightsClicked(object sender, EventArgs e)
        {
            if (isBusy)
            {
                return;
            }

            try
            {
                isBusy = true;

                string query = string.IsNullOrWhiteSpace(EbayQueryEntry.Text)
                    ? BuildDefaultEbayQueryFromForm()
                    : EbayQueryEntry.Text.Trim();

                if (string.IsNullOrWhiteSpace(query))
                {
                    await DisplayAlert("eBay Insights",
                        "Enter a search phrase (player, year, set, #, grade) before refreshing.",
                        "OK");
                    return;
                }

                EbayQueryEntry.Text = query;
                EbayResultLabel.Text = $"Searching eBay for \"{query}\"...";

                var comps = await ebayService.SearchListingsAsync(
                    query,
                    limit: 80,
                    listingTypeFilter: "sold",
                    daysRange: 90);

                if (comps == null || comps.Count == 0)
                {
                    EbayResultLabel.Text = $"No sold listings found for \"{query}\".";
                    return;
                }

                var priced = comps
                    .Where(c => c.Price.HasValue && c.Price.Value > 0m)
                    .ToList();

                if (priced.Count == 0)
                {
                    EbayResultLabel.Text = $"No valid prices found for \"{query}\".";
                    return;
                }

                var priceDoubles = priced
                    .Select(c => (double)c.Price!.Value)
                    .OrderBy(v => v)
                    .ToList();

                BuildInsightsFromPrices(
                    priceDoubles,
                    currency: "USD",
                    query: query);

                decimal anchorPriceDec =
                    viewModel.SelectedCard.Insights.SuggestedPrice.HasValue
                        ? (decimal)Math.Round(viewModel.SelectedCard.Insights.SuggestedPrice.Value, 2)
                        : priced.First().Price!.Value;

                var anchorListing = new EbayListing
                {
                    Title = CardTitleLabel.Text,
                    Price = anchorPriceDec,
                    Status = "Active"
                };

                CardInsightsOverlay.OnEstimatedValueReady = async (value) =>
                {
                    if (!value.HasValue)
                    {
                        return;
                    }

                    anchorListing.Price = value.Value;

                    if (CardInsightsOverlay?.InsightsData != null)
                    {
                        anchorListing.EstimatedValue =
                            CardInsightsOverlay.InsightsData.SuggestedPrice ?? 0.00m;
                    }
                    else
                    {
                        anchorListing.EstimatedValue = 0.00m;
                    }

                    await CardInsightsOverlay.HideAsync();
                };

                await CardInsightsOverlay.ShowAsync(
                    anchorListing,
                    priced,
                    listingTypeFilter: "sold",
                    daysRangeFilter: 90);
            }
            catch (Exception ex)
            {
                await DisplayAlert("eBay Insights Error", ex.Message, "OK");
            }
            finally
            {
                isBusy = false;
            }
        }

        /*
         * FUNCTION     : OnPickFrontPhotoClicked
         * DESCRIPTION  :
         *     Allows the user to choose a single FRONT image from the
         *     device photo library.
         */
        private async void OnPickFrontPhotoClicked(object sender, EventArgs e)
        {
            if (isBusy)
            {
                return;
            }

            try
            {
                isBusy = true;

                PickOptions options = new PickOptions
                {
                    PickerTitle = "Select FRONT image for this card",
                    FileTypes = FilePickerFileType.Images
                };

                FileResult result = await FilePicker.PickAsync(options);

                if (result == null)
                {
                    // user cancelled
                    return;
                }

                string? savedFrontPath = await SavePhotoToLocalAsync(result, "front");

                if (!string.IsNullOrWhiteSpace(savedFrontPath))
                {
                    viewModel.SelectedCard.FrontImagePath = savedFrontPath;
                    frontPath = savedFrontPath;
                    FrontImagePreview.Source = ImageSource.FromFile(savedFrontPath);
                }
            }
            catch (PermissionException)
            {
                await DisplayAlert("Permissions Required",
                    "Storage/photos permission is required to pick a front image.",
                    "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Pick Error",
                    $"An error occurred while picking the front image: {ex.Message}",
                    "OK");
            }
            finally
            {
                isBusy = false;
            }
        }

        /*
         * FUNCTION     : OnPickBackPhotoClicked
         * DESCRIPTION  :
         *     Allows the user to choose a single BACK image from the
         *     device photo library.
         */
        private async void OnPickBackPhotoClicked(object sender, EventArgs e)
        {
            if (isBusy)
            {
                return;
            }

            try
            {
                isBusy = true;

                PickOptions options = new PickOptions
                {
                    PickerTitle = "Select BACK image for this card",
                    FileTypes = FilePickerFileType.Images
                };

                FileResult result = await FilePicker.PickAsync(options);

                if (result == null)
                {
                    // user cancelled
                    return;
                }

                string? savedBackPath = await SavePhotoToLocalAsync(result, "back");

                if (!string.IsNullOrWhiteSpace(savedBackPath))
                {
                    viewModel.SelectedCard.BackImagePath = savedBackPath;
                    backPath = savedBackPath;
                    BackImagePreview.Source = ImageSource.FromFile(savedBackPath);
                }
            }
            catch (PermissionException)
            {
                await DisplayAlert("Permissions Required",
                    "Storage/photos permission is required to pick a back image.",
                    "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Pick Error",
                    $"An error occurred while picking the back image: {ex.Message}",
                    "OK");
            }
            finally
            {
                isBusy = false;
            }
        }

        private string BuildDefaultEbayQueryFromForm()
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(PlayerEntry.Text))
            {
                parts.Add(PlayerEntry.Text.Trim());
            }

            if (!string.IsNullOrWhiteSpace(YearEntry.Text))
            {
                parts.Add(YearEntry.Text.Trim());
            }

            if (!string.IsNullOrWhiteSpace(TeamEntry.Text))
            {
                parts.Add(TeamEntry.Text.Trim());
            }

            var setText = SetEntry.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(setText) &&
                !string.Equals(setText, "eBay Import", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(setText);
            }

            if (!string.IsNullOrWhiteSpace(NumberEntry.Text))
            {
                parts.Add("#" + NumberEntry.Text.Trim());
            }

            string gradeSearchText = viewModel.SelectedGradeOption?.SearchSuffix ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(gradeSearchText) &&
                !string.Equals(viewModel.SelectedGradeOption?.Label, "Ungraded", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(gradeSearchText);
            }

            return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        /*
         * FUNCTION     : OnHighlightClicked
         * DESCRIPTION  :
         *     Handles the highlight button click from CardPage. It:
         *         1. Attempts to use any existing player-level HighlightReel.
         *         2. Falls back to card-level highlights if present.
         *         3. If none exist, it searches YouTube for
         *            "PlayerName career highlights", attaches the found
         *            reel to both Player and Card, saves the card, and
         *            then navigates to HighlightPlayerPage.
         */
        private async void OnHighlightClicked(object sender, EventArgs e)
        {
            if (isBusy || viewModel?.SelectedCard == null)
            {
                return;
            }

            try
            {
                isBusy = true;

                Card card = viewModel.SelectedCard;

                // ------------------------------------------------------------
                // 1) Try existing player-level highlight reel first.
                // ------------------------------------------------------------
                Player player = card.Player ?? new Player();
                if (string.IsNullOrWhiteSpace(player.FullName))
                {
                    player.FullName = card.Player.FullName ?? string.Empty;
                }

                HighlightReel playerReel = player.HighlightReel ?? new HighlightReel();
                if (playerReel.Clips == null)
                {
                    playerReel.Clips = new List<HighlightClip>();
                }

                HighlightClip existingPlayerClip = playerReel.Clips
                    .FirstOrDefault(c => c != null && !string.IsNullOrWhiteSpace(c.VideoUrl));

                if (existingPlayerClip != null && playerReel.Clips.Count > 0)
                {
                    await Navigation.PushAsync(new HighlightPlayerPage(card, playerReel, 0));
                    return;
                }

                // ------------------------------------------------------------
                // 2) Fall back to card-level highlights.
                // ------------------------------------------------------------
                HighlightReel cardReel = card.Highlights ?? new HighlightReel();
                if (cardReel.Clips == null)
                {
                    cardReel.Clips = new List<HighlightClip>();
                }

                HighlightClip existingCardClip = cardReel.Clips
                    .FirstOrDefault(c => c != null && !string.IsNullOrWhiteSpace(c.VideoUrl));

                if (existingCardClip != null && cardReel.Clips.Count > 0)
                {
                    await Navigation.PushAsync(new HighlightPlayerPage(card, cardReel, 0));
                    return;
                }

                // ------------------------------------------------------------
                // 3) We have no stored highlights; search YouTube.
                // ------------------------------------------------------------
                string playerName = card.Player.FullName;

                if (string.IsNullOrWhiteSpace(playerName))
                {
                    playerName = player.FullName;
                }

                if (string.IsNullOrWhiteSpace(playerName))
                {
                    await DisplayAlert(
                        "Highlights",
                        "Please fill in the Player/Name field before searching for highlights.",
                        "OK");
                    return;
                }

                string searchQuery = $"{playerName} career highlights";

                HighlightService highlightService = new HighlightService();
                HighlightReel foundReel =
                    await highlightService.FindHighlightReelAsync(searchQuery);

                if (foundReel == null || foundReel.Clips == null || foundReel.Clips.Count == 0)
                {
                    await DisplayAlert(
                        "Highlights",
                        "Sorry, no suitable highlight reel could be found for this card.",
                        "OK");
                    return;
                }

                // Attach the reel to the Player and Card domain models.
                player.HighlightReel = foundReel;
                card.Player = player;          // Updates PlayerJson

                card.Highlights = foundReel;   // Updates HighlightJson

                // Persist using shared save logic (no navigation or alert).
                await SaveCardInternalAsync();

                // Navigate to our CollectIQ-styled highlight player page.
                await Navigation.PushAsync(new HighlightPlayerPage(card, foundReel, 0));
            }
            catch (Exception ex)
            {
                await DisplayAlert(
                    "Highlights Error",
                    $"An error occurred while searching for highlights: {ex.Message}",
                    "OK");
            }
            finally
            {
                isBusy = false;
            }
        }
    }
}
