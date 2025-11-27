// -------------------------------------------------------------------------------------------------
// File: CardPage.xaml.cs
// Description: Detail / edit page for a single card. Shows front/back images, editable fields,
//              and live eBay-based pricing insights.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CollectIQ.Models;
using CollectIQ.Services;
using CollectIQ.Utilities;
using Microsoft.Maui.Controls;

namespace CollectIQ.Views
{
    public partial class CardPage : ContentPage
    {
        private readonly SqliteDatabase database = new();
        private readonly EbayService ebayService;

        private Card currentCard;
        private CardInsights currentInsights;

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

            ebayService = new EbayService(new HttpClient());

            currentCard = card ?? new Card();
            isNewCard = card == null;

            frontPath = currentCard.FrontImagePath;
            backPath = currentCard.BackImagePath;

            currentInsights = new CardInsights();

            PopulateFormFromCard();
            ApplyInsightsToUi(currentInsights);
        }

        private void OnAppearing(object sender, EventArgs e)
        {
            // If ImageViewerPage pushed updated image paths into the navigation cache, pick them up.
            try
            {
                var cachedResult = NavigationCache.Get<Dictionary<string, string>>(nameof(CardPage));
                if (cachedResult != null)
                {
                    if (cachedResult.TryGetValue("FrontImagePath", out var front))
                    {
                        frontPath = front;
                        currentCard.FrontImagePath = front;
                        if (!string.IsNullOrWhiteSpace(front))
                        {
                            FrontImagePreview.Source = ImageSource.FromFile(front);
                        }
                    }

                    if (cachedResult.TryGetValue("BackImagePath", out var back))
                    {
                        backPath = back;
                        currentCard.BackImagePath = back;
                        if (!string.IsNullOrWhiteSpace(back))
                        {
                            BackImagePreview.Source = ImageSource.FromFile(back);
                        }
                    }

                    NavigationCache.Clear(nameof(CardPage));
                }
            }
            catch
            {
                // If NavigationCache is not available or throws for any reason,
                // we don't want to crash the page. Silently ignore.
            }
        }

        // ---------------------------------------------------------------------
        // UI POPULATION / MAPPING
        // ---------------------------------------------------------------------

        private void PopulateFormFromCard()
        {
            // Map card -> entries / labels
            PlayerEntry.Text = currentCard.Player;
            TeamEntry.Text = currentCard.Team;
            YearEntry.Text = currentCard.Year.HasValue ? currentCard.Year.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
            SetEntry.Text = currentCard.Set;
            NumberEntry.Text = currentCard.Number;
            GradeCoEntry.Text = currentCard.GradeCompany;
            GradeEntry.Text = currentCard.Grade.HasValue ? currentCard.Grade.Value.ToString("0.0#", CultureInfo.InvariantCulture) : string.Empty;
            PriceEntry.Text = currentCard.PurchasePrice.HasValue ? currentCard.PurchasePrice.Value.ToString("0.00", CultureInfo.InvariantCulture) : string.Empty;

            if (!string.IsNullOrWhiteSpace(currentCard.FrontImagePath))
            {
                FrontImagePreview.Source = ImageSource.FromFile(currentCard.FrontImagePath);
            }

            if (!string.IsNullOrWhiteSpace(currentCard.BackImagePath))
            {
                BackImagePreview.Source = ImageSource.FromFile(currentCard.BackImagePath);
            }

            // Title + subtitle
            UpdateHeaderFromCard();

            // Estimated value label from stored card value if it exists
            if (currentCard.EstimatedValue.HasValue)
            {
                EstimatedValueLabel.Text = FormatCurrency(currentCard.EstimatedValue.Value, "USD");
            }
            else
            {
                EstimatedValueLabel.Text = "$0.00";
            }
        }

        private void UpdateHeaderFromCard()
        {
            string yearText = currentCard.Year.HasValue ? currentCard.Year.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
            string setText = string.IsNullOrWhiteSpace(currentCard.Set) ? string.Empty : currentCard.Set;
            string numberText = string.IsNullOrWhiteSpace(currentCard.Number) ? string.Empty : $"#{currentCard.Number}";
            string teamText = string.IsNullOrWhiteSpace(currentCard.Team) ? string.Empty : currentCard.Team;

            string title = !string.IsNullOrWhiteSpace(currentCard.Title)
                ? currentCard.Title
                : $"{yearText} {currentCard.Player}";

            CardTitleLabel.Text = title.Trim();

            // Subtitle: Year · Team · Set · #
            List<string> parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(yearText)) parts.Add(yearText);
            if (!string.IsNullOrWhiteSpace(teamText)) parts.Add(teamText);
            if (!string.IsNullOrWhiteSpace(setText)) parts.Add(setText);
            if (!string.IsNullOrWhiteSpace(numberText)) parts.Add(numberText);

