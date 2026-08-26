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

        public InspectCenteringPage()
        {
            InitializeComponent();
            BindingContext = ServiceHelper.Services?.GetService(typeof(InspectCenteringViewModel)) as InspectCenteringViewModel
                ?? new InspectCenteringViewModel();
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
                CaptureInstructionLabel.Text = "STEP 2 — Photo captured. CollectIQ is analyzing the outer card and inner frame now…";

                // Analyze immediately. The user should not have to discover another button.
                await ViewModel.AnalyzeLoadedImageAsync();
                CaptureInstructionLabel.Text = "STEP 3 — Review the purple/yellow guides and centering scores below. If a line is off, use Manual Adjustments; otherwise you are done.";
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
                CaptureInstructionLabel.Text = "Loaded photo — CollectIQ is analyzing it now…";
                await ViewModel.AnalyzeLoadedImageAsync();
                CaptureInstructionLabel.Text = "Review the centering guides and scores below. Retake or use Manual Adjustments if needed.";
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

        private void ShowCapturedImage()
        {
            CenteringCameraView.IsVisible = false;
            CenteringCameraGuide.IsVisible = false;
            CenteringResultImage.IsVisible = true;
            CameraStatusLabel.IsVisible = false;
            RestartCameraButton.IsVisible = false;
        }
    }
}
