/*
* FILE: ScanPage.xaml.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-25
* DESCRIPTION:
*     Handles camera capture, invokes the recognition service,
*     and stores recognized card data and image references locally
*     in the SQLite database using SqliteDatabase service.
*     Includes visual scanning overlay animation during recognition.
*/

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CollectIQ.Models;
using CollectIQ.Services;

namespace CollectIQ.Views
{
    /// <summary>
    /// Page responsible for capturing and recognizing collectible cards.
    /// </summary>
    public partial class ScanPage : ContentPage
    {
        // === Private Service References ===
        private readonly CardRecognitionService _recognitionService = new();
        private readonly SqliteDatabase _database = new();

        // === Private Animation Control ===
        private CancellationTokenSource? _animationTokenSource;

        /// <summary>
        /// Command bound to the Scan button.
        /// </summary>
        public Command ScanCommand { get; }

        /// <summary>
        /// Initializes page components and bindings.
        /// </summary>
        public ScanPage()
        {
            InitializeComponent();
            ScanCommand = new Command(async () => await CaptureAndRecognizeAsync());
            BindingContext = this;
        }

        /// <summary>
        /// Captures an image from the camera, performs recognition,
        /// displays results, and saves both card and image data locally.
        /// </summary>
        private async Task CaptureAndRecognizeAsync()
        {
            try
            {
                // Request a camera photo
                var photo = await MediaPicker.CapturePhotoAsync();
                if (photo == null)
                    return;

                // Save photo to app cache folder
                string filePath = Path.Combine(FileSystem.CacheDirectory, $"{Guid.NewGuid()}.jpg");
                await using (var src = await photo.OpenReadAsync())
                await using (var dst = File.OpenWrite(filePath))
                    await src.CopyToAsync(dst);

                // Update UI preview
                PreviewImage.Source = ImageSource.FromFile(filePath);
                ResultLabel.Text = "🔍 Scanning card...";

                // === Begin scanning animation ===
                ScanOverlay.IsVisible = true;
                _animationTokenSource = new CancellationTokenSource();
                _ = AnimateScanLineAsync(_animationTokenSource.Token);

                // === Perform recognition ===
                var recognizedCard = await _recognitionService.RecognizeCardAsync(filePath);

                // Stop overlay animation
                _animationTokenSource?.Cancel();
                ScanOverlay.IsVisible = false;

                if (recognizedCard == null)
                {
                    ResultLabel.Text = "❌ Unable to identify card.";
                    await DisplayAlert("Recognition Failed", "Unable to identify this card.", "OK");
                    return;
                }

                // Assign unique ID and store local photo path
                recognizedCard.Id = Guid.NewGuid().ToString();
                recognizedCard.PhotoPath = filePath;

                // Display detected card info
                ResultLabel.Text = $"{recognizedCard.Year} {recognizedCard.Player} – {recognizedCard.Set}";

                // Initialize DB if needed
                await _database.InitializeAsync();

                // Save recognized card
                int rows = await _database.AddCardAsync(recognizedCard);
                if (rows > 0)
                {
                    // Save corresponding image reference
                    await _database.AddCardImageAsync(new CardImage
                    {
                        Id = Guid.NewGuid().ToString(),
                        CardId = recognizedCard.Id,
                        Path = filePath,
                        Kind = "front"
                    });

                    await Toast.Make("✅ Card saved to collection.", ToastDuration.Short).Show();
                }
                else
                {
                    await DisplayAlert("Database Error", "Card could not be saved.", "OK");
                }
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
                _animationTokenSource?.Cancel();
                ScanOverlay.IsVisible = false;
                await DisplayAlert("Error", $"Unexpected error: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Animates a neon scanning line across the preview frame for visual feedback.
        /// </summary>
        private async Task AnimateScanLineAsync(CancellationToken token)
        {
            try
            {
                const double frameHeight = 260;  // Match Frame height in XAML
                const double lineHeight = 3;
                double lineStart = 0;
                double lineEnd = frameHeight - lineHeight;

                while (!token.IsCancellationRequested)
                {
                    // Move down
                    await ScanLine.TranslateTo(0, lineEnd, 1600, Easing.Linear);
                    // Fade down slightly
                    await ScanLine.FadeTo(0.3, 200);
                    // Reset to top instantly
                    ScanLine.TranslationY = 0;
                    ScanLine.Opacity = 0.8;
                }
            }
            catch (TaskCanceledException)
            {
                // normal — stop animation quietly
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Scan Animation Error] {ex.Message}");
            }
        }

        /// <summary>
        /// Ensures any running animation is stopped when page is unloaded.
        /// </summary>
        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _animationTokenSource?.Cancel();
            ScanOverlay.IsVisible = false;
        }
    }
}
