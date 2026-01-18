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

// Needed for FileSystem + Share + Path/File
using System.IO;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel.DataTransfer;

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

        #region Exporting
        // Export button click
        private async void OnExportExcelClicked(object sender, EventArgs e)
        {
            try
            {
                if (Cards == null || Cards.Count == 0)
                {
                    await DisplayAlert("Export", "No cards to export yet.", "OK");
                    return;
                }

                // Ask whether to include images (important BEFORE we start the background task)
                string action = await DisplayActionSheet(
                    "Excel Export",
                    "Cancel",
                    null,
                    "Export WITH images (larger)",
                    "Export data ONLY (smaller)");

                if (string.IsNullOrWhiteSpace(action) || action == "Cancel")
                {
                    return;
                }

                bool includeImages = action.StartsWith("Export WITH", StringComparison.Ordinal);

                // Tell the user what's happening, but don't block the app afterwards
                await DisplayAlert(
                    "Export",
                    "CollectIQ is preparing your Excel package in the background.\n\n" +
                    "You can keep using the app – you'll be asked where to share it " +
                    "as soon as it's ready.",
                    "OK");

                // Fire-and-forget: run the heavy work + share in the background
                _ = RunExcelExportAndShareAsync(includeImages);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Export Error", ex.Message, "OK");
            }
        }

        // Background helper that does the actual export, then notifies the user
        private async Task RunExcelExportAndShareAsync(bool includeImages)
        {
            try
            {
                // Use cache directory for the temporary Excel file (same as before)
                var exportPath = await ExcelCollectionExportService.ExportAsync(
                    Cards,
                    FileSystem.CacheDirectory,
                    includeImages);

                // Back to the UI thread for the Share sheet
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await Share.Default.RequestAsync(new ShareFileRequest
                    {
                        Title = includeImages
                            ? "Export Collection (Excel) - With Images"
                            : "Export Collection (Excel) - Data Only",
                        File = new ShareFile(exportPath)
                    });
                });
            }
            catch (Exception ex)
            {
                // Make sure any error is shown on the UI thread
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert("Export Error", ex.Message, "OK");
                });
            }
        }


        private async void OnExportPdfClicked(object sender, EventArgs e)
        {
            // Placeholder so the button does not break anything.
            // You can later plug in a real PDF generator (Syncfusion, QuestPDF, etc.)
            await DisplayAlert(
                "Export to PDF",
                "PDF export is not wired up yet. Excel export is available now, and PDF can be added next.",
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
