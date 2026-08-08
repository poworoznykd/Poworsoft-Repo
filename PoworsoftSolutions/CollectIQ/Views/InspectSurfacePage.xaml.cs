/*
* FILE: InspectSurfacePage.xaml.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* DESCRIPTION:
*     Guides the user through a repeatable four-direction surface capture.
*     The phone and card remain stationary while an external light is moved
*     around the card. After four captures the local inspection service builds
*     an enhanced surface map and anomaly heatmap.
*/

using CollectIQ.Helpers;
using CollectIQ.Interfaces;
using CollectIQ.Models.Inspection;
using Microsoft.Maui.Storage;

namespace CollectIQ.Views
{
    public partial class InspectSurfacePage : ContentPage
    {
        private readonly ISurfaceInspectionService surfaceInspectionService;
        private readonly Dictionary<SurfaceLightDirection, string> captures = new();
        private SurfaceLightDirection currentDirection = SurfaceLightDirection.Top;
        private bool captureInProgress;

        public InspectSurfacePage()
        {
            InitializeComponent();

            surfaceInspectionService =
                ServiceHelper.Services?.GetService(typeof(ISurfaceInspectionService)) as ISurfaceInspectionService
                ?? throw new InvalidOperationException("Surface inspection service is not registered.");

            UpdateCaptureStep();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (!CapturePanel.IsVisible)
            {
                return;
            }

            await StartCameraAsync();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            try
            {
                SurfaceCameraView?.StopCameraPreview();
            }
            catch
            {
                // Camera cleanup must not block navigation.
            }
        }

        private async void OnBackClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("..");
        }

        private async void OnStartCaptureClicked(object sender, EventArgs e)
        {
            SetupPanel.IsVisible = false;
            CapturePanel.IsVisible = true;
            ProcessingPanel.IsVisible = false;
            HeaderStatusLabel.Text = "Keep camera and card fixed";

            await StartCameraAsync();
        }

        private async Task StartCameraAsync()
        {
            try
            {
                using var cancellationTokenSource =
                    new CancellationTokenSource(TimeSpan.FromSeconds(7));

                await SurfaceCameraView.StartCameraPreview(cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                await DisplayAlert(
                    "Camera",
                    $"The inspection camera could not start: {ex.Message}",
                    "OK");
            }
        }

        private async void OnCaptureClicked(object sender, EventArgs e)
        {
            if (captureInProgress)
            {
                return;
            }

            captureInProgress = true;
            CaptureButton.IsEnabled = false;

            try
            {
                using var cancellationTokenSource =
                    new CancellationTokenSource(TimeSpan.FromSeconds(7));

                using Stream imageStream =
                    await SurfaceCameraView.CaptureImage(cancellationTokenSource.Token);

                string directory = Path.Combine(
                    FileSystem.AppDataDirectory,
                    "SurfaceInspectionCaptures");

                Directory.CreateDirectory(directory);

                string path = Path.Combine(
                    directory,
                    $"surface_{currentDirection.ToString().ToLowerInvariant()}_{Guid.NewGuid():N}.jpg");

                await using (FileStream fileStream = File.Create(path))
                {
                    await imageStream.CopyToAsync(fileStream);
                }

                captures[currentDirection] = path;
                CapturedCountLabel.Text = $"{captures.Count}/4";

                if (captures.Count == 4)
                {
                    await AnalyzeAsync();
                    return;
                }

                currentDirection = (SurfaceLightDirection)((int)currentDirection + 1);
                UpdateCaptureStep();
            }
            catch (Exception ex)
            {
                await DisplayAlert(
                    "Surface Capture",
                    $"The image could not be captured: {ex.Message}",
                    "OK");
            }
            finally
            {
                captureInProgress = false;
                CaptureButton.IsEnabled = true;
            }
        }

        private async void OnRestartClicked(object sender, EventArgs e)
        {
            captures.Clear();
            currentDirection = SurfaceLightDirection.Top;
            CapturedCountLabel.Text = "0/4";
            UpdateCaptureStep();

            await DisplayAlert(
                "Surface Scan Restarted",
                "Keep the phone and card fixed, then begin again with the top light position.",
                "OK");
        }

        private async Task AnalyzeAsync()
        {
            CapturePanel.IsVisible = false;
            ProcessingPanel.IsVisible = true;
            HeaderStatusLabel.Text = "Analyzing four directional images";

            try
            {
                SurfaceCameraView.StopCameraPreview();

                SurfaceInspectionResult result =
                    await surfaceInspectionService.AnalyzeAsync(captures);

                await Navigation.PushAsync(new SurfaceInspectionResultPage(result));
            }
            catch (Exception ex)
            {
                ProcessingPanel.IsVisible = false;
                CapturePanel.IsVisible = true;

                await DisplayAlert(
                    "Analysis Failed",
                    $"The surface images could not be analyzed: {ex.Message}",
                    "OK");
            }
        }

        private void UpdateCaptureStep()
        {
            int step = (int)currentDirection + 1;

            CaptureStepLabel.Text =
                $"{step} OF 4 — {currentDirection.ToString().ToUpperInvariant()} LIGHT";

            CaptureButton.Text =
                $"CAPTURE {currentDirection.ToString().ToUpperInvariant()}";

            CaptureInstructionLabel.Text = currentDirection switch
            {
                SurfaceLightDirection.Top =>
                    "Move the light above the card. Keep the phone and card completely still.",
                SurfaceLightDirection.Right =>
                    "Move the same light to the right side at roughly the same distance and angle.",
                SurfaceLightDirection.Bottom =>
                    "Move the same light below the card. Do not rotate or reposition the card.",
                SurfaceLightDirection.Left =>
                    "Move the same light to the left side. This is the final directional image.",
                _ => string.Empty
            };

            Color active = Color.FromArgb("#39FF14");
            Color inactive = Color.FromArgb("#64748B");

            TopLightMarker.TextColor = currentDirection == SurfaceLightDirection.Top ? active : inactive;
            RightLightMarker.TextColor = currentDirection == SurfaceLightDirection.Right ? active : inactive;
            BottomLightMarker.TextColor = currentDirection == SurfaceLightDirection.Bottom ? active : inactive;
            LeftLightMarker.TextColor = currentDirection == SurfaceLightDirection.Left ? active : inactive;
        }
    }
}
