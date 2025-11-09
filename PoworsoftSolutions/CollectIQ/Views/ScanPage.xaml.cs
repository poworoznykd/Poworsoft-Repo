/*
* FILE: ScanPage.xaml.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-18
* UPDATED: 2025-11-09
* DESCRIPTION:
*     Handles live camera scanning of card front and back,
*     saves captured images, and returns to the appropriate page
*     (CardPage or eBay search) depending on source.
*     Includes safe camera cleanup on exit and double-tap prevention.
*/

using CollectIQ.Utilities;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CollectIQ.Views
{
    /// <summary>
    /// CLASS: ScanPage
    /// PURPOSE:
    ///     Provides front and back card capture via CameraView,
    ///     manages scan-line animation, and controls navigation flow.
    /// </summary>
    public partial class ScanPage : ContentPage
    {
        // === Fields ===
        private bool isScanning = false;
        private bool capturingBack = false;
        private bool isCapturing = false;
        private string frontImagePath = string.Empty;
        private string backImagePath = string.Empty;
        private readonly string? returnPage;

        // === Constructors ===

        /// <summary>
        /// FUNCTION: ScanPage
        /// DESCRIPTION:
        ///     Default constructor for general scanning (eBay OCR workflow).
        /// RETURNS:
        ///     None.
        /// </summary>
        public ScanPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// FUNCTION: ScanPage
        /// DESCRIPTION:
        ///     Overloaded constructor that allows specifying a return page (e.g., CardPage).
        /// PARAMETERS:
        ///     returnPage – The page name to return to after both captures.
        /// RETURNS:
        ///     None.
        /// </summary>
        public ScanPage(string returnPage)
        {
            InitializeComponent();
            this.returnPage = returnPage;
        }

        // === Lifecycle ===

        /// <summary>
        /// FUNCTION: OnAppearing
        /// DESCRIPTION:
        ///     Called when the page becomes visible.
        ///     Starts the camera preview and begins the scan-line animation.
        /// RETURNS:
        ///     None.
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

        // === Utility Methods ===

        /// <summary>
        /// FUNCTION: WaitForElementToRender
        /// DESCRIPTION:
        ///     Waits until the specified element has been rendered
        ///     and has valid dimensions before continuing.
        /// PARAMETERS:
        ///     element – The element to monitor for rendering.
        /// RETURNS:
        ///     Task.
        /// </summary>
        private async Task WaitForElementToRender(VisualElement element)
        {
            int retries = 0;
            while ((element.Height <= 0 || element.Width <= 0) && retries++ < 30)
                await Task.Delay(100);
        }

        /// <summary>
        /// FUNCTION: RunScanLineAnimationAsync
        /// DESCRIPTION:
        ///     Continuously animates the scan-line up and down while scanning is active.
        /// RETURNS:
        ///     Task.
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
        /// FUNCTION: OnScanClicked
        /// DESCRIPTION:
        ///     Captures front and back card images using CameraView,
        ///     saves them locally, and navigates to the appropriate page
        ///     (CardPage or EbaySearchPage) once both sides are complete.
        /// PARAMETERS:
        ///     sender – The button initiating the capture.
        ///     e – Standard event arguments.
        /// RETURNS:
        ///     None.
        /// </summary>
        private async void OnScanClicked(object sender, EventArgs e)
        {
            if (isCapturing)
                return;

            isCapturing = true;

            try
            {
                isScanning = false;

                // --- 1. Capture image with timeout ---
                using var captureCts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                using var imageStream = await CameraView.CaptureImage(captureCts.Token);

                if (imageStream == null)
                {
                    await DisplayAlert("Error", "No image captured.", "OK");
                    isScanning = true;
                    isCapturing = false;
                    return;
                }

                // --- 2. Prepare save directory ---
                string folder = Path.Combine(FileSystem.AppDataDirectory, "CardPhotos");
                Directory.CreateDirectory(folder);

                string fileName = capturingBack
                    ? $"card_back_{Guid.NewGuid()}.jpg"
                    : $"card_front_{Guid.NewGuid()}.jpg";

                string path = Path.Combine(folder, fileName);

                await using (FileStream fileStream = File.Create(path))
                {
                    await imageStream.CopyToAsync(fileStream);
                }

                // --- 3. Assign and continue workflow ---
                if (!capturingBack)
                {
                    frontImagePath = path;
                    capturingBack = true;

                    await DisplayAlert("Flip Card", "Now flip your card and capture the BACK side.", "OK");
                    isScanning = true;
                    isCapturing = false;
                    return;
                }

                backImagePath = path;
                capturingBack = false;

                // --- 4. Delay to ensure camera surface cleanup ---
                await Task.Delay(250);

                // --- 5. Navigation logic ---
                if (!string.IsNullOrEmpty(returnPage) &&
                    returnPage.Equals(nameof(CardPage), StringComparison.OrdinalIgnoreCase))
                {
                    var resultData = new Dictionary<string, string>
                    {
                        { "FrontPath", frontImagePath },
                        { "BackPath", backImagePath }
                    };

                    NavigationCache.Set(nameof(CardPage), resultData);

                    await DisplayAlert("Captured", "Both sides captured successfully.", "OK");
                    await Shell.Current.GoToAsync("..");
                }
                else
                {
                    await DisplayAlert("Captured", "Both sides captured successfully.", "OK");

                    // Use absolute Shell route because it's a tab page
                    await Shell.Current.GoToAsync(
                        $"//{nameof(EbaySearchPage)}?frontPath={Uri.EscapeDataString(frontImagePath)}&backPath={Uri.EscapeDataString(backImagePath)}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ScanPage] Capture failed: {ex}");
                await DisplayAlert("Error", $"Capture failed: {ex.Message}", "OK");
                isScanning = true;
            }
            finally
            {
                isCapturing = false;
            }
        }

        /// <summary>
        /// FUNCTION: OnAddManuallyClicked
        /// DESCRIPTION:
        ///     Navigates to the manual entry workflow for adding a card
        ///     without using the camera.
        /// PARAMETERS:
        ///     sender – The initiating button.
        ///     e – Event arguments.
        /// RETURNS:
        ///     None.
        /// </summary>
        private async void OnAddManuallyClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync($"{nameof(CardPage)}");
        }

        /// <summary>
        /// FUNCTION: Camera_MediaCaptured
        /// DESCRIPTION:
        ///     Logs a successful media capture from the camera.
        /// PARAMETERS:
        ///     sender – CameraView source.
        ///     e – Event arguments.
        /// RETURNS:
        ///     None.
        /// </summary>
        private void Camera_MediaCaptured(object sender, EventArgs e)
        {
            Debug.WriteLine("[Camera] Media captured successfully.");
        }

        private void OnDisappearing(object sender, EventArgs e)
        {
            base.OnDisappearing();

            isScanning = false;

            try
            {
                CameraView?.StopCameraPreview();
                CameraView?.Handler?.DisconnectHandler();
                Debug.WriteLine("[Camera] Preview stopped and handler disconnected safely.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Camera] Cleanup failed: {ex.Message}");
            }
        }
    }
}
