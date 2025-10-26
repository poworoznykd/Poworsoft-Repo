/*
* FILE: ScanPage.xaml.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-25
* DESCRIPTION:
*     Handles camera capture, invokes recognition, and
*     navigates to eBay search results after saving card data.
*/

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CollectIQ.Models;
using CollectIQ.Services;

namespace CollectIQ.Views
{
    public partial class ScanPage : ContentPage
    {
        private readonly CardRecognitionService _recognitionService = new();
        private readonly SqliteDatabase _database = new();
        private readonly EbayService _ebayService = new(new HttpClient());

        public Command ScanCommand { get; }

        public ScanPage()
        {
            InitializeComponent();
            ScanCommand = new Command(async () => await CaptureAndRecognizeAsync());
            BindingContext = this;
        }

        /// <summary>
        /// Captures a photo, recognizes the card, saves locally, and navigates to eBay search.
        /// </summary>
        private async Task CaptureAndRecognizeAsync()
        {
            try
            {
                var photo = await MediaPicker.CapturePhotoAsync();
                if (photo == null)
                    return;

                string filePath = Path.Combine(FileSystem.CacheDirectory, $"{Guid.NewGuid()}.jpg");
                await using (var src = await photo.OpenReadAsync())
                await using (var dst = File.OpenWrite(filePath))
                    await src.CopyToAsync(dst);

                PreviewImage.Source = ImageSource.FromFile(filePath);

                var recognizedCard = await _recognitionService.RecognizeCardAsync(filePath);
                if (recognizedCard == null)
                {
                    await DisplayAlert("Recognition Failed", "Could not identify this card.", "OK");
                    return;
                }

                recognizedCard.Id = Guid.NewGuid().ToString();
                recognizedCard.PhotoPath = filePath;

                ResultLabel.Text = $"{recognizedCard.Year} {recognizedCard.Player} – {recognizedCard.Set}";

                await _database.InitializeAsync();
                await _database.AddCardAsync(recognizedCard);

                await _database.AddCardImageAsync(new CardImage
                {
                    Id = Guid.NewGuid().ToString(),
                    CardId = recognizedCard.Id,
                    Path = filePath,
                    Kind = "front"
                });

                await Toast.Make("✅ Card saved to collection.", ToastDuration.Short).Show();

                // Navigate to eBay results with player name
                await Shell.Current.GoToAsync(nameof(EbaySearchPage),
                    new Dictionary<string, object> { { "initialQuery", recognizedCard.Player } });
            }
            catch (FeatureNotSupportedException)
            {
                await DisplayAlert("Unsupported", "Camera not supported on this device.", "OK");
            }
            catch (PermissionException)
            {
                await DisplayAlert("Permission Denied", "Camera permission is required.", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Unexpected error: {ex.Message}", "OK");
            }
        }
    }
}
