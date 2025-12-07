/*
* FILE: CollectionPage.xaml.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-25
* DESCRIPTION:
*     Displays, filters, deletes, and edits cards in the user’s collection.
*     This version includes fully working filter logic using camelCase fields.
*/

using CollectIQ.Models;
using CollectIQ.Services;
using Microsoft.Maui.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CollectIQ.Views
{
    public partial class CollectionPage : ContentPage
    {
        private readonly SqliteDatabase database = new();
        private List<Card> allCards = new();   // full list, always preserved

        public ObservableCollection<Card> Cards { get; } = new();

        public CollectionPage()
        {
            InitializeComponent();

            // When the overlay raises FiltersChanged → reapply filters
            FilterOverlay.FiltersChanged += (_, __) => ApplyFilters();

            BindingContext = this;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadCardsAsync();
            CollectionCountLabel.Text = $"{Cards.Count} Cards";
            CollectionValueLabel.Text = "Est. Total Value: $" + Cards.Sum(c => c.EstimatedValue ?? 0).ToString("0.00");
        }

        /// <summary>
        /// Loads collection from DB and stores a full copy in allCards.
        /// </summary>
        private async Task LoadCardsAsync()
        {
            try
            {
                await database.InitializeAsync();
                Cards.Clear();

                var cardsFromDb = await database.GetAllCardsAsync();

                allCards = cardsFromDb.ToList();   // <-- store full copy

                foreach (var card in allCards)
                    Cards.Add(card);

                EmptyMessage.IsVisible = Cards.Count == 0;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to load collection: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Swipe delete handler.
        /// </summary>
        private async void OnDeleteCard(object sender, EventArgs e)
        {
            if (sender is SwipeItem swipe && swipe.CommandParameter is Card card)
            {
                bool confirm = await DisplayAlert("Confirm Delete",
                    $"Delete '{card.Title}'?", "Delete", "Cancel");

                if (!confirm) return;

                try
                {
                    await database.DeleteCardAsync(card.Id);
                    allCards.Remove(card);
                    Cards.Remove(card);
                    CollectionCountLabel.Text = $"{Cards.Count} Cards";
                    CollectionValueLabel.Text = "Est. Total Value: $" + Cards.Sum(c => c.EstimatedValue ?? 0).ToString("0.00");
                    EmptyMessage.IsVisible = Cards.Count == 0;
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", ex.Message, "OK");
                }
            }
        }

        /// <summary>
        /// Opens edit page.
        /// </summary>
        private async void OnEditCard(object sender, EventArgs e)
        {
            if (sender is SwipeItem swipe && swipe.CommandParameter is Card card)
                await Navigation.PushAsync(new CardPage(card));
        }

        /// <summary>
        /// Navigates to Scan tab to add a card.
        /// </summary>
        private async void OnAddCardClicked(object sender, EventArgs e)
        {
            try
            {
                await Shell.Current.GoToAsync("//ScanPage");
            }
            catch
            {
                await DisplayAlert("Error", "Unable to switch to Scan tab.", "OK");
            }
        }

        #region Filtering

        /// <summary>
        /// Applies filters from the FilterOverlay control.
        /// </summary>
        private void ApplyFilters()
        {
            if (allCards == null || allCards.Count == 0)
                return;

            IEnumerable<Card> filtered = allCards;

            // SEARCH
            string search = FilterOverlay.Search?.Trim().ToLower() ?? "";
            if (!string.IsNullOrEmpty(search))
            {
                filtered = filtered.Where(c =>
                    (c.Name?.ToLower().Contains(search) ?? false) ||
                    (c.Title?.ToLower().Contains(search) ?? false) ||
                    (c.Team?.ToLower().Contains(search) ?? false));
            }

            // SPORT FILTERS
            var sports = new List<string>();
            if (FilterOverlay.Hockey) sports.Add("Hockey");
            if (FilterOverlay.Football) sports.Add("Football");
            if (FilterOverlay.Basketball) sports.Add("Basketball");
            if (FilterOverlay.Pokemon) sports.Add("Pokemon");

            if (sports.Count > 0)
            {
                filtered = filtered.Where(c =>
                    !string.IsNullOrWhiteSpace(c.Sport) &&
                    sports.Contains(c.Sport, StringComparer.OrdinalIgnoreCase));
            }

            // YEAR RANGE
            filtered = filtered.Where(c =>
                (c.Year ?? 0) >= FilterOverlay.MinYear &&
                (c.Year ?? 0) <= FilterOverlay.MaxYear);

            // VALUE RANGE
            filtered = filtered.Where(c =>
                (c.EstimatedValue ?? 0) >= FilterOverlay.MinValue &&
                (c.EstimatedValue ?? 0) <= FilterOverlay.MaxValue);

            // --- FINAL UPDATE ---
            Cards.Clear();
            foreach (var card in filtered)
                Cards.Add(card);

            EmptyMessage.IsVisible = Cards.Count == 0;
        }

        /// <summary>
        /// shows the filter overlay
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void OnFilterClicked(object sender, EventArgs e)
        {
            await FilterOverlay.ShowAsync();
        }

        #endregion

        #region Exporting
        private async void OnExportCsvClicked(object sender, EventArgs e)
        {
            try
            {
                if (CardsCollectionView.ItemsSource is not IEnumerable<object> items)
                {
                    await DisplayAlert("Export", "No cards to export yet.", "OK");
                    return;
                }

                var cards = items.OfType<Card>().ToList();
                if (cards.Count == 0)
                {
                    await DisplayAlert("Export", "No cards to export yet.", "OK");
                    return;
                }

                // Build CSV (Excel-compatible)
                var sb = new StringBuilder();
                sb.AppendLine("Name,Team,Year,Set,Number,GradeCompany,Grade,PurchasePrice,EstimatedValue,FrontImagePath,BackImagePath");

                foreach (var c in cards)
                {
                    string csvLine = string.Join(",",
                        EscapeCsv(c.Name),
                        EscapeCsv(c.Team),
                        c.Year?.ToString() ?? string.Empty,
                        EscapeCsv(c.Set),
                        EscapeCsv(c.Number),
                        EscapeCsv(c.GradeCompany),
                        c.Grade?.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                        c.PurchasePrice?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                        c.EstimatedValue?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                        EscapeCsv(c.FrontImagePath),
                        EscapeCsv(c.BackImagePath));

                    sb.AppendLine(csvLine);
                }

                var fileName = $"CollectIQ_Collection_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                var filePath = Path.Combine(FileSystem.CacheDirectory, fileName);
                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);

                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = "Export Collection (CSV)",
                    File = new ShareFile(filePath)
                });
            }
            catch (Exception ex)
            {
                await DisplayAlert("Export Error", ex.Message, "OK");
            }
        }

        private async void OnExportPdfClicked(object sender, EventArgs e)
        {
            // Placeholder so the button does not break anything.
            // You can later plug in a real PDF generator (Syncfusion, QuestPDF, etc.)
            await DisplayAlert(
                "Export to PDF",
                "PDF export is not wired up yet. CSV export is available now, and PDF can be added with a PDF library later.",
                "OK");
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            bool mustQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n');
            string escaped = value.Replace("\"", "\"\"");
            return mustQuote ? $"\"{escaped}\"" : escaped;
        }
        #endregion

    }
}
