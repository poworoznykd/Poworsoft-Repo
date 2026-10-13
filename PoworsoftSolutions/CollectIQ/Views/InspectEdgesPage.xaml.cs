using CollectIQ.Services.Inspection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace CollectIQ.Views
{
    public partial class InspectEdgesPage : ContentPage
    {
        private readonly SingleEdgeInspectionService inspectionService = new();
        private readonly Dictionary<EdgePosition, double> sessionScores = new();
        private EdgePosition selectedEdge = EdgePosition.Top;
        private bool cameraReady;
        private bool captureInProgress;
        private bool showingResult;
        private CancellationTokenSource? cameraCts;

        public InspectEdgesPage()
        {
            InitializeComponent();
            UpdateSelectedEdgeUi();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            if (!showingResult)
                await RestartCameraAsync();
        }

        protected override void OnDisappearing()
        {
            ReleaseCamera();
            base.OnDisappearing();
        }

        private void ReleaseCamera()
        {
            cameraCts?.Cancel();
            cameraCts?.Dispose();
            cameraCts = null;
            cameraReady = false;
            try { InspectionCameraView?.StopCameraPreview(); } catch { }
        }

        private async Task RestartCameraAsync()
        {
            ReleaseCamera();
            ShowCamera();
            CaptureButton.IsEnabled = false;
            CameraStatusLabel.Text = "Starting camera…";

            PermissionStatus status = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.Camera>();

            if (status != PermissionStatus.Granted)
            {
                CameraStatusLabel.Text = "Camera permission is required.";
                return;
            }

            cameraCts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            try
            {
                for (int i = 0; i < 30; i++)
                {
                    if (InspectionCameraView.Width > 20 && InspectionCameraView.Height > 20)
                        break;
                    await Task.Delay(80, cameraCts.Token);
                }

                await InspectionCameraView.StartCameraPreview(cameraCts.Token);
                await Task.Delay(300, cameraCts.Token);
                cameraReady = true;
                CaptureButton.IsEnabled = true;
                CameraStatusLabel.Text = $"Place the {selectedEdge.ToString().ToUpperInvariant()} physical edge anywhere inside the yellow band — BACKGROUND on one side, CARD on the other.";
            }
            catch (Exception ex)
            {
                CameraStatusLabel.Text = $"Camera could not start: {ex.Message}";
            }
        }

        private async void OnEdgeSelected(object sender, EventArgs e)
        {
            if (sender is not Button button || button.CommandParameter is not string value ||
                !Enum.TryParse(value, true, out EdgePosition edge))
                return;

            selectedEdge = edge;
            showingResult = false;
            UpdateSelectedEdgeUi();
            await RestartCameraAsync();
        }

        private async void OnCaptureClicked(object sender, EventArgs e)
        {
            if (captureInProgress) return;
            if (!cameraReady)
            {
                await RestartCameraAsync();
                if (!cameraReady) return;
            }

            captureInProgress = true;
            CaptureButton.IsEnabled = false;
            try
            {
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
                using Stream stream = await InspectionCameraView.CaptureImage(timeout.Token);
                string path = await SaveInputAsync(stream, "edge");
                ReleaseCamera();
                await AnalyzeAsync(path);
            }
            catch (Exception ex)
            {
                CameraStatusLabel.Text = $"Capture failed: {ex.Message}";
                CaptureButton.IsEnabled = true;
            }
            finally
            {
                captureInProgress = false;
            }
        }

        private async void OnLoadPhotoClicked(object sender, EventArgs e)
        {
            try
            {
                FileResult? photo = await MediaPicker.Default.PickPhotoAsync();
                if (photo == null) return;

                await using Stream input = await photo.OpenReadAsync();
                string path = await SaveInputAsync(input, "edge_picked", Path.GetExtension(photo.FileName));
                ReleaseCamera();
                await AnalyzeAsync(path);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Edge inspection", ex.Message, "OK");
            }
        }

        private static async Task<string> SaveInputAsync(Stream input, string prefix, string? extension = null)
        {
            string dir = Path.Combine(FileSystem.AppDataDirectory, "EdgeInspections", "Inputs");
            Directory.CreateDirectory(dir);
            string ext = string.IsNullOrWhiteSpace(extension) ? ".jpg" : extension;
            string path = Path.Combine(dir, $"{prefix}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}{ext}");
            await using FileStream output = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output);
            return path;
        }

        private async Task AnalyzeAsync(string path)
        {
            await InspectionDiagnosticLogger.StartRunAsync("SingleEdge", $"Input={path}; Edge={selectedEdge}");
            BusyIndicator.IsVisible = true;
            BusyIndicator.IsRunning = true;
            SummaryLabel.Text = $"Locking the {selectedEdge.ToString().ToUpperInvariant()} physical edge before grading…";

            try
            {
                SingleEdgeInspectionResult result = await InspectionExecution.RunAsync(
                    "SingleEdge",
                    "Close-up edge analysis",
                    cancellationToken => inspectionService.AnalyzeAsync(path, selectedEdge, cancellationToken),
                    TimeSpan.FromSeconds(35));

                sessionScores[selectedEdge] = result.OverallConditionScore;
                ApplyResult(result);
            }
            catch (Exception ex)
            {
                await InspectionDiagnosticLogger.WriteAsync("SingleEdge", "ANALYSIS FAILED", exception: ex);
                showingResult = false;
                ResultPanel.IsVisible = false;
                SummaryLabel.Text = ex.Message;
                await DisplayAlert("Retake edge", ex.Message, "OK");
                await RestartCameraAsync();
            }
            finally
            {
                BusyIndicator.IsVisible = false;
                BusyIndicator.IsRunning = false;
                CaptureButton.IsEnabled = true;
            }
        }

        private void ApplyResult(SingleEdgeInspectionResult result)
        {
            showingResult = true;
            InspectionCameraView.IsVisible = false;
            CameraGuide.IsVisible = false;
            CameraStatusLabel.IsVisible = false;
            ResultImage.IsVisible = true;
            ResultImage.Source = ImageSource.FromFile(result.CloseupImagePath);
            ResultPanel.IsVisible = true;
            AnalysisImage.Source = ImageSource.FromFile(result.AnalysisOverlayPath);

            OverallScoreLabel.Text = $"{result.OverallConditionScore:0}/100";
            GeometryConfidenceLabel.Text = $"Geometry {result.GeometryConfidence:0}/100";
            CaptureQualityLabel.Text = $"Quality {result.CaptureQuality:0}/100";
            StraightnessScoreLabel.Text = $"{result.StraightnessConditionScore:0}/100";
            WhiteningScoreLabel.Text = $"{result.WhiteningConditionScore:0}/100";
            FrayingScoreLabel.Text = $"{result.FrayingConditionScore:0}/100";
            NickScoreLabel.Text = $"{result.NickConditionScore:0}/100";
            MissingScoreLabel.Text = $"{result.MissingMaterialConditionScore:0}/100";
            DeformationScoreLabel.Text = $"{result.DeformationConditionScore:0}/100";

            ResultExplanationLabel.Text = string.Join("\n\n", new[]
            {
                result.Summary,
                result.StraightnessExplanation,
                result.WhiteningExplanation,
                result.FrayingExplanation,
                result.NickExplanation,
                result.MissingMaterialExplanation,
                result.DeformationExplanation
            }.Where(x => !string.IsNullOrWhiteSpace(x)));

            SummaryLabel.Text = result.Summary;
            UpdateSessionLabels();
        }

        private async void OnRetakeClicked(object sender, EventArgs e)
        {
            showingResult = false;
            ResultPanel.IsVisible = false;
            await RestartCameraAsync();
        }

        private async void OnNextEdgeClicked(object sender, EventArgs e)
        {
            selectedEdge = selectedEdge switch
            {
                EdgePosition.Top => EdgePosition.Right,
                EdgePosition.Right => EdgePosition.Bottom,
                EdgePosition.Bottom => EdgePosition.Left,
                _ => EdgePosition.Top
            };

            showingResult = false;
            ResultPanel.IsVisible = false;
            UpdateSelectedEdgeUi();
            await RestartCameraAsync();
        }

        private void ShowCamera()
        {
            InspectionCameraView.IsVisible = true;
            CameraGuide.IsVisible = true;
            CameraStatusLabel.IsVisible = true;
            ResultImage.IsVisible = false;
            ResultImage.Source = null;
            ResultPanel.IsVisible = false;
            UpdateGuideOrientation();
        }

        private void UpdateSelectedEdgeUi()
        {
            SelectedEdgeLabel.Text = $"Inspecting {selectedEdge.ToString().ToUpperInvariant()} edge";
            TopEdgeButton.BackgroundColor = selectedEdge == EdgePosition.Top ? Color.FromArgb("#F59E0B") : Color.FromArgb("#334155");
            RightEdgeButton.BackgroundColor = selectedEdge == EdgePosition.Right ? Color.FromArgb("#F59E0B") : Color.FromArgb("#334155");
            BottomEdgeButton.BackgroundColor = selectedEdge == EdgePosition.Bottom ? Color.FromArgb("#F59E0B") : Color.FromArgb("#334155");
            LeftEdgeButton.BackgroundColor = selectedEdge == EdgePosition.Left ? Color.FromArgb("#F59E0B") : Color.FromArgb("#334155");
            UpdateGuideOrientation();
        }

        private void UpdateGuideOrientation()
        {
            bool horizontal = selectedEdge is EdgePosition.Top or EdgePosition.Bottom;
            GuideBand.WidthRequest = horizontal ? 285 : 18;
            GuideBand.HeightRequest = horizontal ? 18 : 285;
            GuideCenterLine.WidthRequest = horizontal ? 285 : 2;
            GuideCenterLine.HeightRequest = horizontal ? 2 : 285;

            GuideOutsideLabel.TranslationX = 0;
            GuideOutsideLabel.TranslationY = 0;
            GuideCardLabel.TranslationX = 0;
            GuideCardLabel.TranslationY = 0;

            switch (selectedEdge)
            {
                case EdgePosition.Top:
                    GuideOutsideLabel.TranslationY = -35;
                    GuideCardLabel.TranslationY = 35;
                    break;
                case EdgePosition.Bottom:
                    GuideOutsideLabel.TranslationY = 35;
                    GuideCardLabel.TranslationY = -35;
                    break;
                case EdgePosition.Left:
                    GuideOutsideLabel.TranslationX = -55;
                    GuideCardLabel.TranslationX = 55;
                    break;
                case EdgePosition.Right:
                    GuideOutsideLabel.TranslationX = 55;
                    GuideCardLabel.TranslationX = -55;
                    break;
            }
        }

        private void UpdateSessionLabels()
        {
            TopSessionLabel.Text = SessionText("TOP", EdgePosition.Top);
            RightSessionLabel.Text = SessionText("RIGHT", EdgePosition.Right);
            BottomSessionLabel.Text = SessionText("BOTTOM", EdgePosition.Bottom);
            LeftSessionLabel.Text = SessionText("LEFT", EdgePosition.Left);
        }

        private string SessionText(string name, EdgePosition edge)
            => sessionScores.TryGetValue(edge, out double score) ? $"{name} {score:0}" : $"{name} —";
    }
}
