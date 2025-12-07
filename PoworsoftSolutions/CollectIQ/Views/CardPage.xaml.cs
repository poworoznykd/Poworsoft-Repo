// -------------------------------------------------------------------------------------------------
// File: CardPage.xaml.cs
// Description: Detail / edit page for a single card. Shows front/back images, editable fields,
//              and live eBay-based pricing insights via the shared InsightsOverlayControl.
// -------------------------------------------------------------------------------------------------

using CollectIQ.Controls;
using CollectIQ.Interfaces;
using CollectIQ.Models;
using CollectIQ.Services;
using CollectIQ.Utilities;
using CollectIQ.ViewModels;
using CollectIQ.ViewModels.Auth;
using Microsoft.Maui.Controls;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using static AndroidX.Core.Text.Util.LocalePreferences.FirstDayOfWeek;

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

        public CardPage()
            : this(null)
        {
        }

        public CardPage(Card card)
        {
            InitializeComponent();
            viewModel = new CardPageViewModel(card);
            ebayService = new EbayService(new HttpClient());
            BindingContext = viewModel;
            frontPath = viewModel.SelectedCard.FrontImagePath;
            backPath = viewModel.SelectedCard.BackImagePath;
        }

        private void OnAppearing(object sender, EventArgs e)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(viewModel.SelectedCard.FrontImagePath))
                {
                    FrontImagePreview.Source = ImageSource.FromFile(viewModel.SelectedCard.FrontImagePath);
                }
                if (!string.IsNullOrWhiteSpace(viewModel.SelectedCard.BackImagePath))
                {
                    BackImagePreview.Source = ImageSource.FromFile(viewModel.SelectedCard.BackImagePath);
                }
            }
            catch(Exception ex)
            {
                //TODO - Do something with ex
            }
        }


        private async void OnFrontImageTapped(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(frontPath))
            {
                await DisplayAlert("No image", "No front image set for this card yet.", "OK");
                return;
            }

            await Navigation.PushModalAsync(new ImageViewerPage(frontPath));
        }

        private async void OnBackImageTapped(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(backPath))
            {
                await DisplayAlert("No image", "No back image set for this card yet.", "OK");
                return;
            }

            await Navigation.PushModalAsync(new ImageViewerPage(backPath));
        }

        // These stubs keep your existing buttons wired without breaking the app.
        // You can swap them out later for your full camera / gallery flows.
        private async void OnTakePhotos(object sender, EventArgs e)
        {
            await DisplayAlert("Not implemented", "Photo capture from the card page is not wired yet. You can still annotate existing images via the image viewer.", "OK");
        }

        private async void OnPickPhotos(object sender, EventArgs e)
        {
            await DisplayAlert("Not implemented", "Picking photos from the gallery on this page is not wired yet. Use your existing flow or image viewer for now.", "OK");
        }

        // ---------------------------------------------------------------------
        // SAVE / DELETE
        // ---------------------------------------------------------------------

        private async void OnSave(object sender, EventArgs e)
        {
            if (isBusy)
            {
                return;
            }

            try
            {
                isBusy = true;
                if (viewModel.SelectedCard.Id != null)
                {
                    await database.UpdateCardAsync(viewModel.SelectedCard);
                }
                else
                {
                    await database.AddCardAsync(viewModel.SelectedCard);
                    isNewCard = false;
                }

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

        private async void OnDelete(object sender, EventArgs e)
        {
            if (viewModel.SelectedCard == null)
            {
                await DisplayAlert("Delete", "This card has not been saved yet.", "OK");
                return;
            }

            bool confirm = await DisplayAlert("Delete Card", "Are you sure you want to remove this card from your collection?", "Delete", "Cancel");
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

            // Simple heuristic suggested price: closer to median but nudged toward average
            double suggested = (median * 0.7) + (avg * 0.3);

            // Confidence: more listings -> higher confidence, capped at 1.0
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

            if (!string.IsNullOrWhiteSpace(viewModel.SelectedCard.Name))
            {
                parts.Add(viewModel.SelectedCard.Name);
            }

            // Avoid polluting search with sentinel text like "eBay Import"
            if (!string.IsNullOrWhiteSpace(viewModel.SelectedCard.Set) &&
                !string.Equals(viewModel.SelectedCard.Set.Trim(), "eBay Import", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(viewModel.SelectedCard.Set);
            }

            if (!string.IsNullOrWhiteSpace(viewModel.SelectedCard.Number))
            {
                parts.Add($"#{viewModel.SelectedCard.Number}");
            }

            if (!string.IsNullOrWhiteSpace(viewModel.SelectedCard.GradeCompany))
            {
                parts.Add(viewModel.SelectedCard.GradeCompany);
            }

            if (viewModel.SelectedCard.Grade.HasValue)
            {
                parts.Add(viewModel.SelectedCard.Grade.Value.ToString("0.0#", CultureInfo.InvariantCulture));
            }

            return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        // ============================================================
        // eBay Insights (per-card) - uses shared overlay control
        // ============================================================

        private async void OnRefreshInsightsClicked(object sender, EventArgs e)
        {
            if (isBusy)
            {
                return;
            }

            try
            {
                isBusy = true;

                // Build a query: use typed query if present; otherwise build from card fields
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

                // Fetch comps – using sold listings over last 90 days by default
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

                // Build insights from price list
                var priceDoubles = priced
                    .Select(c => (double)c.Price!.Value)
                    .OrderBy(v => v)
                    .ToList();

                BuildInsightsFromPrices(
                    priceDoubles,
                    currency: "USD",
                    query: query);

              

                // Prepare anchor listing representing this card for the overlay
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

                    // Update the selected listing
                    anchorListing.Price = value.Value;
                    if(CardInsightsOverlay?.InsightsData != null)
                        anchorListing.EstimatedValue = CardInsightsOverlay.InsightsData.SuggestedPrice ?? 0.00m;
                    else
                    {
                        anchorListing.EstimatedValue = 0.00m;
                    }

                    // Properly await the async hide call
                    await CardInsightsOverlay.HideAsync();
                };

                // Then: show the overlay
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

        private string BuildDefaultEbayQueryFromForm()
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(PlayerEntry.Text))
                parts.Add(PlayerEntry.Text.Trim());

            if (!string.IsNullOrWhiteSpace(YearEntry.Text))
                parts.Add(YearEntry.Text.Trim());

            if (!string.IsNullOrWhiteSpace(TeamEntry.Text))
                parts.Add(TeamEntry.Text.Trim());

            // Do NOT include the sentinel "eBay Import" value in the query
            var setText = SetEntry.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(setText) &&
                !string.Equals(setText, "eBay Import", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(setText);
            }

            if (!string.IsNullOrWhiteSpace(NumberEntry.Text))
                parts.Add("#" + NumberEntry.Text.Trim());

            if (!string.IsNullOrWhiteSpace(GradeCoEntry.Text) &&
                !string.IsNullOrWhiteSpace(GradeEntry.Text))
            {
                parts.Add($"{GradeCoEntry.Text.Trim()} {GradeEntry.Text.Trim()}");
            }

            // "Josh Allen 2018 Prizm #205 PSA 10"
            return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

    }
}
