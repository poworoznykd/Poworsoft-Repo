using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using CommunityToolkit.Maui.Core;

namespace CollectIQ.Views
{
    public partial class ScanPage : ContentPage
    {
        private bool _isScanning = false;
        private bool _capturingBack = false;
        private string _frontImagePath = string.Empty;
        private string _backImagePath = string.Empty;

        public ScanPage()
        {
            InitializeComponent();
            AnimateScanLine();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            try
            {
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await CameraView.StartCameraPreview(cts.Token);
                _isScanning = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Camera start error: {ex.Message}");
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            try
            {
                _isScanning = false;
                CameraView.StopCameraPreview();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Camera stop error: {ex.Message}");
            }
        }

        private async void AnimateScanLine()
        {
            while (true)
            {
                if (_isScanning)
                {
                    await ScanLine.TranslateTo(0, 360, 1200, Easing.CubicInOut);
                    await ScanLine.TranslateTo(0, 0, 1200, Easing.CubicInOut);
                }
                await Task.Delay(50);
            }
        }

        private async void OnScanClicked(object sender, EventArgs e)
        {
            try
            {
                _isScanning = false;

                var captureCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var imageStream = await CameraView.CaptureImage(captureCts.Token);

                if (imageStream != null)
                {
                    string folderPath = Path.Combine(FileSystem.AppDataDirectory, "CardPhotos");
                    Directory.CreateDirectory(folderPath);

                    string fileName = _capturingBack ? $"card_back_{Guid.NewGuid()}.jpg" : $"card_front_{Guid.NewGuid()}.jpg";
                    string fullPath = Path.Combine(folderPath, fileName);

                    using (var fileStream = File.Create(fullPath))
                        await imageStream.CopyToAsync(fileStream);

                    if (!_capturingBack)
                    {
                        _frontImagePath = fullPath;
                        _capturingBack = true;
                        await DisplayAlert("Flip Card", "Now flip your card and capture the BACK side.", "OK");
                        _isScanning = true;
                        return;
                    }

                    _backImagePath = fullPath;
                    _capturingBack = false;

                    await DisplayAlert("Captured", "Both sides captured. Searching eBay...", "OK");

                    await Shell.Current.GoToAsync($"{nameof(EbaySearchPage)}?frontPath={Uri.EscapeDataString(_frontImagePath)}&backPath={Uri.EscapeDataString(_backImagePath)}");
                }

                _isScanning = true;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Capture failed: {ex.Message}", "OK");
                _isScanning = true;
            }
        }

        private async void OnAddManuallyClicked(object sender, EventArgs e)
        {
            await DisplayAlert("Manual Entry", "Manual card entry form coming soon.", "OK");
        }

        private void Camera_MediaCaptured(object sender, MediaCapturedEventArgs e)
        {
            Debug.WriteLine("Media captured successfully.");
        }
    }
}
