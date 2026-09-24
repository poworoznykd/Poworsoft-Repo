using System.Diagnostics;
using CollectIQ.Helpers;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace CollectIQ.Views
{
    public partial class InspectCenteringPage : ContentPage
    {
        private InspectCenteringViewModel ViewModel => (InspectCenteringViewModel)BindingContext;
        private bool cameraReady;
        private bool captureInProgress;
        private bool showingCapturedImage;
        private CancellationTokenSource? cameraStartCancellationTokenSource;

        private double measurementScale = 1.0;
        private double measurementScaleAtGestureStart = 1.0;
        private double measurementTranslationX;
        private double measurementTranslationY;
        private double measurementPanStartX;
        private double measurementPanStartY;

        private int activeGuideStartPixel;

        private Image? MeasurementImageControl =>
            this.FindByName<Image>("CenteringMeasurementImage");

        public InspectCenteringPage()
        {
            InitializeComponent();
            BindingContext = ServiceHelper.Services?.GetService(typeof(InspectCenteringViewModel)) as InspectCenteringViewModel
                ?? new InspectCenteringViewModel();

            ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(InspectCenteringViewModel.HasAnalysis) ||
                    args.PropertyName?.EndsWith("GuidePixel", StringComparison.Ordinal) == true)
                {
                    MainThread.BeginInvokeOnMainThread(UpdateGuideVisuals);
                }

                if (args.PropertyName == nameof(InspectCenteringViewModel.NeedsManualOuterCard) ||
                    args.PropertyName?.StartsWith("ManualOuter", StringComparison.Ordinal) == true)
                {
                    MainThread.BeginInvokeOnMainThread(UpdateManualOuterGuideVisuals);
                }
            };
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            showingCapturedImage = false;
            ShowLiveCamera();
            await RestartCameraAsync();
        }

        protected override void OnDisappearing()
        {
            ReleaseCamera();
            base.OnDisappearing();
        }

        private void ReleaseCamera()
        {
            cameraStartCancellationTokenSource?.Cancel();
            cameraStartCancellationTokenSource?.Dispose();
            cameraStartCancellationTokenSource = null;
            cameraReady = false;
            try { CenteringCameraView?.StopCameraPreview(); } catch { }
        }

        private async Task<bool> EnsureCameraPermissionAsync()
        {
            PermissionStatus status = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.Camera>();

            if (status == PermissionStatus.Granted)
                return true;

            CameraStatusLabel.Text = "Camera permission is required. Enable Camera permission for CollectIQ in Android settings.";
            CenteringShutterButton.IsEnabled = false;
            return false;
        }

        private async Task RestartCameraAsync()
        {
            cameraReady = false;
            CenteringShutterButton.IsEnabled = false;
            CameraStatusLabel.IsVisible = true;
            CameraStatusLabel.Text = "Starting camera…";

            if (!await EnsureCameraPermissionAsync()) return;

            cameraStartCancellationTokenSource?.Cancel();
            cameraStartCancellationTokenSource?.Dispose();
            cameraStartCancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(12));

            try
            {
                try { CenteringCameraView.StopCameraPreview(); } catch { }
                await Task.Delay(250, cameraStartCancellationTokenSource.Token);
                await WaitForCameraLayoutAsync(cameraStartCancellationTokenSource.Token);
                await CenteringCameraView.StartCameraPreview(cameraStartCancellationTokenSource.Token);
                await Task.Delay(450, cameraStartCancellationTokenSource.Token);

                cameraReady = true;
                CenteringShutterButton.IsEnabled = true;
                CenteringShutterButton.Text = "●  CAPTURE CARD";
                CameraStatusLabel.Text = "STEP 1 — Fit the entire card inside the purple guide, then tap CAPTURE CARD.";
            }
            catch (OperationCanceledException)
            {
                CameraStatusLabel.Text = "Camera start timed out. Tap RESTART CAMERA.";
                RestartCameraButton.IsVisible = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CenteringCamera] Start failed: {ex}");
                CameraStatusLabel.Text = $"Camera could not start: {ex.Message}";
                RestartCameraButton.IsVisible = true;
            }
        }

        private async Task WaitForCameraLayoutAsync(CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (CenteringCameraView.IsVisible && CenteringCameraView.Width > 20 && CenteringCameraView.Height > 20) return;
                await Task.Delay(80, cancellationToken);
            }
            throw new InvalidOperationException("The centering camera view was not ready on screen.");
        }

        private async void OnTakePhotoClicked(object sender, EventArgs e)
        {
            if (captureInProgress) return;

            // When a result is displayed, the same large button becomes Retake.
            if (showingCapturedImage)
            {
                showingCapturedImage = false;
                ShowLiveCamera();
                await RestartCameraAsync();
                return;
            }

            if (!cameraReady)
            {
                ShowLiveCamera();
                await RestartCameraAsync();
                if (!cameraReady) return;
            }

            captureInProgress = true;
            CenteringShutterButton.IsEnabled = false;
            CameraStatusLabel.Text = "Capturing… hold the phone still.";

            try
            {
                using CancellationTokenSource captureTimeout = new(TimeSpan.FromSeconds(10));
                using Stream imageStream = await CenteringCameraView.CaptureImage(captureTimeout.Token);

                string directory = Path.Combine(FileSystem.AppDataDirectory, "Centering", "Inputs");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, $"captured_front_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.jpg");

                await using (FileStream output = new(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    await imageStream.CopyToAsync(output);

                if (!File.Exists(path) || new FileInfo(path).Length < 1024)
                    throw new IOException("The camera returned an empty image.");

                try { CenteringCameraView.StopCameraPreview(); } catch { }
                cameraReady = false;

                ViewModel.LoadCapturedImage(path);
                showingCapturedImage = true;
                ShowCapturedImage();
                CenteringShutterButton.Text = "↻  RETAKE PHOTO";
                CenteringShutterButton.IsEnabled = true;
                CaptureInstructionLabel.Text = "STEP 2 — Photo captured. Watch the INSPECTION STAGE below while CollectIQ detects, locks and normalizes the card…";

                // Analyze immediately. The user should not have to discover another button.
                await ViewModel.AnalyzeLoadedImageAsync();

                if (ViewModel.HasAnalysis)
                {
                    CaptureInstructionLabel.Text =
                        "STEP 3 — Automatic centering is complete. Review Original Capture → Card Lock View → TrueForm View → Centering Map. Use ADJUST CENTERING only if a guide needs correction.";
                    UpdateGuideSurfaceSize();
                    UpdateGuideVisuals();
                }
                else
                {
                    CaptureInstructionLabel.Text = ViewModel.NeedsManualOuterCard
                        ? "Automatic Card Lock did not finish. Use MANUAL CARD LOCK below instead of retaking: move the four green lines to the physical card edges and continue."
                        : "Centering could not create the normalized card. Read the RESULT below.";
                }
            }
            catch (OperationCanceledException)
            {
                CameraStatusLabel.Text = "Capture timed out. Tap RESTART CAMERA and try again.";
                RestartCameraButton.IsVisible = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CenteringCamera] Capture failed: {ex}");
                CameraStatusLabel.Text = $"Capture failed: {ex.Message}";
                RestartCameraButton.IsVisible = true;
            }
            finally
            {
                captureInProgress = false;
                if (!showingCapturedImage) CenteringShutterButton.IsEnabled = cameraReady;
            }
        }

        private async void OnRestartCameraClicked(object sender, EventArgs e)
        {
            showingCapturedImage = false;
            RestartCameraButton.IsVisible = false;
            ShowLiveCamera();
            await RestartCameraAsync();
        }

        private async void OnLoadPhotoClicked(object sender, EventArgs e)
        {
            try
            {
                FileResult? photo = await MediaPicker.Default.PickPhotoAsync(new MediaPickerOptions { Title = "Select a front card image" });
                if (photo == null) return;

                string extension = Path.GetExtension(photo.FileName);
                if (string.IsNullOrWhiteSpace(extension)) extension = ".jpg";
                string directory = Path.Combine(FileSystem.AppDataDirectory, "Centering", "Inputs");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, $"picked_front_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}{extension}");

                await using Stream source = await photo.OpenReadAsync();
                await using (FileStream output = new(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    await source.CopyToAsync(output);

                ReleaseCamera();
                ViewModel.LoadCapturedImage(path);
                showingCapturedImage = true;
                ShowCapturedImage();
                CenteringShutterButton.Text = "↻  RETAKE WITH CAMERA";
                CenteringShutterButton.IsEnabled = true;
                CaptureInstructionLabel.Text = "Loaded photo — automatic card detection and normalization are running now…";
                await ViewModel.AnalyzeLoadedImageAsync();

                if (ViewModel.HasAnalysis)
                {
                    CaptureInstructionLabel.Text =
                        "Automatic centering is complete. Review Original Capture → Card Lock View → TrueForm View → Centering Map. Manual adjustment is optional.";
                    UpdateGuideSurfaceSize();
                    UpdateGuideVisuals();
                }
                else
                {
                    CaptureInstructionLabel.Text = ViewModel.NeedsManualOuterCard
                        ? "Automatic Card Lock did not finish. Use MANUAL CARD LOCK below: position the four green physical-card lines and continue."
                        : "Centering could not create the normalized card. Read the RESULT below.";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CenteringCamera] Pick image failed: {ex}");
                await DisplayAlert("Centering", $"The image could not be loaded: {ex.Message}", "OK");
            }
        }

        private void ShowLiveCamera()
        {
            CenteringCameraView.IsVisible = true;
            CenteringCameraGuide.IsVisible = true;
            CenteringResultImage.IsVisible = false;
            CameraStatusLabel.IsVisible = true;
            CaptureInstructionLabel.Text = "Place ONE card on a plain, contrasting background. Keep all four card edges visible and avoid glare.";
        }

        private int manualOuterGuideStartPixel;
        private bool manualGuideEditing;

        private void OnManualGuideEditClicked(object sender, EventArgs e)
        {
            SetManualGuideEditing(!manualGuideEditing);
        }

        private void SetManualGuideEditing(bool enabled)
        {
            manualGuideEditing = enabled;
            // Never change ScrollView orientation while the user is looking at
            // the capture: Android can remeasure/jump the viewport and hide it.
            // The fixed-size guide surface owns its own pan gestures.
            Button? editButton = this.FindByName<Button>("ManualGuideEditButton");
            if (editButton != null)
            {
                editButton.Text = enabled
                    ? "DONE MOVING LINES — ENABLE PAGE SCROLL"
                    : "EDIT GREEN LINES — LOCK PAGE SCROLL";
            }
        }


        private void OnManualOuterSliderValueChanged(object sender, ValueChangedEventArgs e)
        {
            if (sender is not Slider slider || !ViewModel.NeedsManualOuterCard)
                return;

            string? name = slider.AutomationId switch
            {
                "ManualOuterLeftSlider" => "Left",
                "ManualOuterRightSlider" => "Right",
                "ManualOuterTopSlider" => "Top",
                "ManualOuterBottomSlider" => "Bottom",
                _ => null
            };

            if (name is null) return;
            ViewModel.SetManualOuterGuide(name, e.NewValue);
            UpdateManualOuterGuideVisuals();
        }

        private void OnManualOuterGuideSurfaceSizeChanged(object sender, EventArgs e)
        {
            UpdateManualOuterGuideVisuals();
        }

        private void UpdateManualOuterGuideVisuals()
        {
            if (ManualOuterGuideSurface == null ||
                ManualOuterGuideSurface.Width <= 1 ||
                ManualOuterGuideSurface.Height <= 1)
                return;

            ManualOuterLeftGuide.TranslationX =
                (ViewModel.ManualOuterLeft * ManualOuterGuideSurface.Width) -
                (ManualOuterLeftGuide.WidthRequest / 2.0);

            ManualOuterRightGuide.TranslationX =
                (ViewModel.ManualOuterRight * ManualOuterGuideSurface.Width) -
                (ManualOuterRightGuide.WidthRequest / 2.0);

            ManualOuterTopGuide.TranslationY =
                (ViewModel.ManualOuterTop * ManualOuterGuideSurface.Height) -
                (ManualOuterTopGuide.HeightRequest / 2.0);

            ManualOuterBottomGuide.TranslationY =
                (ViewModel.ManualOuterBottom * ManualOuterGuideSurface.Height) -
                (ManualOuterBottomGuide.HeightRequest / 2.0);
        }

        private void HandleManualOuterGuidePan(
            string guideName,
            bool vertical,
            PanUpdatedEventArgs e)
        {
            if (!ViewModel.NeedsManualOuterCard ||
                !manualGuideEditing ||
                ManualOuterGuideSurface == null)
                return;

            double displaySize = vertical
                ? ManualOuterGuideSurface.Width
                : ManualOuterGuideSurface.Height;

            if (displaySize <= 1)
                return;

            if (e.StatusType == GestureStatus.Started)
            {
                manualOuterGuideStartPixel = (int)Math.Round(
                    ViewModel.GetManualOuterGuide(guideName) * displaySize);
                return;
            }

            if (e.StatusType == GestureStatus.Running)
            {
                double delta = vertical ? e.TotalX : e.TotalY;
                double normalized =
                    (manualOuterGuideStartPixel + delta) / displaySize;

                ViewModel.SetManualOuterGuide(guideName, normalized);
                UpdateManualOuterGuideVisuals();
            }
        }

        private void OnManualOuterLeftPanUpdated(object sender, PanUpdatedEventArgs e) =>
            HandleManualOuterGuidePan("Left", true, e);

        private void OnManualOuterRightPanUpdated(object sender, PanUpdatedEventArgs e) =>
            HandleManualOuterGuidePan("Right", true, e);

        private void OnManualOuterTopPanUpdated(object sender, PanUpdatedEventArgs e) =>
            HandleManualOuterGuidePan("Top", false, e);

        private void OnManualOuterBottomPanUpdated(object sender, PanUpdatedEventArgs e) =>
            HandleManualOuterGuidePan("Bottom", false, e);

        private async void OnContinueManualOuterEdgesClicked(object sender, EventArgs e)
        {
            SetManualGuideEditing(false);
            CaptureInstructionLabel.Text =
                "Using your manual card edges → Card Lock → TrueForm → automatic centering…";

            await ViewModel.ContinueWithManualOuterEdgesAsync();

            if (ViewModel.HasAnalysis)
            {
                CaptureInstructionLabel.Text =
                    "Centering complete from your manual Card Lock. Review TrueForm and Centering Map; ADJUST CENTERING remains available.";
                UpdateGuideSurfaceSize();
                UpdateGuideVisuals();
            }
            else
            {
                CaptureInstructionLabel.Text =
                    "TrueForm was not created. Your four manual outer-card lines are still available below—adjust them and retry.";
                UpdateManualOuterGuideVisuals();
            }
        }

        private void OnCenteringGuideHostSizeChanged(object sender, EventArgs e)
        {
            UpdateGuideSurfaceSize();
            UpdateGuideVisuals();
        }

        private void UpdateGuideSurfaceSize()
        {
            if (CenteringGuideHost == null || CenteringGuideSurface == null)
                return;

            double availableWidth = CenteringGuideHost.Width;
            if (availableWidth <= 20)
                return;

            // Normalized inspection card is exactly 5:7.
            double width = Math.Min(availableWidth, 460.0 * 5.0 / 7.0);
            double height = width * 7.0 / 5.0;

            if (height > 460.0)
            {
                height = 460.0;
                width = height * 5.0 / 7.0;
            }

            CenteringGuideSurface.WidthRequest = width;
            CenteringGuideSurface.HeightRequest = height;
        }

        private void UpdateGuideVisuals()
        {
            if (!ViewModel.HasAnalysis ||
                CenteringGuideSurface == null ||
                CenteringGuideSurface.Width <= 1 ||
                CenteringGuideSurface.Height <= 1)
                return;

            double xScale = CenteringGuideSurface.Width / ViewModel.CanonicalWidthPixels;
            double yScale = CenteringGuideSurface.Height / ViewModel.CanonicalHeightPixels;

            SetVerticalGuide(OuterLeftGuide, ViewModel.OuterLeftGuidePixel * xScale);
            SetVerticalGuide(OuterRightGuide, ViewModel.OuterRightGuidePixel * xScale);
            SetHorizontalGuide(OuterTopGuide, ViewModel.OuterTopGuidePixel * yScale);
            SetHorizontalGuide(OuterBottomGuide, ViewModel.OuterBottomGuidePixel * yScale);

            SetVerticalGuide(InnerLeftGuide, ViewModel.InnerLeftGuidePixel * xScale);
            SetVerticalGuide(InnerRightGuide, ViewModel.InnerRightGuidePixel * xScale);
            SetHorizontalGuide(InnerTopGuide, ViewModel.InnerTopGuidePixel * yScale);
            SetHorizontalGuide(InnerBottomGuide, ViewModel.InnerBottomGuidePixel * yScale);
        }

        private static void SetVerticalGuide(VisualElement guide, double x)
        {
            guide.TranslationX = x - (guide.WidthRequest / 2.0);
        }

        private static void SetHorizontalGuide(VisualElement guide, double y)
        {
            guide.TranslationY = y - (guide.HeightRequest / 2.0);
        }

        private void HandleGuidePan(
            string guideName,
            bool vertical,
            PanUpdatedEventArgs e)
        {
            if (!ViewModel.HasAnalysis || !ViewModel.IsManualMode)
                return;

            if (e.StatusType == GestureStatus.Started)
            {
                activeGuideStartPixel = ViewModel.GetGuidePosition(guideName);
                return;
            }

            if (e.StatusType == GestureStatus.Running)
            {
                double displaySize = vertical
                    ? CenteringGuideSurface.Width
                    : CenteringGuideSurface.Height;

                int canonicalSize = vertical
                    ? ViewModel.CanonicalWidthPixels
                    : ViewModel.CanonicalHeightPixels;

                if (displaySize <= 1)
                    return;

                double displayDelta = vertical ? e.TotalX : e.TotalY;
                int canonicalDelta = (int)Math.Round(displayDelta * canonicalSize / displaySize);

                ViewModel.SetGuidePosition(
                    guideName,
                    activeGuideStartPixel + canonicalDelta);

                UpdateGuideVisuals();
            }
        }

        private void OnOuterLeftPanUpdated(object sender, PanUpdatedEventArgs e) =>
            HandleGuidePan("OuterLeft", true, e);

        private void OnOuterRightPanUpdated(object sender, PanUpdatedEventArgs e) =>
            HandleGuidePan("OuterRight", true, e);

        private void OnOuterTopPanUpdated(object sender, PanUpdatedEventArgs e) =>
            HandleGuidePan("OuterTop", false, e);

        private void OnOuterBottomPanUpdated(object sender, PanUpdatedEventArgs e) =>
            HandleGuidePan("OuterBottom", false, e);

        private void OnInnerLeftPanUpdated(object sender, PanUpdatedEventArgs e) =>
            HandleGuidePan("InnerLeft", true, e);

        private void OnInnerRightPanUpdated(object sender, PanUpdatedEventArgs e) =>
            HandleGuidePan("InnerRight", true, e);

        private void OnInnerTopPanUpdated(object sender, PanUpdatedEventArgs e) =>
            HandleGuidePan("InnerTop", false, e);

        private void OnInnerBottomPanUpdated(object sender, PanUpdatedEventArgs e) =>
            HandleGuidePan("InnerBottom", false, e);

        private void OnResetGuidesClicked(object sender, EventArgs e)
        {
            ViewModel.ResetManualGuides();
            UpdateGuideVisuals();
        }

        private void OnToggleGuideLockClicked(object sender, EventArgs e)
        {
            if (!ViewModel.HasAnalysis)
                return;

            ViewModel.IsManualMode = !ViewModel.IsManualMode;
            ViewModel.StatusMessage = ViewModel.IsManualMode
                ? "Manual correction enabled. Drag any green or yellow guide; the centering result recalculates immediately."
                : "Manual correction locked. The current guide positions and centering result are preserved.";
        }

        private void ShowCapturedImage()
        {
            ResetMeasurementViewer();
            CenteringCameraView.IsVisible = false;
            CenteringCameraGuide.IsVisible = false;
            CenteringResultImage.IsVisible = true;
            CameraStatusLabel.IsVisible = false;
            RestartCameraButton.IsVisible = false;
        }

        private void OnMeasurementPinchUpdated(object sender, PinchGestureUpdatedEventArgs e)
        {
            Image? image = MeasurementImageControl;
            if (image == null)
                return;

            switch (e.Status)
            {
                case GestureStatus.Started:
                    measurementScaleAtGestureStart = measurementScale;
                    break;

                case GestureStatus.Running:
                    measurementScale = Math.Clamp(measurementScaleAtGestureStart * e.Scale, 1.0, 8.0);
                    image.Scale = measurementScale;

                    if (measurementScale <= 1.001)
                    {
                        measurementTranslationX = 0;
                        measurementTranslationY = 0;
                        image.TranslationX = 0;
                        image.TranslationY = 0;
                    }
                    break;

                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    ClampMeasurementTranslation();
                    break;
            }
        }

        private void OnMeasurementPanUpdated(object sender, PanUpdatedEventArgs e)
        {
            if (MeasurementImageControl == null || measurementScale <= 1.001)
                return;

            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    measurementPanStartX = measurementTranslationX;
                    measurementPanStartY = measurementTranslationY;
                    break;

                case GestureStatus.Running:
                    measurementTranslationX = measurementPanStartX + e.TotalX;
                    measurementTranslationY = measurementPanStartY + e.TotalY;
                    ClampMeasurementTranslation();
                    break;

                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    ClampMeasurementTranslation();
                    break;
            }
        }

        private void ClampMeasurementTranslation()
        {
            Image? image = MeasurementImageControl;
            if (image == null)
                return;

            if (measurementScale <= 1.001)
            {
                measurementTranslationX = 0;
                measurementTranslationY = 0;
            }
            else
            {
                double maxX = Math.Max(0, (image.Width * (measurementScale - 1.0)) / 2.0);
                double maxY = Math.Max(0, (image.Height * (measurementScale - 1.0)) / 2.0);

                measurementTranslationX = Math.Clamp(measurementTranslationX, -maxX, maxX);
                measurementTranslationY = Math.Clamp(measurementTranslationY, -maxY, maxY);
            }

            image.TranslationX = measurementTranslationX;
            image.TranslationY = measurementTranslationY;
        }

        private void OnResetMeasurementZoomClicked(object sender, EventArgs e) => ResetMeasurementViewer();

        private void OnResetMeasurementZoomTapped(object sender, TappedEventArgs e) => ResetMeasurementViewer();

        private void ResetMeasurementViewer()
        {
            measurementScale = 1.0;
            measurementScaleAtGestureStart = 1.0;
            measurementTranslationX = 0;
            measurementTranslationY = 0;
            measurementPanStartX = 0;
            measurementPanStartY = 0;

            Image? image = MeasurementImageControl;
            if (image != null)
            {
                image.Scale = 1.0;
                image.TranslationX = 0;
                image.TranslationY = 0;
            }
        }

    }
}
