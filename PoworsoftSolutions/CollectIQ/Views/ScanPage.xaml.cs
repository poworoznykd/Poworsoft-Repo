/*
* FILE: ScanPage.xaml.cs
* PROJECT: CollectIQ
* PROGRAMMER: Darryl Poworoznyk
* UPDATED VERSION: 2025-10-28
* DESCRIPTION:
*     Controls the ZXing camera preview and mock card recognition.
*/

using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;
using CollectIQ.Services;
using CollectIQ.Models;
using System;

namespace CollectIQ.Views
{
    public partial class ScanPage : ContentPage
    {
        private readonly SqliteDatabase _database = new SqliteDatabase();

        public ScanPage()
        {
            InitializeComponent();
        }

        private void OnBarcodesDetected(object sender, BarcodeDetectionEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (e.Results?.Any() == true)
                {
                    var result = e.Results.First().Value;
                    await DisplayAlert("Scanned", $"Detected: {result}", "OK");
                }
            });
        }
        private async void OnCaptureClicked(object sender, EventArgs e)
        {
            try
            {
                StatusLabel.Text = "Processing card...";

                // Simulated recognized card
                var card = new Card
                {
                    Id = Guid.NewGuid().ToString(),
                    Player = "Patrick Mahomes",
                    Team = "Kansas City Chiefs",
                    Year = 2017,
                    Set = "Panini Prizm",
                    Number = "PM-RC",
                    Name = "Patrick Mahomes Rookie Card",
                    GradeCompany = "Raw",
                    PhotoPath = string.Empty
                };

                await _database.AddCardAsync(card);

                StatusLabel.Text = $"Added: {card.Name}";
                await DisplayAlert("Success",
                    $"Saved {card.Name} to your collection.",
                    "OK");

                await Navigation.PushAsync(new EbaySearchPage());
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }
    }
}
