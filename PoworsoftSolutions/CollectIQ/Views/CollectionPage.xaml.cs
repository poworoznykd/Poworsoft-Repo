//
//  FILE            : CollectionPage.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-26
//  DESCRIPTION     :
//      Displays the user's stored card collection. This page automatically
//      refreshes data each time it appears and uses SqliteDatabase to
//      retrieve saved card records.
//

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CollectIQ.Models;
using CollectIQ.Services;
using Microsoft.Maui.Controls;

namespace CollectIQ.Views
{
    public partial class CollectionPage : ContentPage
    {
        private readonly SqliteDatabase _database = new();

        public CollectionPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Refreshes the collection list every time the page becomes visible.
        /// </summary>
        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadCardsAsync();
        }

        /// <summary>
        /// Loads all cards from the SQLite database.
        /// </summary>
        private async Task LoadCardsAsync()
        {
            try
            {
                await _database.InitializeAsync();
                List<Card> cards = await _database.GetAllCardsAsync();

                if (cards.Count == 0)
                {
                    await DisplayAlert("Collection Empty", "No cards have been added yet.", "OK");
                }

                // Assumes you have a CollectionView named 'CardsCollectionView' in XAML
                CardsCollectionView.ItemsSource = cards;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to load cards: {ex.Message}", "OK");
            }
        }
    }
}
