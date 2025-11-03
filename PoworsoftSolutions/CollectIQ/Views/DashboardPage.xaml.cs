/*
 * FILE         : DashboardPage.xaml.cs
 * PROJECT      : CollectIQ
 * PROGRAMMER   : Darryl Poworoznyk
 * FIRST VERSION: 2025-10-29
 * DESCRIPTION  :
 *   Implements dashboard logic, including total card count
 *   and estimated collection value, with real-time updates.
 */

using Microsoft.Maui.Controls;
using System;
using System.Linq;
using System.Threading.Tasks;
using CollectIQ.Services;

namespace CollectIQ.Views
{
    public partial class DashboardPage : ContentPage
    {
        // ------------------------------
        // Private Fields
        // ------------------------------
        private readonly SqliteDatabase database = new();
        private int cardsOwned;
        private decimal collectionValue;

        // ------------------------------
        // Public Properties
        // ------------------------------
        public int CardsOwned
        {
            get { return cardsOwned; }
            set
            {
                if (cardsOwned != value)
                {
                    cardsOwned = value;
                    OnPropertyChanged(); // Uses ContentPage.OnPropertyChanged()
                }
            }
        }

        public decimal CollectionValue
        {
            get { return collectionValue; }
            set
            {
                if (collectionValue != value)
                {
                    collectionValue = value;
                    OnPropertyChanged();
                }
            }
        }

        // ------------------------------
        // Constructor
        // ------------------------------
        public DashboardPage()
        {
            InitializeComponent();
            BindingContext = this;

            // Subscribe for collection updates (add/delete)
            MessagingCenter.Subscribe<object>(this, "CollectionUpdated", async (sender) =>
            {
                await LoadCollectionStatsAsync();
            });
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadCollectionStatsAsync();
        }

        // --------------------------------------------------
        // FUNCTION     : LoadCollectionStatsAsync
        // DESCRIPTION  :
        //   Loads all cards from the SQLite database and updates
        //   dashboard totals for card count and total value.
        // RETURNS      : Task (async)
        // --------------------------------------------------
        private async Task LoadCollectionStatsAsync()
        {
            try
            {
                var cardList = await database.GetAllCardsAsync();

                // Safe decimal conversion for consistency
                CardsOwned = cardList.Count;
                CollectionValue = Convert.ToDecimal(cardList.Sum(c => Convert.ToDouble(c.EstimatedValue)));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DashboardPage] Error loading stats: {ex.Message}");
                CardsOwned = 0;
                CollectionValue = 0;
            }
        }

        // ------------------------------
        // Navigation Commands
        // ------------------------------
        public Command ScanCardCommand => new(async () =>
            await Shell.Current.GoToAsync(nameof(ScanPage)));

        public Command EbayCompareCommand => new(async () =>
            await Shell.Current.GoToAsync(nameof(EbaySearchPage)));

        public Command ViewCollectionCommand => new(async () =>
            await Shell.Current.GoToAsync(nameof(CollectionPage)));
    }
}
