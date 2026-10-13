using CollectIQ.Services.Inspection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Storage;

namespace CollectIQ.Views
{
    public partial class InspectCornersPage : ContentPage
    {
        private readonly SingleCornerInspectionService inspectionService = new();
        private readonly Dictionary<CornerPosition, double> sessionScores = new();
        private bool cameraReady;
        private bool captureInProgress;
        private bool showingResult;
        private CancellationTokenSource? cameraCts;
        private CornerPosition selectedCorner = CornerPosition.TopLeft;

        public InspectCornersPage()
        {
            InitializeComponent();
            UpdateCornerSelectionUi();
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
            ResultPanel.IsVisible = false;
            CapturePanel.IsVisible = true;
            CaptureButton.IsEnabled = false;
            UpdateCornerGuide();
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
                await Task.Delay(350, cameraCts.Token);
                cameraReady = true;
                CaptureButton.IsEnabled = true;
                CameraStatusLabel.Text = $"Place the {FriendlyName(selectedCorner).ToLowerInvariant()} at the yellow circle and align both physical edges with the yellow L.";
            }
            catch (Exception ex)
            {
                CameraStatusLabel.Text = $"Camera could not start: {ex.Message}";
            }
        }

        private void OnCornerSelected(object sender, EventArgs e)
        {
            if (sender is not Button button ||
                button.CommandParameter is not string value ||
                !Enum.TryParse(value, out CornerPosition parsed))
                return;

            selectedCorner = parsed;
            showingResult = false;
            UpdateCornerSelectionUi();
            SummaryLabel.Text = $"{FriendlyName(selectedCorner)} selected. Capture a tight close-up of that physical corner.";
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
                string path = await SaveInputAsync(stream, "corner_closeup");
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
                string path = await SaveInputAsync(input, "corner_picked", Path.GetExtension(photo.FileName));
                ReleaseCamera();
                await AnalyzeAsync(path);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Corner inspection", ex.Message, "OK");
            }
        }

        private static async Task<string> SaveInputAsync(Stream input, string prefix, string? extension = null)
        {
            string dir = Path.Combine(FileSystem.AppDataDirectory, "CornerInspections", "Inputs");
            Directory.CreateDirectory(dir);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".jpg";
            string path = Path.Combine(dir, $"{prefix}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}{extension}");
            await using FileStream output = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output);
            return path;
        }

        private async Task AnalyzeAsync(string path)
        {
            BusyIndicator.IsVisible = true;
            BusyIndicator.IsRunning = true;
            CapturePanel.IsVisible = false;
            ResultPanel.IsVisible = false;
            SummaryLabel.Text = $"Analyzing the {FriendlyName(selectedCorner).ToLowerInvariant()} close-up…";

            await InspectionDiagnosticLogger.StartRunAsync(
                "SingleCorner",
                $"Corner={selectedCorner}; Input={path}");

            try
            {
                SingleCornerInspectionResult result = await InspectionExecution.RunAsync(
                    "SingleCorner",
                    "Close-up corner analysis",
                    cancellationToken => inspectionService.AnalyzeAsync(path, selectedCorner, cancellationToken),
                    TimeSpan.FromSeconds(35));

                ApplyResult(result);
            }
            catch (Exception ex)
            {
                await InspectionDiagnosticLogger.WriteAsync("SingleCorner", "ANALYSIS FAILED", exception: ex);
                showingResult = false;
                SummaryLabel.Text = ex.Message;
                await DisplayAlert("Retake corner", ex.Message, "OK");
                await RestartCameraAsync();
            }
            finally
            {
                BusyIndicator.IsVisible = false;
                BusyIndicator.IsRunning = false;
            }
        }

        private void ApplyResult(SingleCornerInspectionResult result)
        {
            showingResult = true;
            sessionScores[result.Corner] = result.OverallConditionScore;

            ResultPanel.IsVisible = true;
            CapturePanel.IsVisible = false;
            AnalysisImage.Source = ImageSource.FromFile(result.AnalysisOverlayPath);
            DetectedCornerZoomImage.Source = ImageSource.FromFile(result.DetectedCornerZoomPath);
            DetectedCornerExplanationLabel.Text =
                $"You selected {FriendlyName(result.Corner).ToUpperInvariant()}. The highlighted intersection below is the exact physical corner CollectIQ used for every defect score. If the marker is not on that corner, retake it rather than trusting the grade.";
            OverallScoreLabel.Text = result.OverallConditionScore.ToString("0");
            ResultCornerLabel.Text = FriendlyName(result.Corner).ToUpperInvariant();
            CaptureQualityLabel.Text = $"Geometry {result.GeometryConfidence:0}/100 • Capture quality {result.CaptureQuality:0}/100";
            ResultSummaryLabel.Text = result.Summary;
            SummaryLabel.Text = "Corner analysis complete. Review the breakdown, then retake or move to another corner.";

            SetMetric(ShapeScoreLabel, ShapeExplanationLabel, result.ShapeConditionScore, result.ShapeExplanation);
            SetMetric(FrayingScoreLabel, FrayingExplanationLabel, result.FrayingConditionScore, result.FrayingExplanation);
            SetMetric(WhiteningScoreLabel, WhiteningExplanationLabel, result.WhiteningConditionScore, result.WhiteningExplanation);
            SetMetric(CrushScoreLabel, CrushExplanationLabel, result.CrushBendConditionScore, result.CrushBendExplanation);
            SetMetric(MissingScoreLabel, MissingExplanationLabel, result.MissingMaterialConditionScore, result.MissingMaterialExplanation);
            SetMetric(DeformationScoreLabel, DeformationExplanationLabel, result.DeformationConditionScore, result.DeformationExplanation);
            UpdateSessionScoreLabels();
        }

        private static void SetMetric(Label scoreLabel, Label explanationLabel, double score, string explanation)
        {
            scoreLabel.Text = $"{score:0}/100";
            scoreLabel.TextColor = score >= 85 ? Color.FromArgb("#4ADE80") :
                                   score >= 68 ? Color.FromArgb("#FACC15") :
                                   score >= 48 ? Color.FromArgb("#FB923C") :
                                   Color.FromArgb("#F87171");
            explanationLabel.Text = explanation;
        }

        private async void OnRetakeClicked(object sender, EventArgs e)
        {
            showingResult = false;
            await RestartCameraAsync();
        }

        private async void OnNextCornerClicked(object sender, EventArgs e)
        {
            selectedCorner = selectedCorner switch
            {
                CornerPosition.TopLeft => CornerPosition.TopRight,
                CornerPosition.TopRight => CornerPosition.BottomRight,
                CornerPosition.BottomRight => CornerPosition.BottomLeft,
                _ => CornerPosition.TopLeft
            };

            showingResult = false;
            UpdateCornerSelectionUi();
            await RestartCameraAsync();
        }

        private void UpdateCornerSelectionUi()
        {
            TopLeftButton.BackgroundColor = selectedCorner == CornerPosition.TopLeft ? Color.FromArgb("#7E22CE") : Color.FromArgb("#334155");
            TopRightButton.BackgroundColor = selectedCorner == CornerPosition.TopRight ? Color.FromArgb("#7E22CE") : Color.FromArgb("#334155");
            BottomRightButton.BackgroundColor = selectedCorner == CornerPosition.BottomRight ? Color.FromArgb("#7E22CE") : Color.FromArgb("#334155");
            BottomLeftButton.BackgroundColor = selectedCorner == CornerPosition.BottomLeft ? Color.FromArgb("#7E22CE") : Color.FromArgb("#334155");
            SelectedCornerOverlayLabel.Text = FriendlyName(selectedCorner).ToUpperInvariant();
            UpdateCornerGuide();
        }

        private void UpdateCornerGuide()
        {
            if (GuideHorizontalLine == null || GuideVerticalLine == null || GuideCornerMarker == null)
                return;

            const double center = 130;
            const double arm = 112;
            const double thickness = 4;
            const double marker = 28;

            double horizontalX = selectedCorner is CornerPosition.TopLeft or CornerPosition.BottomLeft
                ? center
                : center - arm;
            double horizontalY = center - (thickness / 2);
            double verticalX = center - (thickness / 2);
            double verticalY = selectedCorner is CornerPosition.TopLeft or CornerPosition.TopRight
                ? center
                : center - arm;

            AbsoluteLayout.SetLayoutFlags(GuideHorizontalLine, AbsoluteLayoutFlags.None);
            AbsoluteLayout.SetLayoutFlags(GuideVerticalLine, AbsoluteLayoutFlags.None);
            AbsoluteLayout.SetLayoutFlags(GuideCornerMarker, AbsoluteLayoutFlags.None);
            AbsoluteLayout.SetLayoutBounds(GuideHorizontalLine, new Microsoft.Maui.Graphics.Rect(horizontalX, horizontalY, arm, thickness));
            AbsoluteLayout.SetLayoutBounds(GuideVerticalLine, new Microsoft.Maui.Graphics.Rect(verticalX, verticalY, thickness, arm));
            AbsoluteLayout.SetLayoutBounds(GuideCornerMarker, new Microsoft.Maui.Graphics.Rect(center - marker / 2, center - marker / 2, marker, marker));
        }

        private void UpdateSessionScoreLabels()
        {
            TopLeftSessionLabel.Text = SessionText("TL", CornerPosition.TopLeft);
            TopRightSessionLabel.Text = SessionText("TR", CornerPosition.TopRight);
            BottomRightSessionLabel.Text = SessionText("BR", CornerPosition.BottomRight);
            BottomLeftSessionLabel.Text = SessionText("BL", CornerPosition.BottomLeft);
        }

        private string SessionText(string prefix, CornerPosition corner)
            => sessionScores.TryGetValue(corner, out double score) ? $"{prefix} {score:0}" : $"{prefix} —";

        private static string FriendlyName(CornerPosition corner) => corner switch
        {
            CornerPosition.TopLeft => "Top Left",
            CornerPosition.TopRight => "Top Right",
            CornerPosition.BottomRight => "Bottom Right",
            CornerPosition.BottomLeft => "Bottom Left",
            _ => "Corner"
        };
    }
}
