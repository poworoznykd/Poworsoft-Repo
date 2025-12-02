/*
* FILE: CollectionPage.xaml.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-25
* DESCRIPTION:
*     Provides logic for displaying, deleting, and editing sports cards
*     within the user’s collection. Integrates swipe actions, smooth
*     animations, and SQLite persistence for a premium user experience.
*/

using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CollectIQ.Models;
using CollectIQ.Services;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;

namespace CollectIQ.Views
{
    public partial class CollectionPage : ContentPage
    {
        private readonly SqliteDatabase _database = new();
        public ObservableCollection<Card> Cards { get; } = new();

        public CollectionPage()
        {
            InitializeComponent();
            BindingContext = this;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadCardsAsync();
        }

        /// <summary>
        /// Loads all cards from the SQLite database into the CollectionView.
        /// </summary>
        private async Task LoadCardsAsync()
        {
            try
            {
                await _database.InitializeAsync();
                Cards.Clear();

                var cards = await _database.GetAllCardsAsync();
                foreach (var card in cards)
                    Cards.Add(card);

                EmptyMessage.IsVisible = Cards.Count == 0;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to load collection: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Handles deletion of a selected card from the local collection.
        /// </summary>
        private async void OnDeleteCard(object sender, EventArgs e)
        {
            if (sender is SwipeItem swipe && swipe.CommandParameter is Card card)
            {
                bool confirm = await DisplayAlert("Confirm Delete",
                    $"Are you sure you want to delete '{card.Title}'?", "Delete", "Cancel");

                if (!confirm) return;

                try
                {
                    await _database.DeleteCardAsync(card.Id);
                    Cards.Remove(card);
                    EmptyMessage.IsVisible = Cards.Count == 0;

                    await Toast.Make("Card deleted.", ToastDuration.Short).Show();
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"Failed to delete card: {ex.Message}", "OK");
                }
            }
        }

        /// <summary>
        /// Placeholder for future edit functionality.
        /// </summary>
        private async void OnEditCard(object sender, EventArgs e)
        {
            if (sender is SwipeItem swipe && swipe.CommandParameter is Card card)
            {
                await Navigation.PushAsync(new CardPage(card));
            }
        }

        /// <summary>
        /// FUNCTION: OnAddCardClicked
        /// DESCRIPTION:
        ///     Switches to the ScanPage tab to allow the user to scan or add a new card.
        /// PARAMETERS:
        ///     sender – Source of the event.
        ///     e – Event arguments.
        /// RETURNS:
        ///     None.
        /// </summary>
        private async void OnAddCardClicked(object sender, EventArgs e)
        {
            try
            {
                // Switch tab to ScanPage (ShellContent route)
                await Shell.Current.GoToAsync("//ScanPage");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CollectionPage] Tab navigation error: {ex.Message}");
                await DisplayAlert("Navigation Error", "Unable to switch to Scan tab.", "OK");
            }
        }

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
