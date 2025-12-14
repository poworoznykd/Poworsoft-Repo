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
*     Implements SET Coding Standards (Rev 1.11).
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CollectIQ.Utilities;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

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
        // =========================
        // Fields
        // =========================

        /// <summary>
        /// Indicates whether the scan-line animation should continue running.
        /// </summary>
        private bool isScanning;

        /// <summary>
        /// Indicates that the next capture should be treated as the back of the card
        /// for the CardPage workflow.
        /// </summary>
        private bool isCapturingBack;

        /// <summary>
        /// Prevents double-tap / double-capture scenarios on the Scan button.
        /// </summary>
        private bool isCaptureInProgress;

        /// <summary>
        /// Full file system path for the captured front image.
        /// </summary>
        private string frontImagePath = string.Empty;

        /// <summary>
        /// Full file system path for the captured back image.
        /// </summary>
        private string backImagePath = string.Empty;

        /// <summary>
        /// Title of the page that initiated this scan (e.g., CardPage).
        /// Used to decide if we capture front+back or front-only.
        /// </summary>
        private readonly string? returnPageName;

        // =========================
        // Constructors
        // =========================

        /// <summary>
        /// FUNCTION: ScanPage
        /// DESCRIPTION:
        ///     Default constructor for general scanning (eBay image workflow).
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
        ///     Overloaded constructor that allows specifying a return page
        ///     (for example, CardPage for front + back capture).
        /// PARAMETERS:
        ///     returnPageNameParam - The page name to return to after captures.
        /// RETURNS:
        ///     None.
        /// </summary>
        /// <param name="returnPageNameParam">Title of the page to return to.</param>
        public ScanPage(string returnPageNameParam)
        {
            InitializeComponent();
            returnPageName = returnPageNameParam;
        }

        // =========================
        // Lifecycle
        // =========================

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

            isScanning = true;
            isCaptureInProgress = false;

            try
            {
                var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await CameraView.StartCameraPreview(cancellationTokenSource.Token);

                // WAIT for CameraView to actually measure.
                await WaitForValidHeightAsync(CameraView);
                await WaitForValidHeightAsync(ScanLine);

                // Start animation once the heights are actually valid.
                _ = RunScanLineAnimationAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Camera] Failed to start: {ex.Message}");
            }
        }

        private async Task WaitForValidHeightAsync(VisualElement element)
        {
            int retries = 0;

            while (retries++ < 30)
            {
                if (element?.Height > 0)
                    return;

                await Task.Delay(100);
            }

            Debug.WriteLine($"[Layout] Warning: {element} did not get valid height after retries.");
        }



        /// <summary>
        /// FUNCTION: OnDisappearing
        /// DESCRIPTION:
        ///     Override called when the page is no longer visible.
        ///     Stops the camera preview to release the hardware.
        /// RETURNS:
        ///     None.
        /// </summary>
        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            isScanning = false;

            try
            {
                if (CameraView != null)
                {
                    CameraView.StopCameraPreview();
                    Debug.WriteLine("[Camera] Preview stopped safely in OnDisappearing override.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Camera] Cleanup failed in OnDisappearing override: {ex.Message}");
            }
        }

        /// <summary>
        /// FUNCTION: OnDisappearing
        /// DESCRIPTION:
        ///     Event handler used by XAML (Disappearing="OnDisappearing").
        ///     Forwards to the parameterless override to keep logic in one place.
        /// PARAMETERS:
        ///     sender - The page raising the event.
        ///     e      - Event arguments.
        /// RETURNS:
        ///     None.
        /// </summary>
        /// <param name="sender">Event source.</param>
        /// <param name="e">Event arguments.</param>
        private void OnDisappearing(object sender, EventArgs e)
        {
            // Forward to the override, so XAML wiring still works.
            OnDisappearing();
        }

        // =========================
        // Utility Methods
        // =========================

        /// <summary>
        /// FUNCTION: WaitForElementToRender
        /// DESCRIPTION:
        ///     Waits until the specified element has valid dimensions,
        ///     which indicates that layout has completed.
        /// PARAMETERS:
        ///     element - The visual element to monitor.
        /// RETURNS:
        ///     Task that completes when the element has non-zero width/height
        ///     or after a reasonable timeout.
        /// </summary>
        /// <param name="element">Element to wait on.</param>
        private async Task WaitForElementToRender(VisualElement element)
        {
            int retries = 0;

            while ((element.Height <= 0 || element.Width <= 0) && retries++ < 30)
            {
                await Task.Delay(100);
            }
        }

        /// <summary>
        /// FUNCTION: RunScanLineAnimationAsync
        /// DESCRIPTION:
        ///     Continuously animates the scan-line up and down while
        ///     the isScanning flag remains true.
        /// RETURNS:
        ///     Task.
        /// </summary>
        private async Task RunScanLineAnimationAsync()
        {
            if (ScanLine == null || CameraView == null)
            {
                return;
            }

            double containerHeight = CameraView.Height;
            double startY = 0;
            double endY = containerHeight - 10;

            while (isScanning)
            {
                try
                {
                    await ScanLine.TranslateTo(0, endY, 1800, Easing.CubicInOut);
                    await ScanLine.TranslateTo(0, startY, 1800, Easing.CubicInOut);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ScanLine] Animation error: {ex.Message}");
                    break;
                }
            }
        }

        // =========================
        // Event Handlers
        // =========================

        /// <summary>
        /// FUNCTION: OnScanClicked
        /// DESCRIPTION:
        ///     Captures card images using CameraView, saves them locally,
        ///     and navigates to the appropriate page.
        ///     - If launched with returnPageName = CardPage:
        ///         captures FRONT then BACK and returns both paths.
        ///     - Otherwise (eBay flow):
        ///         captures only FRONT and navigates to EbaySearchPage
        ///         using search_by_image.
        /// PARAMETERS:
        ///     sender - The button initiating the capture.
        ///     e - Standard event arguments.
        /// RETURNS:
        ///     None.
        /// </summary>
        private async void OnScanClicked(object sender, EventArgs e)
        {
            if (isCaptureInProgress)
            {
                return;
            }

            isCaptureInProgress = true;

            try
            {
                isScanning = false;

                using var captureCancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                using var imageStream = await CameraView.CaptureImage(captureCancellationTokenSource.Token);

                if (imageStream == null)
                {
                    await DisplayAlert("Error", "No image captured.", "OK");
                    isScanning = true;
                    isCaptureInProgress = false;
                    return;
                }

                string cardPhotosFolder = Path.Combine(FileSystem.AppDataDirectory, "CardPhotos");
                Directory.CreateDirectory(cardPhotosFolder);

                string fileName = isCapturingBack
                    ? $"card_back_{Guid.NewGuid()}.jpg"
                    : $"card_front_{Guid.NewGuid()}.jpg";

                string savedPath = Path.Combine(cardPhotosFolder, fileName);

                await using (FileStream fileStream = File.Create(savedPath))
                {
                    await imageStream.CopyToAsync(fileStream);
                }

                bool isCardPageWorkflow =
                    !string.IsNullOrWhiteSpace(returnPageName) &&
                    returnPageName.Equals(nameof(CardPage), StringComparison.OrdinalIgnoreCase);

                if (isCardPageWorkflow)
                {
                    await HandleCardPageWorkflowAsync(savedPath);
                }
                else
                {
                    await HandleEbayWorkflowAsync(savedPath);
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
                isCaptureInProgress = false;
            }
        }

        /// <summary>
        /// FUNCTION: HandleCardPageWorkflowAsync
        /// DESCRIPTION:
        ///     CardPage flow: first capture is FRONT, second capture is BACK.
        ///     After both are captured, paths are placed into NavigationCache
        ///     and control returns to CardPage.
        /// PARAMETERS:
        ///     savedPath - The file system path of the latest capture.
        /// RETURNS:
        ///     Task.
        /// </summary>
        /// <param name="savedPath">Captured image path.</param>
        private async Task HandleCardPageWorkflowAsync(string savedPath)
        {
            if (!isCapturingBack)
            {
                frontImagePath = savedPath;
                isCapturingBack = true;

                await DisplayAlert(
                    "Flip Card",
                    "Now flip your card and capture the BACK side.",
                    "OK");

                isScanning = true;
                return;
            }

            backImagePath = savedPath;
            isCapturingBack = false;

            await Task.Delay(250);

            var resultData = new Dictionary<string, string>
            {
                { "FrontPath", frontImagePath },
                { "BackPath", backImagePath }
            };

            NavigationCache.Set(nameof(CardPage), resultData);

            await DisplayAlert("Captured", "Both sides captured successfully.", "OK");
            await Shell.Current.GoToAsync("..");
        }

        /// <summary>
        /// FUNCTION: HandleEbayWorkflowAsync
        /// DESCRIPTION:
        ///     eBay flow: capture only the FRONT image and navigate to
        ///     EbaySearchPage, passing the front path as a query parameter.
        /// PARAMETERS:
        ///     savedPath - The file system path of the captured front image.
        /// RETURNS:
        ///     Task.
        /// </summary>
        /// <param name="savedPath">Captured image path.</param>
        private async Task HandleEbayWorkflowAsync(string savedPath)
        {
            frontImagePath = savedPath;
            isCapturingBack = false;

            await Task.Delay(250);

            await DisplayAlert("Captured", "Card front captured successfully.", "OK");

            string encodedPath = Uri.EscapeDataString(frontImagePath);

            await Shell.Current.GoToAsync(
                $"//{nameof(EbaySearchPage)}?frontPath={encodedPath}");
        }

        /// <summary>
        /// FUNCTION: OnAddManuallyClicked
        /// DESCRIPTION:
        ///     Navigates to the manual entry workflow for adding a card
        ///     without using the camera.
        /// PARAMETERS:
        ///     sender - The initiating button.
        ///     e - Event arguments.
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
        ///     Logs a successful media capture from the underlying camera.
        /// PARAMETERS:
        ///     sender - CameraView source.
        ///     e - Event arguments.
        /// RETURNS:
        ///     None.
        /// </summary>
        private void Camera_MediaCaptured(object sender, EventArgs e)
        {
            Debug.WriteLine("[Camera] Media captured successfully.");
        }
    }
}
