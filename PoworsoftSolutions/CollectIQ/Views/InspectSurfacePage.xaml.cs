/*
* FILE: InspectSurfacePage.xaml.cs
* PROJECT: CollectIQ
* DESCRIPTION:
*     Capture UI for three surface-inspection strategies:
*     1) External movable-light photometric inspection (primary)
*     2) Single-photo surface pre-screen
*     3) Fixed-phone/fixed-light 20-view card tilt sweep
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
        private readonly Dictionary<SurfaceLightDirection, string> directionalCaptures = new();
        private readonly List<string> tiltCaptures = new();
        private SurfaceInspectionMode mode = SurfaceInspectionMode.ExternalLight;
        private string? neutralReferencePath;
        private bool capturingReference = true;
        private SurfaceLightDirection currentDirection = SurfaceLightDirection.Top;
        private bool captureInProgress;
        private bool cameraReady;

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
            if (CapturePanel.IsVisible)
                await StartCameraAsync();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            try { SurfaceCameraView?.StopCameraPreview(); } catch { }
        }

        private async void OnBackClicked(object sender, EventArgs e) => await Shell.Current.GoToAsync("..");

        private void OnExternalModeClicked(object sender, EventArgs e) => SelectMode(SurfaceInspectionMode.ExternalLight);
        private void OnSinglePhotoModeClicked(object sender, EventArgs e) => SelectMode(SurfaceInspectionMode.SinglePhoto);
        private void OnTiltSweepModeClicked(object sender, EventArgs e) => SelectMode(SurfaceInspectionMode.TiltSweep);

        private void SelectMode(SurfaceInspectionMode selected)
        {
            mode = selected;
            ExternalModeButton.BackgroundColor = selected == SurfaceInspectionMode.ExternalLight ? Color.FromArgb("#1D6A2A") : Color.FromArgb("#253047");
            SingleModeButton.BackgroundColor = selected == SurfaceInspectionMode.SinglePhoto ? Color.FromArgb("#1D6A2A") : Color.FromArgb("#253047");
            TiltModeButton.BackgroundColor = selected == SurfaceInspectionMode.TiltSweep ? Color.FromArgb("#1D6A2A") : Color.FromArgb("#253047");

            StartSurfaceButton.Text = selected switch
            {
                SurfaceInspectionMode.ExternalLight => "START 5-IMAGE EXTERNAL LIGHT SCAN",
                SurfaceInspectionMode.SinglePhoto => "START SINGLE-PHOTO SURFACE PRE-SCREEN",
                SurfaceInspectionMode.TiltSweep => "START 20-VIEW TILT SWEEP",
                _ => "START SURFACE SCAN"
            };
        }

        private async void OnStartCaptureClicked(object sender, EventArgs e)
        {
            ResetCaptureState();
            SetupPanel.IsVisible = false;
            CapturePanel.IsVisible = true;
            ProcessingPanel.IsVisible = false;
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
                using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await SurfaceCameraView.StartCameraPreview(cancellationTokenSource.Token);
                await Task.Delay(650, cancellationTokenSource.Token);
                cameraReady = SurfaceCameraView.IsAvailable;
                if (!cameraReady)
                    throw new InvalidOperationException("The rear camera is not available yet.");
                UpdateCaptureStep();
            }
            catch (OperationCanceledException)
            {
                cameraReady = false;
                await DisplayAlert("Camera", "The camera took too long to start. Tap Restart to try again.", "OK");
            }
            catch (Exception ex)
            {
                cameraReady = false;
                await DisplayAlert("Camera", $"The inspection camera could not start: {ex.Message}", "OK");
            }
            finally
            {
                CaptureButton.IsEnabled = cameraReady;
                if (!cameraReady) CaptureButton.Text = "CAMERA NOT READY";
            }
        }

        private async void OnCaptureClicked(object sender, EventArgs e)
        {
            if (captureInProgress) return;
            if (!cameraReady)
            {
                await StartCameraAsync();
                if (!cameraReady) return;
            }

            captureInProgress = true;
            CaptureButton.IsEnabled = false;

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));
                using Stream imageStream = await SurfaceCameraView.CaptureImage(cts.Token);
                string directory = Path.Combine(FileSystem.AppDataDirectory, "SurfaceInspectionCaptures");
                Directory.CreateDirectory(directory);

                string captureName = mode switch
                {
                    SurfaceInspectionMode.ExternalLight when capturingReference => "reference",
                    SurfaceInspectionMode.ExternalLight => currentDirection.ToString().ToLowerInvariant(),
                    SurfaceInspectionMode.SinglePhoto => "single",
                    SurfaceInspectionMode.TiltSweep => $"tilt_{tiltCaptures.Count:00}",
                    _ => "surface"
                };

                string path = Path.Combine(directory, $"surface_{captureName}_{Guid.NewGuid():N}.jpg");
                await using (FileStream fileStream = File.Create(path))
                    await imageStream.CopyToAsync(fileStream);

                if (mode == SurfaceInspectionMode.SinglePhoto)
                {
                    neutralReferencePath = path;
                    CapturedCountLabel.Text = "1/1";
                    await AnalyzeAsync();
                    return;
                }

                if (mode == SurfaceInspectionMode.TiltSweep)
                {
                    tiltCaptures.Add(path);
                    CapturedCountLabel.Text = $"{tiltCaptures.Count}/20";
                    if (tiltCaptures.Count >= 20)
                    {
                        await AnalyzeAsync();
                        return;
                    }
                    UpdateCaptureStep();
                    return;
                }

                if (capturingReference)
                {
                    neutralReferencePath = path;
                    capturingReference = false;
                    CapturedCountLabel.Text = "1/5";
                    UpdateCaptureStep();
                    return;
                }

                directionalCaptures[currentDirection] = path;
                CapturedCountLabel.Text = $"{directionalCaptures.Count + 1}/5";
                if (directionalCaptures.Count == 4)
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
                await DisplayAlert("Surface Capture", "The camera was not ready to capture. CollectIQ will restart the preview; then try the capture again.", "OK");
                await StartCameraAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Surface Capture", $"The image could not be captured: {ex.Message}", "OK");
            }
            finally
            {
                captureInProgress = false;
                CaptureButton.IsEnabled = cameraReady;
                if (cameraReady && CapturePanel.IsVisible) UpdateCaptureStep();
            }
        }

        private async void OnRestartClicked(object sender, EventArgs e)
        {
            ResetCaptureState();
            if (!cameraReady) await StartCameraAsync();
            await DisplayAlert("Surface Scan Restarted", mode == SurfaceInspectionMode.TiltSweep
                ? "Keep the phone and light fixed. Tilt the card to a different small angle for every capture."
                : "Keep all four card edges visible against the solid background.", "OK");
        }

        private void ResetCaptureState()
        {
            directionalCaptures.Clear();
            tiltCaptures.Clear();
            neutralReferencePath = null;
            capturingReference = true;
            currentDirection = SurfaceLightDirection.Top;
            CapturedCountLabel.Text = mode switch
            {
                SurfaceInspectionMode.ExternalLight => "0/5",
                SurfaceInspectionMode.SinglePhoto => "0/1",
                SurfaceInspectionMode.TiltSweep => "0/20",
                _ => "0"
            };
            UpdateCaptureStep();
        }

        private async Task AnalyzeAsync()
        {
            CapturePanel.IsVisible = false;
            ProcessingPanel.IsVisible = true;
            HeaderStatusLabel.Text = "Analyzing surface images";

            try
            {
                SurfaceCameraView.StopCameraPreview();
                SurfaceInspectionResult result = mode switch
                {
                    SurfaceInspectionMode.ExternalLight => await surfaceInspectionService.AnalyzeAsync(
                        neutralReferencePath ?? throw new InvalidOperationException("The neutral reference capture is missing."),
                        directionalCaptures),
                    SurfaceInspectionMode.SinglePhoto => await surfaceInspectionService.AnalyzeSinglePhotoAsync(
                        neutralReferencePath ?? throw new InvalidOperationException("The card image is missing.")),
                    SurfaceInspectionMode.TiltSweep => await surfaceInspectionService.AnalyzeTiltSweepAsync(tiltCaptures),
                    _ => throw new InvalidOperationException("Unknown surface inspection mode.")
                };

                await Navigation.PushAsync(new SurfaceInspectionResultPage(result));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SurfaceInspection] {ex}");
                ProcessingPanel.IsVisible = false;
                CapturePanel.IsVisible = true;
                await DisplayAlert("Analysis Failed", $"The surface images could not be analyzed: {ex.Message}", "OK");
            }
        }

        private void UpdateCaptureStep()
        {
            Color active = Color.FromArgb("#39FF14");
            Color inactive = Color.FromArgb("#64748B");
            TopLightMarker.TextColor = inactive;
            RightLightMarker.TextColor = inactive;
            BottomLightMarker.TextColor = inactive;
            LeftLightMarker.TextColor = inactive;

            if (mode == SurfaceInspectionMode.SinglePhoto)
            {
                CaptureStepLabel.Text = "1 OF 1 — CARD SURFACE";
                CaptureButton.Text = "CAPTURE CARD";
                CaptureInstructionLabel.Text = "Place the card on a solid matte background, keep it upright, and capture the full card under even light.";
                HeaderStatusLabel.Text = "Single-photo surface pre-screen";
                return;
            }

            if (mode == SurfaceInspectionMode.TiltSweep)
            {
                int next = tiltCaptures.Count + 1;
                CaptureStepLabel.Text = $"{next} OF 20 — TILT VIEW";
                CaptureButton.Text = $"CAPTURE TILT {next}";
                CaptureInstructionLabel.Text = "KEEP THE PHONE AND LIGHT FIXED. Tilt/skew the card slightly to a new angle (left/right/up/down/diagonal) while keeping all four edges visible. Small changes are enough.";
                HeaderStatusLabel.Text = "Fixed phone + fixed light • move only the card";
                return;
            }

            if (capturingReference)
            {
                CaptureStepLabel.Text = "1 OF 5 — NEUTRAL REFERENCE";
                CaptureButton.Text = "CAPTURE REFERENCE";
                CaptureInstructionLabel.Text = "Use normal room lighting. Keep the card upright on a matte solid background with all four physical edges visible.";
                HeaderStatusLabel.Text = "Neutral geometry reference";
                return;
            }

            int step = (int)currentDirection + 2;
            CaptureStepLabel.Text = $"{step} OF 5 — {currentDirection.ToString().ToUpperInvariant()} LIGHT";
            CaptureButton.Text = $"CAPTURE {currentDirection.ToString().ToUpperInvariant()}";
            CaptureInstructionLabel.Text = "Keep the phone and card fixed. Move only the external light to the indicated side. CollectIQ re-detects the physical rectangle and maps it into the same canonical card matrix.";
            HeaderStatusLabel.Text = "External light photometric capture";
            TopLightMarker.TextColor = currentDirection == SurfaceLightDirection.Top ? active : inactive;
            RightLightMarker.TextColor = currentDirection == SurfaceLightDirection.Right ? active : inactive;
            BottomLightMarker.TextColor = currentDirection == SurfaceLightDirection.Bottom ? active : inactive;
            LeftLightMarker.TextColor = currentDirection == SurfaceLightDirection.Left ? active : inactive;
        }
    }
}
