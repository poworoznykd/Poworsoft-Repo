/*
 * FILE            : DeveloperDatabasePage.xaml.cs
 * PROJECT         : CollectIQ (Mobile Application)
 * PROGRAMMER      : Darryl Poworoznyk
 * FIRST VERSION   : 2026-06-08
 * DESCRIPTION     :
 *     Provides a small local developer database view for CollectIQ. This page
 *     displays the SQLite database path, key table counts, and all local table
 *     names so the database foundation can be verified while the app evolves.
 */

using CollectIQ.Helpers;
using CollectIQ.Interfaces;
using SQLite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace CollectIQ.Views
{
    /// <summary>
    /// Displays local SQLite database diagnostics for development testing.
    /// </summary>
    public partial class DeveloperDatabasePage : ContentPage
    {
        #region Fields

        private readonly IDatabase database;
        private string databasePath = string.Empty;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="DeveloperDatabasePage"/> class.
        /// </summary>
        public DeveloperDatabasePage()
        {
            InitializeComponent();

            database = ServiceHelper.GetService<IDatabase>()
                ?? App.Database
                ?? throw new InvalidOperationException("IDatabase is not available.");
        }

        #endregion

        #region Page Events

        /// <summary>
        /// Loads database information when the page appears.
        /// </summary>
        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadDatabaseInfoAsync();
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Navigates back to the previous page.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The tap event arguments.</param>
        private async void OnBackClicked(object sender, TappedEventArgs e)
        {
            await GoBackAsync();
        }

        /// <summary>
        /// Copies the database path to the clipboard.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private async void OnCopyPathClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                await DisplayAlert("Database", "The database path is not loaded yet.", "OK");
                return;
            }

            await Clipboard.Default.SetTextAsync(databasePath);
            await DisplayAlert("Database", "Database path copied to the clipboard.", "OK");
        }

        /// <summary>
        /// Refreshes the database information.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private async void OnRefreshClicked(object sender, EventArgs e)
        {
            await LoadDatabaseInfoAsync();
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Returns to the previous page or shell route.
        /// </summary>
        private async Task GoBackAsync()
        {
            try
            {
                if (Navigation?.NavigationStack?.Count > 1)
                {
                    await Navigation.PopAsync();
                }
                else
                {
                    await Shell.Current.GoToAsync("..");
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Loads database path, key counts, and table information from SQLite.
        /// </summary>
        private async Task LoadDatabaseInfoAsync()
        {
            try
            {
                await database.InitializeAsync();
                SQLiteAsyncConnection connection = await database.GetConnectionAsync();

                databasePath = database.GetDatabasePath();
                DatabasePathLabel.Text = databasePath;

                UsersCountLabel.Text = (await GetTableCountAsync(connection, "UserAccount")).ToString();
                ProfilesCountLabel.Text = (await GetTableCountAsync(connection, "UserProfile")).ToString();
                CollectionsCountLabel.Text = (await GetTableCountAsync(connection, "CardCollection")).ToString();
                CardsCountLabel.Text = (await GetTableCountAsync(connection, "Card")).ToString();

                List<DatabaseTableInfo> tableInfos = await LoadTableInfosAsync(connection);
                TablesCollectionView.ItemsSource = tableInfos;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CollectIQ DB VIEW] Load failed: {ex}");
                await DisplayAlert("Database", $"Unable to load database information: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Gets a table row count if the table exists.
        /// </summary>
        /// <param name="connection">The SQLite connection.</param>
        /// <param name="tableName">The table name.</param>
        /// <returns>The row count.</returns>
        private static async Task<int> GetTableCountAsync(SQLiteAsyncConnection connection, string tableName)
        {
            try
            {
                return await connection.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM {tableName};");
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Loads table names and row counts from SQLite.
        /// </summary>
        /// <param name="connection">The SQLite connection.</param>
        /// <returns>The table information list.</returns>
        private static async Task<List<DatabaseTableInfo>> LoadTableInfosAsync(SQLiteAsyncConnection connection)
        {
            List<SqliteMasterRow> rows = await connection.QueryAsync<SqliteMasterRow>(
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;");

            List<DatabaseTableInfo> tableInfos = new List<DatabaseTableInfo>();

            foreach (SqliteMasterRow row in rows)
            {
                int rowCount = await GetTableCountAsync(connection, row.name);

                tableInfos.Add(new DatabaseTableInfo
                {
                    Name = row.name,
                    RowCount = rowCount
                });
            }

            return tableInfos;
        }

        #endregion

        #region Nested Types

        /// <summary>
        /// Represents a SQLite sqlite_master row.
        /// </summary>
        private sealed class SqliteMasterRow
        {
            public string name { get; set; } = string.Empty;
        }

        /// <summary>
        /// Represents table information displayed on the database page.
        /// </summary>
        private sealed class DatabaseTableInfo
        {
            public string Name { get; set; } = string.Empty;
            public int RowCount { get; set; }
        }

        #endregion
    }
}
