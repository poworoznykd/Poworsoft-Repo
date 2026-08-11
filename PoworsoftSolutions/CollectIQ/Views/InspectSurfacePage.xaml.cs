/*
* FILE: InspectSurfacePage.xaml.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* DESCRIPTION:
*     Guides the user through a neutral reference plus four directional
*     surface captures. The user can use either an external light or the
*     phone torch. In phone-torch mode the card remains stationary while the
*     phone is moved around it and registration corrects handheld pose changes.
*/

using CollectIQ.Helpers;
using CollectIQ.Interfaces;
using CollectIQ.Models.Inspection;
using CommunityToolkit.Maui.Core;
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
        private IlluminationMode illuminationMode = IlluminationMode.ExternalLight;

        private enum IlluminationMode
        {
            ExternalLight,
            PhoneFlash,
            PhoneTorch
        }
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
                SetTorch(false);
                SetFlashMode(false);
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
            SetTorch(false);
            SetFlashMode(false);
            HeaderStatusLabel.Text = illuminationMode switch
            {
                IlluminationMode.PhoneFlash => "Capture neutral reference first • flash OFF",
                IlluminationMode.PhoneTorch => "Capture neutral reference first • torch OFF",
                _ => "Capture neutral geometry reference first"
            };

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
                ApplyIlluminationForCurrentStep();
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
                ApplyIlluminationForCurrentStep();
                if (illuminationMode == IlluminationMode.PhoneFlash && !capturingReference)
                {
                    await Task.Delay(120);
                }
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
                    ApplyIlluminationForCurrentStep();
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
                ApplyIlluminationForCurrentStep();
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
            SetTorch(false);
            SetFlashMode(false);
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
                SetTorch(false);
                SetFlashMode(false);
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


        private void OnIlluminationSourceChanged(object? sender, CheckedChangedEventArgs e)
        {
            if (!e.Value) return;

            illuminationMode = sender == PhoneFlashModeRadio
                ? IlluminationMode.PhoneFlash
                : sender == PhoneTorchModeRadio
                    ? IlluminationMode.PhoneTorch
                    : IlluminationMode.ExternalLight;

            IlluminationModeHelpLabel.Text = illuminationMode switch
            {
                IlluminationMode.PhoneFlash => "Phone Flash: reference uses flash OFF. Top/Right/Bottom/Left use capture flash ON. The flash fires only when the photo is taken.",
                IlluminationMode.PhoneTorch => "Phone Torch: reference uses torch OFF. Top/Right/Bottom/Left keep the light ON continuously while positioning.",
                _ => "External Light: phone flash and torch stay OFF. Move only the external light."
            };

            if (CapturePanel is not null && CapturePanel.IsVisible)
            {
                ApplyIlluminationForCurrentStep();
                UpdateCaptureStep();
            }
        }

        private void ApplyIlluminationForCurrentStep()
        {
            bool directional = !capturingReference && cameraReady;
            if (illuminationMode == IlluminationMode.PhoneFlash)
            {
                SetTorch(false);
                SetFlashMode(directional);
            }
            else if (illuminationMode == IlluminationMode.PhoneTorch)
            {
                SetFlashMode(false);
                SetTorch(directional);
            }
            else
            {
                SetFlashMode(false);
                SetTorch(false);
            }
        }

        private void SetFlashMode(bool enabled)
        {
            try
            {
                SurfaceCameraView.CameraFlashMode = enabled ? CameraFlashMode.On : CameraFlashMode.Off;
            }
            catch { }
        }

        private void SetTorch(bool enabled)
        {
            try
            {
                SurfaceCameraView.IsTorchOn = enabled;
            }
            catch { }
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
                CaptureInstructionLabel.Text = illuminationMode switch
                {
                    IlluminationMode.PhoneFlash => "FLASH OFF. Capture the neutral reference under normal room light.",
                    IlluminationMode.PhoneTorch => "TORCH OFF. Capture the neutral reference under normal room light.",
                    _ => "Move the external light away and capture the neutral reference under normal room light."
                };
                HeaderStatusLabel.Text = illuminationMode switch
                {
                    IlluminationMode.PhoneFlash => "Neutral reference • flash OFF",
                    IlluminationMode.PhoneTorch => "Neutral reference • torch OFF",
                    _ => "Neutral geometry reference"
                };
                TopLightMarker.TextColor = inactive;
                RightLightMarker.TextColor = inactive;
                BottomLightMarker.TextColor = inactive;
                LeftLightMarker.TextColor = inactive;
                return;
            }

            int step = (int)currentDirection + 2;
            CaptureStepLabel.Text = $"{step} OF 5 — {currentDirection.ToString().ToUpperInvariant()} LIGHT";
            CaptureButton.Text = $"CAPTURE {currentDirection.ToString().ToUpperInvariant()}";

            string side = currentDirection.ToString().ToUpperInvariant();
            CaptureInstructionLabel.Text = illuminationMode switch
            {
                IlluminationMode.PhoneFlash => $"FLASH ON AT CAPTURE. Keep the card fixed. Move/tilt the phone toward the {side} side, keep the whole card visible, then capture. The flash will fire with the photo.",
                IlluminationMode.PhoneTorch => $"TORCH ON. Keep the card fixed. Move/tilt the phone toward the {side} side until the reflection sweeps across the card, then capture.",
                _ => $"Move the external light to the {side} side. Keep the card fixed and fully visible."
            };

            HeaderStatusLabel.Text = illuminationMode switch
            {
                IlluminationMode.PhoneFlash => "Reference locked • phone FLASH armed",
                IlluminationMode.PhoneTorch => "Reference locked • phone TORCH ON",
                _ => "Reference locked • external light"
            };

            TopLightMarker.TextColor = currentDirection == SurfaceLightDirection.Top ? active : inactive;
            RightLightMarker.TextColor = currentDirection == SurfaceLightDirection.Right ? active : inactive;
            BottomLightMarker.TextColor = currentDirection == SurfaceLightDirection.Bottom ? active : inactive;
            LeftLightMarker.TextColor = currentDirection == SurfaceLightDirection.Left ? active : inactive;
        }
    }
}
