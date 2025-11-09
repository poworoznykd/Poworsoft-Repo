/*
* FILE: ScanPage.xaml.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-18
* UPDATED: 2025-10-29
* DESCRIPTION:
*     Handles live camera scanning of card front and back,
*     saves captured images, and returns to the appropriate page
*     (CardPage or eBay search) depending on source.
*/

using CollectIQ.Utilities;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CollectIQ.Views
{
    public partial class ScanPage : ContentPage
    {
        // === Fields ===
        private bool isScanning = false;
        private bool capturingBack = false;
        private string frontImagePath = string.Empty;
        private string backImagePath = string.Empty;
        private readonly string? returnPage;

        // === Constructors ===

        /// <summary>
        /// Default constructor for general scanning (eBay OCR workflow).
        /// </summary>
        public ScanPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Overloaded constructor that allows specifying a return page (e.g., CardPage).
        /// </summary>
        /// <param name="returnPage">The page name to return to after both captures.</param>
        public ScanPage(string returnPage)
        {
            InitializeComponent();
            this.returnPage = returnPage;
        }

        // === Lifecycle ===

        /// <summary>
        /// Called when the page becomes visible.
        /// Starts the camera preview and begins the scan-line animation.
        /// </summary>
        protected override async void OnAppearing()
        {
            base.OnAppearing();

            try
            {
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await CameraView.StartCameraPreview(cts.Token);
                isScanning = true;

                await WaitForElementToRender(ScanLine);
                _ = RunScanLineAnimationAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Camera] Failed to start: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops scanning when the page is no longer visible.
        /// </summary>
        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            isScanning = false;
        }

        // === Utility Methods ===

        /// <summary>
        /// Waits until the specified element has been rendered
        /// and has valid dimensions before continuing.
        /// </summary>
        /// <param name="element">The element to monitor for rendering.</param>
        private async Task WaitForElementToRender(VisualElement element)
        {
            int retries = 0;
            while ((element.Height <= 0 || element.Width <= 0) && retries++ < 30)
                await Task.Delay(100);
        }

        /// <summary>
        /// Continuously animates the scan-line up and down while scanning is active.
        /// </summary>
        private async Task RunScanLineAnimationAsync()
        {
            if (ScanLine == null)
                return;

            double containerHeight = CameraView.Height;
            double startY = 0;
            double endY = containerHeight - 10;

            while (isScanning)
            {
                await ScanLine.TranslateTo(0, endY, 1800, Easing.CubicInOut);
                await ScanLine.TranslateTo(0, startY, 1800, Easing.CubicInOut);
            }
        }

        // === Event Handlers ===

        /// <summary>
        /// Captures front and back card images using the device camera.
        /// After both captures, navigates back to CardPage if invoked from there,
        /// otherwise proceeds to eBaySearchPage for OCR/search workflow.
        /// </summary>
        /// <param name="sender">Button initiating the capture.</param>
        /// <param name="e">Event arguments.</param>
        private async void OnScanClicked(object sender, EventArgs e)
        {
            try
            {
                isScanning = false;

                var captureCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var imageStream = await CameraView.CaptureImage(captureCts.Token);

                if (imageStream == null)
                {
                    await DisplayAlert("Error", "No image captured.", "OK");
                    isScanning = true;
                    return;
                }

                string folder = Path.Combine(FileSystem.AppDataDirectory, "CardPhotos");
                Directory.CreateDirectory(folder);

                string fileName = capturingBack
                    ? $"card_back_{Guid.NewGuid()}.jpg"
                    : $"card_front_{Guid.NewGuid()}.jpg";

                string path = Path.Combine(folder, fileName);
                using (var fs = File.Create(path))
                    await imageStream.CopyToAsync(fs);

                if (!capturingBack)
                {
                    frontImagePath = path;
                    capturingBack = true;
                    await DisplayAlert("Flip Card", "Now flip your card and capture the BACK side.", "OK");
                    isScanning = true;
                    return;
                }

                backImagePath = path;
                capturingBack = false;

                // Return path logic
                // Return path logic
                if (!string.IsNullOrEmpty(returnPage) &&
                    returnPage.Equals(nameof(CardPage), StringComparison.OrdinalIgnoreCase))
                {
                    var resultData = new Dictionary<string, string>
                    {
                        { "FrontPath", frontImagePath },
                        { "BackPath", backImagePath }
                    };

                    // Save temporarily for CardPage to read
                    NavigationCache.Set(nameof(CardPage), resultData);

                    await DisplayAlert("Captured", "Both sides captured successfully.", "OK");

                    // Return to CardPage
                    await Shell.Current.GoToAsync("..");
                }
                else
                {
                    await Shell.Current.GoToAsync(
                        $"//{nameof(EbaySearchPage)}?frontPath={Uri.EscapeDataString(frontImagePath)}&backPath={Uri.EscapeDataString(backImagePath)}");
                }


                await DisplayAlert("Captured", "Both sides captured successfully.", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Capture failed: {ex.Message}", "OK");
                isScanning = true;
            }
        }

        /// <summary>
        /// Navigates to the manual entry workflow for adding a card
        /// without using the camera.
        /// </summary>
        /// <param name="sender">Button initiating the manual entry.</param>
        /// <param name="e">Event arguments.</param>
        private async void OnAddManuallyClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync($"{nameof(CardPage)}");
        }

        /// <summary>
        /// Logs a successful media capture from the camera.
        /// </summary>
        /// <param name="sender">CameraView source.</param>
        /// <param name="e">Event arguments.</param>
        private void Camera_MediaCaptured(object sender, EventArgs e)
        {
            Debug.WriteLine("[Camera] Media captured successfully.");
        }
    }
}