            CardSubtitleLabel.Text = parts.Count > 0 ? string.Join(" · ", parts) : "Tap Save to update details";
        }

        private void UpdateCardFromForm()
        {
            currentCard.Player = PlayerEntry.Text?.Trim();
            currentCard.Team = TeamEntry.Text?.Trim();
            currentCard.Set = SetEntry.Text?.Trim();
            currentCard.Number = NumberEntry.Text?.Trim();
            currentCard.GradeCompany = GradeCoEntry.Text?.Trim();

            if (int.TryParse(YearEntry.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
            {
                currentCard.Year = year;
            }
            else
            {
                currentCard.Year = null;
            }

            if (double.TryParse(GradeEntry.Text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var grade))
            {
                currentCard.Grade = grade;
            }
            else
            {
                currentCard.Grade = null;
            }

            if (decimal.TryParse(PriceEntry.Text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var price))
            {
                currentCard.PurchasePrice = price;
            }
            else
            {
                currentCard.PurchasePrice = null;
            }

            currentCard.FrontImagePath = frontPath;
            currentCard.BackImagePath = backPath;

            // Derive a default title if none is set
            if (string.IsNullOrWhiteSpace(currentCard.Title))
            {
                string yearText = currentCard.Year.HasValue ? currentCard.Year.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
                currentCard.Title = $"{yearText} {currentCard.Player} {currentCard.Set} #{currentCard.Number}".Trim();
            }

            // Keep estimated value in sync with insights if we have them
            if (currentInsights != null && currentInsights.SuggestedPrice.HasValue)
            {
                currentCard.EstimatedValue = (decimal?)currentInsights.SuggestedPrice.Value;
            }
        }

        // ---------------------------------------------------------------------
        // IMAGE TAPS -> IMAGE VIEWER
        // ---------------------------------------------------------------------

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
                UpdateCardFromForm();
                UpdateHeaderFromCard();

                if (currentCard.Id != null)
                {
                    await database.UpdateCardAsync(currentCard);
                }
                else
                {
                    await database.AddCardAsync(currentCard);
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
            if (currentCard == null)
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
                await database.DeleteCardAsync(currentCard.Id);
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

        private async void OnRefreshInsightsClicked(object sender, EventArgs e)
        {
            if (isBusy)
            {
                return;
            }

            string query = EbayQueryEntry.Text?.Trim();

            if (string.IsNullOrWhiteSpace(query))
            {
                // If no manual query, build a best-guess from the card fields
                query = BuildDefaultQueryFromCard();
                EbayQueryEntry.Text = query;
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                await DisplayAlert("Missing query", "Please enter a phrase to search on eBay (for example: '2020 Prizm Joe Burrow Silver #307').", "OK");
                return;
            }

            try
            {
                isBusy = true;
                EbayResultLabel.Text = "Searching eBay for comps…";

                // Use same pattern as EbaySearchPage: text search, recent listings
                var listings = await ebayService.SearchListingsAsync(query, 60, "ALL", 90);

                var prices = listings
                    .Select(l => (double)l.Price)
                    .Where(p => p > 0)
                    .OrderBy(p => p)
                    .ToList();

                if (prices.Count == 0)
                {
                    EbayResultLabel.Text = "No priced listings found for this query.";
                    currentInsights = new CardInsights
                    {
                        ListingCount = 0,
                        QueryUsed = query,
                        Summary = "No recent comps were found."
                    };
                }
                else
                {
                    currentInsights = BuildInsightsFromPrices(prices, "USD", query);
                }

                ApplyInsightsToUi(currentInsights);

                // Push estimated value back onto the card so it shows up in header
                if (currentInsights.SuggestedPrice.HasValue)
                {
                    currentCard.EstimatedValue = (decimal?)currentInsights.SuggestedPrice.Value;
                    EstimatedValueLabel.Text = FormatCurrency((decimal)currentInsights.SuggestedPrice.Value, currentInsights.Currency);
                }

                EbayResultLabel.Text = $"Found {currentInsights.ListingCount} listings. Insights updated.";
            }
            catch (Exception ex)
            {
                EbayResultLabel.Text = "Error while fetching insights.";
                await DisplayAlert("Error", $"Failed to refresh insights: {ex.Message}", "OK");
            }
            finally
            {
                isBusy = false;
            }
        }

        private CardInsights BuildInsightsFromPrices(IReadOnlyList<double> prices, string currency, string query)
        {
            if (prices == null || prices.Count == 0)
            {
                return new CardInsights
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

            string summary = $"Based on {count} recent listings between {FormatCurrencyDecimal(min, currency)} and {FormatCurrencyDecimal(max, currency)}, " +
                             $"a fair value for this card is around {FormatCurrencyDecimal(suggested, currency)}.";

            return new CardInsights
            {
                MinPrice = min,
                MaxPrice = max,
                AveragePrice = avg,
                MedianPrice = median,
                SuggestedPrice = suggested,
                ListingCount = count,
                Currency = currency,
                LastUpdatedUtc = DateTime.UtcNow,
                QueryUsed = query,
                ConfidenceScore = confidence,
                Summary = summary
            };
        }

        private void ApplyInsightsToUi(CardInsights insights)
        {
            if (insights == null)
            {
                InsightsCountLabelCard.Text = "0";
                InsightsRangeLabelCard.Text = "$0 - $0";
                InsightsMinLabelCard.Text = "$0";
                InsightsMaxLabelCard.Text = "$0";
                InsightsMedianLabelCard.Text = "$0";
                InsightsEstimatedValueLabel.Text = "$0";
                InsightsSummaryLabelCard.Text = "No insights yet.";
                InsightsLastUpdatedLabel.Text = "(never)";
                return;
            }

            string currency = string.IsNullOrWhiteSpace(insights.Currency) ? "USD" : insights.Currency;

            InsightsCountLabelCard.Text = insights.ListingCount.ToString(CultureInfo.InvariantCulture);

            if (insights.MinPrice.HasValue && insights.MaxPrice.HasValue)
            {
                InsightsRangeLabelCard.Text =
                    $"{FormatCurrencyDecimal(insights.MinPrice.Value, currency)} - {FormatCurrencyDecimal(insights.MaxPrice.Value, currency)}";
            }
            else
            {
                InsightsRangeLabelCard.Text = "n/a";
            }

            InsightsMinLabelCard.Text = insights.MinPrice.HasValue
                ? FormatCurrencyDecimal(insights.MinPrice.Value, currency)
                : "n/a";

            InsightsMaxLabelCard.Text = insights.MaxPrice.HasValue
                ? FormatCurrencyDecimal(insights.MaxPrice.Value, currency)
                : "n/a";

            InsightsMedianLabelCard.Text = insights.MedianPrice.HasValue
                ? FormatCurrencyDecimal(insights.MedianPrice.Value, currency)
                : "n/a";

            InsightsEstimatedValueLabel.Text = insights.SuggestedPrice.HasValue
                ? FormatCurrencyDecimal(insights.SuggestedPrice.Value, currency)
                : "n/a";

            if (insights.LastUpdatedUtc.HasValue)
            {
                InsightsLastUpdatedLabel.Text = insights.LastUpdatedUtc.Value.ToLocalTime().ToString("g");
            }
            else
            {
                InsightsLastUpdatedLabel.Text = "(never)";
            }

            InsightsSummaryLabelCard.Text = string.IsNullOrWhiteSpace(insights.Summary)
                ? "Insights ready."
                : insights.Summary;
        }

        private string BuildDefaultQueryFromCard()
        {
            List<string> parts = new List<string>();

            if (currentCard.Year.HasValue)
            {
                parts.Add(currentCard.Year.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(currentCard.Player))
            {
                parts.Add(currentCard.Player);
            }

            if (!string.IsNullOrWhiteSpace(currentCard.Set))
            {
                parts.Add(currentCard.Set);
            }

            if (!string.IsNullOrWhiteSpace(currentCard.Number))
            {
                parts.Add($"#{currentCard.Number}");
            }

            if (!string.IsNullOrWhiteSpace(currentCard.GradeCompany))
            {
                parts.Add(currentCard.GradeCompany);
            }

            if (currentCard.Grade.HasValue)
            {
                parts.Add(currentCard.Grade.Value.ToString("0.0#", CultureInfo.InvariantCulture));
            }

            return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        // ---------------------------------------------------------------------
        // FORMAT HELPERS
        // ---------------------------------------------------------------------

        private static string FormatCurrency(decimal value, string currency)
        {
            string prefix = currency?.ToUpperInvariant() == "USD" ? "$" : $"{currency} ";
            return prefix + value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string FormatCurrencyDecimal(double value, string currency)
        {
            string prefix = currency?.ToUpperInvariant() == "USD" ? "$" : $"{currency} ";
            return prefix + value.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
