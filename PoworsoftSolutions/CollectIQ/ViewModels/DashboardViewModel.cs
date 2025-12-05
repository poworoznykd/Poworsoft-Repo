/*
 * FILE         : DashboardViewModel.cs
 * PROJECT      : CollectIQ (Mobile Application)
 * PROGRAMMER   : Darryl Poworoznyk
 * FIRST VERSION: 2025-12-04
 * DESCRIPTION  :
 *   View model for the Dashboard page.
 *   - Loads collection statistics from IDatabase
 *   - Responds to "CollectionUpdated" messages
 *   - Exposes commands for refresh and sport deal navigation
 */

using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using CollectIQ.Interfaces;
using Microsoft.Maui.Controls;

namespace CollectIQ.ViewModels
{
    /*
     * CLASS   : DashboardViewModel
     * PURPOSE :
     *   Encapsulates dashboard state and logic:
     *   - Card count / total / average value
     *   - "Last updated" label
     *   - Commands for refreshing and sport-deal navigation
     *   Depends only on abstractions (IDatabase, IBrowserService,
     *   IAlertService) to follow SOLID principles.
     */
    public class DashboardViewModel : INotifyPropertyChanged
    {
        // ------------------------------
        // Private Fields
        // ------------------------------
        private readonly IDatabase database;
        private readonly IBrowserService browserService;
        private readonly IAlertService alertService;

        private int cardsOwned;
        private decimal collectionValue;
        private decimal averageCardValue;
        private string lastUpdatedLabel = "Last updated: --";
        private bool isBusy;

        // ------------------------------
        // Public Properties
        // ------------------------------
        public int CardsOwned
        {
            get { return cardsOwned; }
            private set
            {
                if (cardsOwned == value) return;
                cardsOwned = value;
                OnPropertyChanged();
            }
        }

        public decimal CollectionValue
        {
            get { return collectionValue; }
            private set
            {
                if (collectionValue == value) return;
                collectionValue = value;
                OnPropertyChanged();
            }
        }

        public decimal AverageCardValue
        {
            get { return averageCardValue; }
            private set
            {
                if (averageCardValue == value) return;
                averageCardValue = value;
                OnPropertyChanged();
            }
        }

        public string LastUpdatedLabel
        {
            get { return lastUpdatedLabel; }
            private set
            {
                if (string.Equals(lastUpdatedLabel, value, StringComparison.Ordinal))
                {
                    return;
                }

                lastUpdatedLabel = value;
                OnPropertyChanged();
            }
        }

        public bool IsBusy
        {
            get { return isBusy; }
            private set
            {
                if (isBusy == value) return;
                isBusy = value;
                OnPropertyChanged();
                (RefreshCommand as Command)?.ChangeCanExecute();
            }
        }

        // ------------------------------
        // Commands
        // ------------------------------
        public ICommand RefreshCommand { get; }

        /// <summary>
        /// Command triggered from the "Deals by Sport" tiles.
        /// CommandParameter is the sport code (e.g., "NFL").
        /// </summary>
        public ICommand OpenSportDealsCommand { get; }

        // ------------------------------
        // Constructor
        // ------------------------------
        public DashboardViewModel(
            IDatabase databaseParam,
            IBrowserService browserServiceParam,
            IAlertService alertServiceParam)
        {
            database = databaseParam ?? throw new ArgumentNullException(nameof(databaseParam));
            browserService = browserServiceParam ?? throw new ArgumentNullException(nameof(browserServiceParam));
            alertService = alertServiceParam ?? throw new ArgumentNullException(nameof(alertServiceParam));

            RefreshCommand = new Command(
                async () => await LoadCollectionStatsAsync(),
                () => !IsBusy);

            OpenSportDealsCommand = new Command<string>(
                async sportCode => await OpenSportDealsAsync(sportCode));

            // Refresh when the collection changes anywhere in the app.
            MessagingCenter.Subscribe<object>(
                this,
                "CollectionUpdated",
                async sender => { await LoadCollectionStatsAsync(); });
        }

        // ------------------------------
        // Public Methods
        // ------------------------------
        //
        // FUNCTION     : InitializeAsync
        // DESCRIPTION  :
        //   Called by the view when the page appears so that
        //   dashboard metrics are populated on first load.
        // RETURNS      : Task
        //
        public async Task InitializeAsync()
        {
            await LoadCollectionStatsAsync();
        }

        // ------------------------------
        // Private Methods
        // ------------------------------
        //
        // FUNCTION     : LoadCollectionStatsAsync
        // DESCRIPTION  :
        //   Retrieves all cards from the database and calculates:
        //   - CardsOwned
        //   - CollectionValue
        //   - AverageCardValue
        //   Updates LastUpdatedLabel on success.
        // RETURNS      : Task
        //
        private async Task LoadCollectionStatsAsync()
        {
            if (IsBusy)
            {
                return;
            }

            try
            {
                IsBusy = true;

                var cardList = await database.GetAllCardsAsync();

                CardsOwned = cardList.Count;
                CollectionValue = Convert.ToDecimal(
                    cardList.Sum(card => Convert.ToDouble(card.EstimatedValue)));

                AverageCardValue = CardsOwned > 0
                    ? Math.Round(CollectionValue / CardsOwned, 2)
                    : 0m;

                LastUpdatedLabel = $"Last updated: {DateTime.Now:MMM dd, yyyy HH:mm}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[DashboardViewModel] Error loading stats: {ex.Message}");

                CardsOwned = 0;
                CollectionValue = 0;
                AverageCardValue = 0;
                LastUpdatedLabel = "Last updated: --";
            }
            finally
            {
                IsBusy = false;
            }
        }

        //
        // FUNCTION     : OpenSportDealsAsync
        // DESCRIPTION  :
        //   Builds an eBay search query for the specified sport and
        //   opens it via IBrowserService. If the sport is not yet
        //   supported, shows a friendly alert.
        // PARAMETERS   :
        //   string sportCode : Sport code such as "NFL", "NBA".
        // RETURNS      : Task
        //
        private async Task OpenSportDealsAsync(string sportCode)
        {
            if (string.IsNullOrWhiteSpace(sportCode))
            {
                return;
            }

            string query = BuildSportQuery(sportCode);

            if (string.IsNullOrWhiteSpace(query))
            {
                await alertService.ShowMessageAsync(
                    "Deals",
                    "Deals for this sport are coming soon.",
                    "OK");
                return;
            }

            string url =
                $"https://www.ebay.com/sch/i.html?_nkw={Uri.EscapeDataString(query)}&LH_Sold=0&LH_BIN=1";

            await browserService.OpenAsync(url);
        }

        //
        // FUNCTION     : BuildSportQuery
        // DESCRIPTION  :
        //   Maps a short sport code to a richer search phrase for
        //   curated eBay deals.
        // PARAMETERS   :
        //   string sportCode : Abbreviated sport (NFL, NBA, etc.).
        // RETURNS      :
        //   string: Search phrase or empty string if unsupported.
        //
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
        // INotifyPropertyChanged
        // ------------------------------
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChangedEventHandler? handler = PropertyChanged;

            if (handler != null && propertyName != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
