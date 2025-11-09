/*
* FILE: CardPage.xaml.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-28
* UPDATED: 2025-10-29
* DESCRIPTION:
*     Handles add/edit card functionality including photo capture,
*     OCR parsing, eBay lookup, and persistence to SQLite.
*     Implements full SET Coding Standards Rev 1.11.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Maui.Controls;
using CollectIQ.Services;
using CollectIQ.Models;
using CollectIQ.Utilities;

namespace CollectIQ.Views
{
    public partial class CardPage : ContentPage
    {
        // === Fields ===
        private readonly SqliteDatabase database;
        private Card? currentCard;
        private string? frontPath;
        private string? backPath;

        // === Constructor ===
        /// <summary>
        /// Initializes a new instance of the <see cref="CardPage"/> class.
        /// </summary>
        /// <param name="existing">Optional existing card to edit.</param>
        public CardPage(Card? existing = null)
        {
            InitializeComponent();
            database = new SqliteDatabase();

            if (existing != null)
                LoadExistingCard(existing);
        }

        // === Helper Methods ===
        /// <summary>
        /// Loads an existing card's details into the input fields for editing.
        /// </summary>
        /// <param name="card">The existing card to edit.</param>
        private void LoadExistingCard(Card card)
        {
            try
            {
                currentCard = card;
                PlayerEntry.Text = card.Player;
                YearEntry.Text = card.Year.ToString();
                SetEntry.Text = card.Set;
                NumberEntry.Text = card.Number;
                TeamEntry.Text = card.Team;
                GradeCoEntry.Text = card.GradeCompany;
                GradeEntry.Text = card.Grade?.ToString();
                PriceEntry.Text = card.PurchasePrice?.ToString();

                frontPath = card.FrontImagePath;
                backPath = card.BackImagePath;

                if (!string.IsNullOrWhiteSpace(frontPath))
                    FrontImagePreview.Source = ImageSource.FromFile(frontPath);

                if (!string.IsNullOrWhiteSpace(backPath))
                    BackImagePreview.Source = ImageSource.FromFile(backPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CardPage] Failed to load card: {ex.Message}");
            }
        }

        // === Navigation ===
        /// <summary>
        /// Handles navigation parameters from ScanPage or manual entry.
        /// Populates image previews and OCR-derived eBay search text.
        /// </summary>
        protected override void OnNavigatedTo(NavigatedToEventArgs args)
        {
            base.OnNavigatedTo(args);

            try
            {
                if (Shell.Current?.CurrentState is not null &&
                    Shell.Current.CurrentState.Location.OriginalString.Contains("?"))
                {
                    var query = System.Web.HttpUtility.ParseQueryString(
                        Shell.Current.CurrentState.Location.Query);

                    string? front = query["FrontPath"];
                    string? back = query["BackPath"];
                    string? parsed = query["Parsed"];

                    if (!string.IsNullOrWhiteSpace(front))
                    {
                        frontPath = Uri.UnescapeDataString(front);
                        FrontImagePreview.Source = ImageSource.FromFile(frontPath);
                    }

                    if (!string.IsNullOrWhiteSpace(back))
                    {
                        backPath = Uri.UnescapeDataString(back);
                        BackImagePreview.Source = ImageSource.FromFile(backPath);
                    }

                    if (!string.IsNullOrWhiteSpace(parsed))
                        EbayQueryEntry.Text = Uri.UnescapeDataString(parsed);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CardPage] Navigation parameter handling error: {ex.Message}");
            }
        }

        #region Event Handlers

        /// <summary>
        /// Opens ScanPage to capture card photos and perform OCR.
        /// After completion, returns here with image paths.
        /// </summary>
        private async void OnTakePhotos(object sender, EventArgs e)
        {
            try
            {
                await Shell.Current.Navigation.PushAsync(new ScanPage(nameof(CardPage)));
                frontPath = currentCard?.FrontImagePath;
                FrontImagePreview.Source = ImageSource.FromFile(frontPath);
                backPath = currentCard?.BackImagePath;
                BackImagePreview.Source = ImageSource.FromFile(backPath);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Unable to open camera: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Allows the user to manually pick front and back photos from device storage.
        /// Automatically runs OCR and fills eBay query text.
        /// </summary>
        private async void OnPickPhotos(object sender, EventArgs e)
        {
            try
            {
                var frontPick = await FilePicker.PickAsync();
                if (frontPick != null)
                {
                    frontPath = frontPick.FullPath;
                    FrontImagePreview.Source = ImageSource.FromFile(frontPath);
                }

                var backPick = await FilePicker.PickAsync();
                if (backPick != null)
                {
                    backPath = backPick.FullPath;
                    BackImagePreview.Source = ImageSource.FromFile(backPath);
                }

                if (!string.IsNullOrWhiteSpace(frontPath) && !string.IsNullOrWhiteSpace(backPath))
                {
                    string? rawText = await OCRUtility.ExtractTextFromFrontAndBackAsync(frontPath, backPath);
                    string? cleaned = await OCRUtility.SanitizeForEbay(rawText ?? string.Empty);
                    EbayQueryEntry.Text = cleaned ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Photo selection failed: {ex.Message}", "OK");
            }
        }

        private async void OnFrontImageTapped(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(frontPath))
                await Navigation.PushModalAsync(new ImageViewerPage(frontPath));
        }

        private async void OnBackImageTapped(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(backPath))
                await Navigation.PushModalAsync(new ImageViewerPage(backPath));
        }


        /// <summary>
        /// Performs an eBay search using the current query field.
        /// </summary>
        private async void OnFindEbay(object sender, EventArgs e)
        {
            try
            {
                string query = EbayQueryEntry.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(query))
                {
                    await DisplayAlert("Missing Query", "Please enter or scan a description first.", "OK");
                    return;
                }

                await Shell.Current.GoToAsync($"{nameof(EbaySearchPage)}?query={Uri.EscapeDataString(query)}");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Unable to search eBay: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Placeholder event for future eBay integration to populate card details.
        /// </summary>
        private async void OnUseEbayImage(object sender, EventArgs e)
        {
            await DisplayAlert("Info", "Feature under development: auto-populate from eBay match.", "OK");
        }

        /// <summary>
        /// Saves the current card entry to the SQLite collection database.
        /// Updates the record if it already exists.
        /// </summary>
        private async void OnSave(object sender, EventArgs e)
        {
            try
            {
                var card = currentCard ?? new Card();

                card.Player = PlayerEntry.Text?.Trim() ?? string.Empty;
                card.Year = int.TryParse(YearEntry.Text, out int y) ? y : 0;
                card.Set = SetEntry.Text?.Trim() ?? string.Empty;
                card.Number = NumberEntry.Text?.Trim() ?? string.Empty;
                card.Team = TeamEntry.Text?.Trim() ?? string.Empty;
                card.GradeCompany = GradeCoEntry.Text?.Trim() ?? string.Empty;
                card.Grade = double.TryParse(GradeEntry.Text, out double g) ? g : null;
                card.PurchasePrice = decimal.TryParse(PriceEntry.Text, out decimal p) ? p : null;

                card.FrontImagePath = currentCard?.FrontImagePath ?? string.Empty;
                card.BackImagePath = currentCard?.BackImagePath ?? string.Empty;

                if (currentCard != null)
                    await database.UpdateCardAsync(card);
                else
                    await database.AddCardAsync(card);

                await DisplayAlert("Success", "Card saved to collection.", "OK");
                await Shell.Current.GoToAsync("..");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Unable to save card: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Delete operation placeholder.
        /// </summary>
        private async void OnDelete(object sender, EventArgs e)
        {
            await DisplayAlert("Notice", "Delete functionality coming soon.", "OK");
        }

        #endregion

        private void ContentPage_Appearing(object sender, EventArgs e)
        {
            base.OnAppearing();

            var cachedResult = NavigationCache.Get<Dictionary<string, string>>(nameof(CardPage));

            if (cachedResult != null)
            {
                if (cachedResult.TryGetValue("FrontPath", out var front))
                {
                    currentCard.FrontImagePath = front;
                    FrontImagePreview.Source = ImageSource.FromFile(front);
                }

                if (cachedResult.TryGetValue("BackPath", out var back))
                {
                    currentCard.BackImagePath = back;
                    BackImagePreview.Source = ImageSource.FromFile(back);
                }

                // Clear after use
                NavigationCache.Clear(nameof(CardPage));
            }
        }
    }
}
