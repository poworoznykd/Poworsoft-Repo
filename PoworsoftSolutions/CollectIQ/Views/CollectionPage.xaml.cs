/*
* FILE: CollectionPage.xaml.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-25
* DESCRIPTION:
*     Code-behind for CollectionPage.
*     - Loads cards from the local SQLite database.
*     - Supports swipe actions (Edit/Delete) and export actions.
*     - Adds compact search + filter support to help users quickly
*       find cards by player/title/set/team/sport.
*
* NOTES:
*     - This page intentionally keeps UI logic here (code-behind)
*       to avoid introducing a new ViewModel during active iteration.
*       If/when we move to MVVM, the filtering logic can be lifted
*       into a CollectionViewModel with an ObservableCollection.
*/

using CollectIQ.Interfaces;
using CollectIQ.Models;
using CollectIQ.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace CollectIQ.Views
{
    public partial class CollectionPage : ContentPage
    {
        private readonly IDatabase database;

        // Full, unfiltered set from the database.
        private readonly List<Card> allCards = new List<Card>();

        // Current search text (SearchBar).
        private string searchText = string.Empty;

        public CollectionPage(IDatabase database)
        {
            InitializeComponent();
            this.database = database;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadCardsAsync();
        }

        // ============================================================
        //  DATA LOADING
        // ============================================================

        private async Task LoadCardsAsync()
        {
            try
            {
                await database.InitializeAsync();

                List<Card> cards = await database.GetAllCardsAsync();

                allCards.Clear();
                allCards.AddRange(cards);

                PopulateFilterPickers();
                ApplyFilters();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to load collection: {ex.Message}", "OK");

                allCards.Clear();
                CardsCollectionView.ItemsSource = new List<Card>();
                RefreshStats(0m, 0);
                UpdateActiveFiltersLabel(0, 0);
            }
        }

        // ============================================================
        //  SEARCH + FILTER
        // ============================================================

        private void PopulateFilterPickers()
        {
            if (SportPicker == null || TeamPicker == null || SetPicker == null)
            {
                return;
            }

            List<string> sports = allCards
                .Select(GetSportName)
                .Where(s => !string.IsNullOrWhiteSpace(s) &&
                            !s.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToList();

            List<string> teams = allCards
                .Select(GetTeamName)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t)
                .ToList();

            List<string> sets = allCards
                .Select(c => (c.Set ?? string.Empty).Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToList();

            SportPicker.ItemsSource = new List<string> { "Any" }.Concat(sports).ToList();
            TeamPicker.ItemsSource = new List<string> { "Any" }.Concat(teams).ToList();
            SetPicker.ItemsSource = new List<string> { "Any" }.Concat(sets).ToList();

            SportPicker.SelectedIndex = 0;
            TeamPicker.SelectedIndex = 0;
            SetPicker.SelectedIndex = 0;

            UpdateActiveFiltersLabel(-1, -1);
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            searchText = e.NewTextValue ?? string.Empty;
            ApplyFilters();
        }

        private void OnToggleFilterPanelClicked(object sender, EventArgs e)
        {
            FilterPanelBorder.IsVisible = !FilterPanelBorder.IsVisible;
            FilterToggleButton.Text = FilterPanelBorder.IsVisible ? "Hide" : "Filter";
        }

        private void OnFilterChanged(object sender, EventArgs e)
        {
            ApplyFilters();
        }

        private void OnClearFiltersClicked(object sender, EventArgs e)
        {
            searchText = string.Empty;
            if (CollectionSearchBar != null)
            {
                CollectionSearchBar.Text = string.Empty;
            }

            if (SportPicker != null) SportPicker.SelectedIndex = 0;
            if (TeamPicker != null) TeamPicker.SelectedIndex = 0;
            if (SetPicker != null) SetPicker.SelectedIndex = 0;

            ApplyFilters();
        }

        private void ApplyFilters()
        {
            IEnumerable<Card> query = allCards;

            string selectedSport = GetPickerValueOrEmpty(SportPicker);
            string selectedTeam = GetPickerValueOrEmpty(TeamPicker);
            string selectedSet = GetPickerValueOrEmpty(SetPicker);

            if (!string.IsNullOrWhiteSpace(selectedSport))
            {
                query = query.Where(c => GetSportName(c)
                    .Equals(selectedSport, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(selectedTeam))
            {
                query = query.Where(c => GetTeamName(c)
                    .Equals(selectedTeam, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(selectedSet))
            {
                query = query.Where(c =>
                    string.Equals((c.Set ?? string.Empty).Trim(),
                                  selectedSet,
                                  StringComparison.OrdinalIgnoreCase));
            }

            string needle = (searchText ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(needle))
            {
                query = query.Where(c => CardMatchesSearch(c, needle));
            }

            List<Card> filtered = query.ToList();

            CardsCollectionView.ItemsSource = filtered;

            decimal total = (decimal)filtered.Sum(c => c.EstimatedValue);
            RefreshStats(total, filtered.Count);

            UpdateActiveFiltersLabel(filtered.Count, allCards.Count);
        }

        private static bool CardMatchesSearch(Card card, string needle)
        {
            string title = card.Title ?? string.Empty;
            string display = TryGetDisplayName(card);
            string player = card.Player?.FullName ?? string.Empty;
            string team = GetTeamName(card);
            string set = card.Set ?? string.Empty;
            string number = card.Number ?? string.Empty;
            string sport = GetSportName(card);
            string year = card.Year > 0 ? card.Year.ToString() : string.Empty;

            return title.Contains(needle, StringComparison.OrdinalIgnoreCase)
                   || display.Contains(needle, StringComparison.OrdinalIgnoreCase)
                   || player.Contains(needle, StringComparison.OrdinalIgnoreCase)
                   || team.Contains(needle, StringComparison.OrdinalIgnoreCase)
                   || set.Contains(needle, StringComparison.OrdinalIgnoreCase)
                   || number.Contains(needle, StringComparison.OrdinalIgnoreCase)
                   || sport.Contains(needle, StringComparison.OrdinalIgnoreCase)
                   || year.Contains(needle, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetPickerValueOrEmpty(Picker picker)
        {
            if (picker == null || picker.SelectedItem == null)
            {
                return string.Empty;
            }

            string value = picker.SelectedItem.ToString() ?? string.Empty;
            if (value.Equals("Any", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return value.Trim();
        }

        private void UpdateActiveFiltersLabel(int shown, int total)
        {
            if (ActiveFiltersLabel == null)
            {
                return;
            }

            string sport = GetPickerValueOrEmpty(SportPicker);
            string team = GetPickerValueOrEmpty(TeamPicker);
            string set = GetPickerValueOrEmpty(SetPicker);

            List<string> chips = new List<string>();

            if (!string.IsNullOrWhiteSpace(sport)) chips.Add(sport);
            if (!string.IsNullOrWhiteSpace(team)) chips.Add(team);
            if (!string.IsNullOrWhiteSpace(set)) chips.Add(set);

            string prefix = string.Empty;
            if (shown >= 0 && total >= 0)
            {
                prefix = $"{shown}/{total} shown";
            }

            if (chips.Count == 0)
            {
                ActiveFiltersLabel.Text = prefix;
                return;
            }

            string filterText = string.Join(" · ", chips);
            ActiveFiltersLabel.Text = string.IsNullOrWhiteSpace(prefix)
                ? filterText
                : $"{prefix} · {filterText}";
        }

        // ============================================================
        //  EXISTING HANDLERS (KEEP BEHAVIOUR)
        // ============================================================

        private async void OnAddCardClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new EbaySearchPage());
        }

        private async void OnEditCard(object sender, EventArgs e)
        {
            if (sender is SwipeItem item && item.CommandParameter is Card card)
            {
                await Navigation.PushAsync(new CardPage(card));
            }
        }

        private async void OnDeleteCard(object sender, EventArgs e)
        {
            if (sender is SwipeItem item && item.CommandParameter is Card card)
            {
                bool confirm = await DisplayAlert(
                    "Delete Card",
                    "Are you sure you want to delete this card from your collection?",
                    "Delete",
                    "Cancel");

                if (!confirm)
                {
                    return;
                }

                await DeleteCardFromDatabaseAsync(card);

                await LoadCardsAsync();
            }
        }

        private async Task DeleteCardFromDatabaseAsync(Card card)
        {
            if (card == null || string.IsNullOrWhiteSpace(card.Id))
            {
                return;
            }

            // Prefer a strongly typed call if it exists in your IDatabase.
            // If the interface changes between iterations, reflection keeps this page compiling.
            try
            {
                MethodInfo? deleteCardAsync = database.GetType().GetMethod("DeleteCardAsync");
                if (deleteCardAsync != null)
                {
                    object? taskObj = deleteCardAsync.Invoke(database, new object[] { card.Id });
                    if (taskObj is Task task)
                    {
                        await task;
                        return;
                    }
                }
            }
            catch
            {
                // fall through to generic delete
            }

            try
            {
                MethodInfo? deleteGeneric = database.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "DeleteAsync" && m.IsGenericMethodDefinition);

                if (deleteGeneric != null)
                {
                    MethodInfo closed = deleteGeneric.MakeGenericMethod(typeof(Card));
                    object? taskObj = closed.Invoke(database, new object[] { card.Id });
                    if (taskObj is Task task)
                    {
                        await task;
                    }
                }
            }
            catch
            {
                // Last resort: do nothing (we already confirmed with user)
            }
        }

        private async void OnExportExcelClicked(object sender, EventArgs e)
        {
            try
            {
                IEnumerable<Card> Cards = CardsCollectionView.ItemsSource as List<Card>
                   ?? (CardsCollectionView.ItemsSource as IEnumerable<Card>)?.ToList()
                   ?? allCards.ToList();

                if (Cards == null)
                {
                    await DisplayAlert("Export", "No cards to export yet.", "OK");
                    return;
                }

                await DisplayAlert(
                    "Export",
                    "CollectIQ is preparing your Excel package in the background.\n\n" +
                    "You can keep using the app – you'll be asked where to share it " +
                    "as soon as it's ready.",
                    "OK");

                _ = RunExcelExportAndShareAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Export Error", ex.Message, "OK");
            }
        }

        private async Task RunExcelExportAndShareAsync()
        {
            try
            {
                var exportPath = await ExcelCollectionExportService.ExportAsync(
                      CardsCollectionView.ItemsSource as List<Card>
                    ?? (CardsCollectionView.ItemsSource as IEnumerable<Card>)?.ToList()
                    ?? allCards.ToList(),
                    FileSystem.CacheDirectory);

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await Share.Default.RequestAsync(new ShareFileRequest
                    {
                        Title = "Export Collection (Excel)",
                        File = new ShareFile(exportPath)
                    });
                });
            }
            catch (Exception ex)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert("Export Error", ex.Message, "OK");
                });
            }
        }

        private async void OnExportPdfClicked(object sender, EventArgs e)
        {
            try
            {
                IEnumerable<Card> Cards = CardsCollectionView.ItemsSource as List<Card>
                    ?? (CardsCollectionView.ItemsSource as IEnumerable<Card>)?.ToList()
                    ?? allCards.ToList();
                if (Cards == null )
                {
                    await DisplayAlert("Export", "No cards to export yet.", "OK");
                    return;
                }

                // Ask whether to include images
                bool includeImages = await DisplayAlert(
                    "PDF Export",
                    "Include images in the PDF?\n\nYes = larger/slower\nNo = smaller/faster",
                    "Yes",
                    "No");

                await DisplayAlert(
                    "Export",
                    "CollectIQ is preparing your PDF in the background.\n\n" +
                    "You can keep using the app – you'll be asked where to share it " +
                    "as soon as it's ready.",
                    "OK");

                _ = RunPdfExportAndShareAsync(includeImages);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Export Error", ex.Message, "OK");
            }
        }

        private async Task RunPdfExportAndShareAsync(bool includeImages)
        {
            try
            {
              
                var exportPath = await PdfCollectionExportService.ExportAsync(
                      CardsCollectionView.ItemsSource as List<Card>
                    ?? (CardsCollectionView.ItemsSource as IEnumerable<Card>)?.ToList()
                    ?? allCards.ToList(),
                    FileSystem.CacheDirectory,
                    includeImages);

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await Share.Default.RequestAsync(new ShareFileRequest
                    {
                        Title = "Export Collection (PDF)",
                        File = new ShareFile(exportPath)
                    });
                });
            }
            catch (Exception ex)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert("Export Error", ex.Message, "OK");
                });
            }
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

        // ============================================================
        //  UI HELPERS
        // ============================================================

        private void RefreshStats(decimal totalValue, int cardCount)
        {
            if (CollectionCountLabel != null)
            {
                CollectionCountLabel.Text = $"{cardCount} Cards";
            }

            if (CollectionValueLabel != null)
            {
                CollectionValueLabel.Text = $"Est. Total Value: ${totalValue:F2}";
            }
        }

        private static string GetSportName(Card card)
        {
            try
            {
                return card.SportName ?? card.Sport.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetTeamName(Card card)
        {
            if (card.Team != null && !string.IsNullOrWhiteSpace(card.Team.Name))
            {
                return card.Team.Name.Trim();
            }

            if (!string.IsNullOrWhiteSpace(card.TeamName))
            {
                return card.TeamName.Trim();
            }

            return string.Empty;
        }

        private static string TryGetDisplayName(Card card)
        {
            if (!string.IsNullOrWhiteSpace(card.DisplayName))
            {
                return card.DisplayName.Trim();
            }

            if (card.Player != null && !string.IsNullOrWhiteSpace(card.Player.FullName))
            {
                return card.Player.FullName.Trim();
            }

            return (card.Title ?? string.Empty).Trim();
        }
    }
}
