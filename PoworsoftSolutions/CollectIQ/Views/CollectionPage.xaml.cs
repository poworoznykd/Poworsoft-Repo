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
                    $"Are you sure you want to delete '{card.Name}'?", "Delete", "Cancel");

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
                await DisplayAlert("Edit Card",
                    $"Editing for '{card.Name}' coming soon!", "OK");
            }
        }

        /// <summary>
        /// Navigates to the Scan Page to add a new card.
        /// </summary>
        private async void OnAddCardClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(ScanPage));
        }
    }
}
