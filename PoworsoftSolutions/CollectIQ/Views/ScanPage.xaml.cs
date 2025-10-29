/*
* FILE: ScanPage.xaml.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-18
* UPDATED: 2025-10-28
* DESCRIPTION:
*     Handles live camera scanning of card front and back, saves images,
*     and passes them to the eBay search workflow.
*/

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

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
                    string folder = Path.Combine(FileSystem.AppDataDirectory, "CardPhotos");
                    Directory.CreateDirectory(folder);

                    string name = _capturingBack ? $"card_back_{Guid.NewGuid()}.jpg" : $"card_front_{Guid.NewGuid()}.jpg";
                    string path = Path.Combine(folder, name);

                    using (var fs = File.Create(path))
                        await imageStream.CopyToAsync(fs);

                    if (!_capturingBack)
                    {
                        _frontImagePath = path;
                        _capturingBack = true;
                        await DisplayAlert("Flip Card", "Now flip your card and capture the BACK side.", "OK");
                        _isScanning = true;
                        return;
                    }

                    _backImagePath = path;
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

        private void Camera_MediaCaptured(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[Camera] Media captured successfully.");
        }

    }
}
