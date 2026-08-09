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
        private string? neutralReferencePath;
        private bool capturingReference = true;
        private SurfaceLightDirection currentDirection = SurfaceLightDirection.Top;
        private bool captureInProgress;
        private bool cameraReady;
        private InspectionCardSurfaceProfile selectedSurfaceProfile = InspectionCardSurfaceProfile.Normal;

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
            HeaderStatusLabel.Text = "Capture neutral geometry reference first";

            await StartCameraAsync();
        }

        private async Task StartCameraAsync()
        {
            cameraReady = false;
            CaptureButton.IsEnabled = false;
            CaptureButton.Text = "INITIALIZING CAMERA...";
            HeaderStatusLabel.Text = "Starting inspection camera";

            try
            {
                using var cancellationTokenSource =
                    new CancellationTokenSource(TimeSpan.FromSeconds(10));

                // CameraView can exist before its native camera session is fully
                // ready. Starting the preview and allowing a short stabilization
                // period prevents an immediate capture from racing initialization.
                await SurfaceCameraView.StartCameraPreview(cancellationTokenSource.Token);
                await Task.Delay(650, cancellationTokenSource.Token);

                cameraReady = SurfaceCameraView.IsAvailable;

                if (!cameraReady)
                {
                    throw new InvalidOperationException(
                        "The rear camera is not available yet.");
                }

                HeaderStatusLabel.Text = "Keep card fixed • auto-alignment enabled";
                UpdateCaptureStep();
            }
            catch (OperationCanceledException)
            {
                cameraReady = false;

                await DisplayAlert(
                    "Camera",
                    "The camera took too long to start. Tap Restart to try again.",
                    "OK");
            }
            catch (Exception ex)
            {
                cameraReady = false;

                await DisplayAlert(
                    "Camera",
                    $"The inspection camera could not start: {ex.Message}",
                    "OK");
            }
            finally
            {
                CaptureButton.IsEnabled = cameraReady;

                if (!cameraReady)
                {
                    CaptureButton.Text = "CAMERA NOT READY";
                }
            }
        }

        private async void OnCaptureClicked(object sender, EventArgs e)
        {
            if (captureInProgress)
            {
                return;
            }

            if (!cameraReady)
            {
                await StartCameraAsync();

                if (!cameraReady)
                {
                    return;
                }
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

                string captureName = capturingReference
                    ? "reference"
                    : currentDirection.ToString().ToLowerInvariant();

                string path = Path.Combine(
                    directory,
                    $"surface_{captureName}_{Guid.NewGuid():N}.jpg");

                await using (FileStream fileStream = File.Create(path))
                {
                    await imageStream.CopyToAsync(fileStream);
                }

                if (capturingReference)
                {
                    neutralReferencePath = path;
                    capturingReference = false;
                    CapturedCountLabel.Text = "1/5";
                    UpdateCaptureStep();
                    return;
                }

                captures[currentDirection] = path;
                CapturedCountLabel.Text = $"{captures.Count + 1}/5";

                if (captures.Count == 4)
                {
                    await AnalyzeAsync();
                    return;
                }

                currentDirection = (SurfaceLightDirection)((int)currentDirection + 1);
                UpdateCaptureStep();
            }
            catch (OperationCanceledException)
            {
                cameraReady = false;

                await DisplayAlert(
                    "Surface Capture",
                    "The camera was not ready to capture. CollectIQ will restart the preview; then try the capture again.",
                    "OK");

                await StartCameraAsync();
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
                CaptureButton.IsEnabled = cameraReady;

                if (cameraReady)
                {
                    UpdateCaptureStep();
                }
            }
        }

        private async void OnRestartClicked(object sender, EventArgs e)
        {
            captures.Clear();
            neutralReferencePath = null;
            capturingReference = true;
            currentDirection = SurfaceLightDirection.Top;
            CapturedCountLabel.Text = "0/5";
            UpdateCaptureStep();

            if (!cameraReady)
            {
                await StartCameraAsync();
            }

            await DisplayAlert(
                "Surface Scan Restarted",
                "Keep the card fixed and all four edges visible. Small camera movement will be corrected automatically.",
                "OK");
        }

        private async Task AnalyzeAsync()
        {
            CapturePanel.IsVisible = false;
            ProcessingPanel.IsVisible = true;
            HeaderStatusLabel.Text = $"Analyzing four directional images • {GetSurfaceProfileLabel()} profile";

            try
            {
                SurfaceCameraView.StopCameraPreview();

                SurfaceInspectionResult result =
                    await surfaceInspectionService.AnalyzeAsync(
                        neutralReferencePath
                            ?? throw new InvalidOperationException("The neutral reference capture is missing."),
                        captures,
                        selectedSurfaceProfile);

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


        private void OnSurfaceProfileChanged(object? sender, CheckedChangedEventArgs e)
        {
            if (!e.Value)
            {
                return;
            }

            selectedSurfaceProfile = sender == FoilChromeSurfaceProfileRadio
                ? InspectionCardSurfaceProfile.FoilChrome
                : InspectionCardSurfaceProfile.Normal;

            HeaderStatusLabel.Text = $"Surface inspection • {GetSurfaceProfileLabel()} profile";
        }

        private string GetSurfaceProfileLabel()
        {
            return selectedSurfaceProfile == InspectionCardSurfaceProfile.FoilChrome
                ? "Foil/Chrome"
                : "Normal";
        }

        private void UpdateCaptureStep()
        {
            Color active = Color.FromArgb("#39FF14");
            Color inactive = Color.FromArgb("#64748B");

            if (capturingReference)
            {
                CaptureStepLabel.Text = "1 OF 5 — NEUTRAL REFERENCE";
                CaptureButton.Text = "CAPTURE REFERENCE";
                CaptureInstructionLabel.Text =
                    "Move the directional light away. Use normal room lighting and keep the complete card inside the guide. This image establishes the physical card edges for all four lit captures.";
                HeaderStatusLabel.Text = "Neutral geometry reference";
                TopLightMarker.TextColor = inactive;
                RightLightMarker.TextColor = inactive;
                BottomLightMarker.TextColor = inactive;
                LeftLightMarker.TextColor = inactive;
                return;
            }

            int step = (int)currentDirection + 2;
            CaptureStepLabel.Text =
                $"{step} OF 5 — {currentDirection.ToString().ToUpperInvariant()} LIGHT";
            CaptureButton.Text =
                $"CAPTURE {currentDirection.ToString().ToUpperInvariant()}";

            CaptureInstructionLabel.Text = currentDirection switch
            {
                SurfaceLightDirection.Top =>
                    "Move the light above the card. Keep the full card inside the guide; CollectIQ will relocate the same four physical sides from the neutral reference.",
                SurfaceLightDirection.Right =>
                    "Move the same light to the right side. Keep the card fixed; normal handheld phone movement is allowed.",
                SurfaceLightDirection.Bottom =>
                    "Move the same light below the card. Keep all four physical card edges visible inside the guide.",
                SurfaceLightDirection.Left =>
                    "Move the light to the left side. Final image — keep the card itself fixed and fully visible.",
                _ => string.Empty
            };

            HeaderStatusLabel.Text = "Reference locked • local edge tracking enabled";
            TopLightMarker.TextColor = currentDirection == SurfaceLightDirection.Top ? active : inactive;
            RightLightMarker.TextColor = currentDirection == SurfaceLightDirection.Right ? active : inactive;
            BottomLightMarker.TextColor = currentDirection == SurfaceLightDirection.Bottom ? active : inactive;
            LeftLightMarker.TextColor = currentDirection == SurfaceLightDirection.Left ? active : inactive;
        }
    }
}
