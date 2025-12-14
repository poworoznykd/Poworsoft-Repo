/*
 * FILE         : DashboardPage.xaml.cs
 * PROJECT      : CollectIQ
 * PROGRAMMER   : Darryl Poworoznyk
 * FIRST VERSION: 2025-10-29
 * DESCRIPTION  :
 *   Implements dashboard logic, including total card count
 *   and estimated collection value, with real-time updates.
 */

using System;
using System.Linq;
using System.Threading.Tasks;
using CollectIQ.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

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
        private decimal averageCardValue;
        private string lastUpdatedLabel = string.Empty;

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
                    OnPropertyChanged();
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

        public decimal AverageCardValue
        {
            get { return averageCardValue; }
            set
            {
                if (averageCardValue != value)
                {
                    averageCardValue = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LastUpdatedLabel
        {
            get { return lastUpdatedLabel; }
            set
            {
                if (!string.Equals(lastUpdatedLabel, value, StringComparison.Ordinal))
                {
                    lastUpdatedLabel = value;
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

                CardsOwned = cardList.Count;
                CollectionValue = Convert.ToDecimal(
                    cardList.Sum(c => Convert.ToDouble(c.EstimatedValue)));

                AverageCardValue = CardsOwned > 0
                    ? Math.Round(CollectionValue / CardsOwned, 2)
                    : 0m;

                LastUpdatedLabel = $"Last updated: {DateTime.Now:MMM dd, yyyy HH:mm}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DashboardPage] Error loading stats: {ex.Message}");
                CardsOwned = 0;
                CollectionValue = 0;
                AverageCardValue = 0;
                LastUpdatedLabel = "Last updated: --";
            }
        }

        // Handles taps on any of the "Deals by Sport" tiles.
        private async void OnSportDealsTapped(object sender, TappedEventArgs e)
        {
            try
            {
                string sport = e?.Parameter as string ?? string.Empty;
                if (string.IsNullOrWhiteSpace(sport))
                {
                    return;
                }

                string query = BuildSportQuery(sport);
                if (string.IsNullOrWhiteSpace(query))
                {
                    await DisplayAlert("Deals",
                        "Deals for this sport are coming soon.",
                        "OK");
                    return;
                }

                string url =
                    $"https://www.ebay.com/sch/i.html?_nkw={Uri.EscapeDataString(query)}&LH_Sold=0&LH_BIN=1";

                await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error",
                    $"Unable to open deals right now: {ex.Message}",
                    "OK");
            }
        }

        // Builds a sport-specific search phrase for eBay.
        private static string BuildSportQuery(string sportCode)
        {
            switch (sportCode.ToUpperInvariant())
            {
                case "NFL":
                    return "NFL football trading card lot rookie psa bgs sgc";
                case "NBA":
                    return "NBA basketball trading card rookie silver holo psa sgc";
                case "NHL":
                    return "NHL hockey young guns rookie card lot";
                case "MLB":
                    return "MLB baseball rookie prospect chrome bowman";
                case "NCAA":
                    return "NCAA college rookie trading card lot";
                case "WNBA":
                    return "WNBA basketball rookie trading card lot";
                case "PGA":
                    return "golf pga tour trading card rookie";
                default:
                    return string.Empty;
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
