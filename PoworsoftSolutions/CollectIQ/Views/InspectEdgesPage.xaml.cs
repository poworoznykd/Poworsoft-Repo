using System.Diagnostics;
using CollectIQ.Interfaces;
using CollectIQ.Models.Inspection.Geometry;
using CollectIQ.Services.Inspection;
using CollectIQ.Services.Inspection.Geometry;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;

namespace CollectIQ.Views
{
    public partial class InspectEdgesPage : ContentPage
    {
        private readonly CardBoundaryInspectionService inspectionService;
        private bool cameraReady;
        private bool captureInProgress;
        private bool showingResult;
        private CancellationTokenSource? cameraCts;

        private string? pendingManualImagePath;
        private readonly Point?[] manualOuterCorners = new Point?[4];
        private readonly ManualCornerDrawable manualCornerDrawable;
        private int activeManualOuterCornerIndex;
        private double manualCornerScale = 1.0;
        private double manualCornerScaleAtGestureStart = 1.0;
        private double manualCornerTranslationX;
        private double manualCornerTranslationY;
        private double manualCornerPanStartX;
        private double manualCornerPanStartY;
        private bool manualCornerPanMoved;
        private DateTime manualCornerIgnoreTapUntilUtc = DateTime.MinValue;

        private static readonly string[] ManualCornerNames =
        {
            "TOP LEFT", "TOP RIGHT", "BOTTOM RIGHT", "BOTTOM LEFT"
        };

        public InspectEdgesPage()
        {
            InitializeComponent();
            ICardGeometryService geometry = new CardGeometryService();
            inspectionService = new CardBoundaryInspectionService(geometry);
            manualCornerDrawable = new ManualCornerDrawable(manualOuterCorners);
            ManualCornerPickerOverlay.Drawable = manualCornerDrawable;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            if (!showingResult && !ManualCornerPanel.IsVisible)
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
                await Task.Delay(350, cameraCts.Token);
                cameraReady = true;
                CaptureButton.IsEnabled = true;
                CaptureButton.Text = "● CAPTURE";
                CameraStatusLabel.Text = "Keep the entire physical card inside the guide.";
            }
            catch (Exception ex)
            {
                CameraStatusLabel.Text = $"Camera could not start: {ex.Message}";
            }
        }

        private async void OnCaptureClicked(object sender, EventArgs e)
        {
            if (captureInProgress) return;
            if (showingResult)
            {
                showingResult = false;
                await RestartCameraAsync();
                return;
            }

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
                string dir = Path.Combine(FileSystem.AppDataDirectory, "BoundaryInspections", "Inputs");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "edges_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".jpg");
                await using (FileStream output = new(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    await stream.CopyToAsync(output);

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

                string dir = Path.Combine(FileSystem.AppDataDirectory, "BoundaryInspections", "Inputs");
                Directory.CreateDirectory(dir);
                string ext = Path.GetExtension(photo.FileName);
                if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
                string path = Path.Combine(dir, "edges_picked_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ext);
                await using Stream input = await photo.OpenReadAsync();
                await using (FileStream output = new(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    await input.CopyToAsync(output);

                ReleaseCamera();
                await AnalyzeAsync(path);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Inspection", ex.Message, "OK");
            }
        }

        private async Task AnalyzeAsync(string path)
        {
            pendingManualImagePath = path;
            await InspectionDiagnosticLogger.StartRunAsync("Edges", $"Input={path}");

            BusyIndicator.IsVisible = true;
            BusyIndicator.IsRunning = true;
            SummaryLabel.Text = "Finding the physical card with the Centering detector and analyzing the four physical edge bands…";

            try
            {
                CardBoundaryInspectionResult r = await InspectionExecution.RunAsync(
                    "Edges",
                    "Boundary analysis",
                    cancellationToken => inspectionService.AnalyzeAsync(path, cancellationToken),
                    TimeSpan.FromSeconds(50));

                ApplyResult(r, false);
            }
            catch (CardBoundaryGeometryException ex)
            {
                await InspectionDiagnosticLogger.WriteAsync("Edges", "AUTO CARD LOCK FAILED - MANUAL FALLBACK", exception: ex);
                showingResult = false;
                ShowManualCornerPicker(path);
                SummaryLabel.Text = "Automatic Card Lock could not confidently find the physical card. Set the four corners on the captured image, then CollectIQ will flatten the card and continue the edges inspection.";
            }
            catch (Exception ex)
            {
                await InspectionDiagnosticLogger.WriteAsync("Edges", "ANALYSIS FAILED", exception: ex);
                SummaryLabel.Text = ex is TimeoutException
                    ? ex.Message + " The camera has been restored so you can retake the photo."
                    : ex.Message;

                showingResult = false;
                ShowCamera();
                await RestartCameraAsync();
            }
            finally
            {
                BusyIndicator.IsVisible = false;
                BusyIndicator.IsRunning = false;
                CaptureButton.IsEnabled = true;
                await InspectionDiagnosticLogger.WriteAsync("Edges", "BUSY STATE RELEASED");
            }
        }

        private void ApplyResult(CardBoundaryInspectionResult r, bool manualGeometry)
        {
            showingResult = true;
            ShowResult();
            CaptureButton.Text = "↻ RETAKE";
            CaptureButton.IsEnabled = true;
                Score1.Text = Format(r.TopEdge);
                Score2.Text = Format(r.RightEdge);
                Score3.Text = Format(r.LeftEdge);
                Score4.Text = Format(r.BottomEdge);
                ResultImage.Source = ImageSource.FromFile(r.NormalizedImagePath);
                AnalysisImage.Source = ImageSource.FromFile(r.EdgeOverlayPath);
                TopEdgeCloseupImage.Source = ImageSource.FromFile(r.TopEdgeCloseupPath);
                RightEdgeCloseupImage.Source = ImageSource.FromFile(r.RightEdgeCloseupPath);
                BottomEdgeCloseupImage.Source = ImageSource.FromFile(r.BottomEdgeCloseupPath);
                LeftEdgeCloseupImage.Source = ImageSource.FromFile(r.LeftEdgeCloseupPath);
                TopEdgeExplanationLabel.Text = r.TopEdgeExplanation;
                RightEdgeExplanationLabel.Text = r.RightEdgeExplanation;
                BottomEdgeExplanationLabel.Text = r.BottomEdgeExplanation;
                LeftEdgeExplanationLabel.Text = r.LeftEdgeExplanation;
            SummaryLabel.Text = manualGeometry
                ? "Card geometry was set manually. The flattened card is shown above; use the magnified strips to review the strongest machine-vision candidates."
                : $"Physical card detected at {r.DetectionConfidence:0}% confidence. The full card remains visible above; use the magnified strips to inspect each highlighted candidate.";
        }

        private void ShowManualCornerPicker(string path)
        {
            ReleaseCamera();
            CapturePanel.IsVisible = false;
            ManualCornerPanel.IsVisible = true;
            ManualCornerImage.Source = ImageSource.FromFile(path);
            PrepareManualCornerPicker();
        }

        private void PrepareManualCornerPicker()
        {
            for (int i = 0; i < manualOuterCorners.Length; i++)
                manualOuterCorners[i] = null;

            activeManualOuterCornerIndex = 0;
            ManualCornerUseButton.IsEnabled = false;
            ManualCornerInstructionLabel.Text =
                "Pinch to zoom, drag to pan, then tap TOP LEFT on the physical card.";
            ResetManualCornerView();
            ManualCornerPickerOverlay.Invalidate();
        }

        private void OnManualCornerSelectionClicked(object sender, EventArgs e)
        {
            if (sender is not Button button ||
                button.CommandParameter is not string value ||
                !int.TryParse(value, out int index) || index < 0 || index > 3)
                return;

            activeManualOuterCornerIndex = index;
            ManualCornerInstructionLabel.Text = $"Tap {ManualCornerNames[index]} on the physical card.";
        }

        private void OnManualCornerPickerTapped(object sender, TappedEventArgs e)
        {
            if (!ManualCornerPanel.IsVisible ||
                DateTime.UtcNow < manualCornerIgnoreTapUntilUtc ||
                ManualCornerPickerSurface.Width <= 1 || ManualCornerPickerSurface.Height <= 1 ||
                ManualCornerTransformLayer.Width <= 1 || ManualCornerTransformLayer.Height <= 1)
                return;

            Point? position = e.GetPosition(ManualCornerPickerSurface);
            if (position is null) return;

            double surfaceCenterX = ManualCornerPickerSurface.Width / 2.0;
            double surfaceCenterY = ManualCornerPickerSurface.Height / 2.0;
            double layerCenterX = ManualCornerTransformLayer.Width / 2.0;
            double layerCenterY = ManualCornerTransformLayer.Height / 2.0;

            double imageX = ((position.Value.X - surfaceCenterX - manualCornerTranslationX) / manualCornerScale) + layerCenterX;
            double imageY = ((position.Value.Y - surfaceCenterY - manualCornerTranslationY) / manualCornerScale) + layerCenterY;

            if (imageX < 0 || imageX > ManualCornerTransformLayer.Width ||
                imageY < 0 || imageY > ManualCornerTransformLayer.Height)
            {
                ManualCornerInstructionLabel.Text =
                    "That tap is outside the image. Pan the card into view and tap the physical corner again.";
                return;
            }

            double x = Math.Clamp(imageX / ManualCornerTransformLayer.Width, 0.0, 1.0);
            double y = Math.Clamp(imageY / ManualCornerTransformLayer.Height, 0.0, 1.0);
            manualOuterCorners[activeManualOuterCornerIndex] = new Point(x, y);

            bool complete = manualOuterCorners.All(p => p.HasValue);
            ManualCornerUseButton.IsEnabled = complete;

            if (!complete)
            {
                for (int offset = 1; offset <= 4; offset++)
                {
                    int candidate = (activeManualOuterCornerIndex + offset) % 4;
                    if (!manualOuterCorners[candidate].HasValue)
                    {
                        activeManualOuterCornerIndex = candidate;
                        break;
                    }
                }

                ManualCornerInstructionLabel.Text =
                    $"Corner saved. Zoom/pan as needed, then tap {ManualCornerNames[activeManualOuterCornerIndex]}.";
            }
            else
            {
                ManualCornerInstructionLabel.Text =
                    "All four corners are set. Check the green quadrilateral, correct any named corner if needed, then analyze.";
            }

            ManualCornerPickerOverlay.Invalidate();
        }

        private async void OnUseManualCornersClicked(object sender, EventArgs e)
        {
            if (pendingManualImagePath == null || !manualOuterCorners.All(p => p.HasValue))
                return;

            CardPoint[] corners = manualOuterCorners
                .Select(p => new CardPoint((float)p!.Value.X, (float)p.Value.Y))
                .ToArray();

            if (!IsValidManualQuad(corners))
            {
                ManualCornerInstructionLabel.Text =
                    "Those four points do not form a valid card shape. Recheck TOP LEFT → TOP RIGHT → BOTTOM RIGHT → BOTTOM LEFT.";
                return;
            }

            ManualCornerUseButton.IsEnabled = false;
            BusyIndicator.IsVisible = true;
            BusyIndicator.IsRunning = true;
            SummaryLabel.Text = "Flattening the manually locked card and running the edges inspection…";

            try
            {
                CardBoundaryInspectionResult r = await InspectionExecution.RunAsync(
                    "Edges",
                    "Manual boundary analysis",
                    cancellationToken => inspectionService.AnalyzeWithNormalizedCornersAsync(pendingManualImagePath, corners, cancellationToken),
                    TimeSpan.FromSeconds(50));

                ApplyResult(r, true);
            }
            catch (Exception ex)
            {
                await InspectionDiagnosticLogger.WriteAsync("Edges", "MANUAL ANALYSIS FAILED", exception: ex);
                ManualCornerInstructionLabel.Text = ex.Message;
                SummaryLabel.Text = ex.Message;
                ManualCornerUseButton.IsEnabled = true;
            }
            finally
            {
                BusyIndicator.IsVisible = false;
                BusyIndicator.IsRunning = false;
            }
        }

        private static bool IsValidManualQuad(IReadOnlyList<CardPoint> p)
        {
            if (p.Count != 4) return false;

            double area = 0;
            for (int i = 0; i < 4; i++)
            {
                CardPoint a = p[i];
                CardPoint b = p[(i + 1) % 4];
                area += (a.X * b.Y) - (b.X * a.Y);
            }

            if (Math.Abs(area) < 0.02) return false;

            double? sign = null;
            for (int i = 0; i < 4; i++)
            {
                CardPoint a = p[i];
                CardPoint b = p[(i + 1) % 4];
                CardPoint c = p[(i + 2) % 4];
                double cross = ((b.X - a.X) * (c.Y - b.Y)) - ((b.Y - a.Y) * (c.X - b.X));
                if (Math.Abs(cross) < 0.0001) continue;
                double current = Math.Sign(cross);
                sign ??= current;
                if (current != sign.Value) return false;
            }

            return true;
        }

        private void OnManualCornerPickerSurfaceSizeChanged(object sender, EventArgs e)
            => ApplyManualCornerTransform();

        private void OnManualCornerPickerPinchUpdated(object sender, PinchGestureUpdatedEventArgs e)
        {
            if (!ManualCornerPanel.IsVisible) return;

            if (e.Status == GestureStatus.Started)
            {
                manualCornerScaleAtGestureStart = manualCornerScale;
                BoundaryScroll.Orientation = ScrollOrientation.Neither;
                return;
            }

            if (e.Status == GestureStatus.Running)
            {
                manualCornerScale = Math.Clamp(manualCornerScaleAtGestureStart * e.Scale, 1.0, 8.0);
                ClampManualCornerTranslation();
                ApplyManualCornerTransform();
                return;
            }

            BoundaryScroll.Orientation = ScrollOrientation.Vertical;
            manualCornerIgnoreTapUntilUtc = DateTime.UtcNow.AddMilliseconds(180);
        }

        private void OnManualCornerPickerPanUpdated(object sender, PanUpdatedEventArgs e)
        {
            if (!ManualCornerPanel.IsVisible) return;

            if (e.StatusType == GestureStatus.Started)
            {
                manualCornerPanStartX = manualCornerTranslationX;
                manualCornerPanStartY = manualCornerTranslationY;
                manualCornerPanMoved = false;
                BoundaryScroll.Orientation = ScrollOrientation.Neither;
                return;
            }

            if (e.StatusType == GestureStatus.Running)
            {
                if (Math.Abs(e.TotalX) > 3 || Math.Abs(e.TotalY) > 3)
                    manualCornerPanMoved = true;

                manualCornerTranslationX = manualCornerPanStartX + e.TotalX;
                manualCornerTranslationY = manualCornerPanStartY + e.TotalY;
                ClampManualCornerTranslation();
                ApplyManualCornerTransform();
                return;
            }

            BoundaryScroll.Orientation = ScrollOrientation.Vertical;
            if (manualCornerPanMoved)
                manualCornerIgnoreTapUntilUtc = DateTime.UtcNow.AddMilliseconds(180);
        }

        private void OnResetManualCornerViewClicked(object sender, EventArgs e)
            => ResetManualCornerView();

        private void ResetManualCornerView()
        {
            manualCornerScale = 1.0;
            manualCornerScaleAtGestureStart = 1.0;
            manualCornerTranslationX = 0;
            manualCornerTranslationY = 0;
            manualCornerPanStartX = 0;
            manualCornerPanStartY = 0;
            manualCornerPanMoved = false;
            manualCornerIgnoreTapUntilUtc = DateTime.MinValue;
            BoundaryScroll.Orientation = ScrollOrientation.Vertical;
            ApplyManualCornerTransform();
        }

        private void ClampManualCornerTranslation()
        {
            if (ManualCornerPickerSurface.Width <= 1 || ManualCornerPickerSurface.Height <= 1)
                return;

            double maxX = Math.Max(0, (ManualCornerPickerSurface.Width * manualCornerScale - ManualCornerPickerSurface.Width) / 2.0);
            double maxY = Math.Max(0, (ManualCornerPickerSurface.Height * manualCornerScale - ManualCornerPickerSurface.Height) / 2.0);
            manualCornerTranslationX = Math.Clamp(manualCornerTranslationX, -maxX, maxX);
            manualCornerTranslationY = Math.Clamp(manualCornerTranslationY, -maxY, maxY);
        }

        private void ApplyManualCornerTransform()
        {
            ManualCornerTransformLayer.Scale = manualCornerScale;
            ManualCornerTransformLayer.TranslationX = manualCornerTranslationX;
            ManualCornerTransformLayer.TranslationY = manualCornerTranslationY;
        }

        private static string Format(RegionScore s) => $"{s.DamageScore:0}/100 • {s.Label}";

        private void ShowCamera()
        {
            CapturePanel.IsVisible = true;
            ManualCornerPanel.IsVisible = false;
            InspectionCameraView.IsVisible = true;
            CameraGuide.IsVisible = true;
            CameraStatusLabel.IsVisible = true;
            ResultImage.IsVisible = false;
            AnalysisImage.Source = null;
        }

        private void ShowResult()
        {
            CapturePanel.IsVisible = true;
            ManualCornerPanel.IsVisible = false;
            InspectionCameraView.IsVisible = false;
            CameraGuide.IsVisible = false;
            CameraStatusLabel.IsVisible = false;
            ResultImage.IsVisible = true;
        }

        private sealed class ManualCornerDrawable : IDrawable
        {
            private readonly Point?[] points;

            public ManualCornerDrawable(Point?[] points)
            {
                this.points = points;
            }

            public void Draw(ICanvas canvas, RectF dirtyRect)
            {
                PointF?[] actual = points
                    .Select(p => p.HasValue
                        ? new PointF((float)(p.Value.X * dirtyRect.Width), (float)(p.Value.Y * dirtyRect.Height))
                        : (PointF?)null)
                    .ToArray();

                canvas.StrokeColor = Color.FromArgb("#22FF66");
                canvas.StrokeSize = 2;

                for (int i = 0; i < 3; i++)
                {
                    if (actual[i].HasValue && actual[i + 1].HasValue)
                        canvas.DrawLine(actual[i]!.Value, actual[i + 1]!.Value);
                }

                if (actual[3].HasValue && actual[0].HasValue && points.All(p => p.HasValue))
                    canvas.DrawLine(actual[3]!.Value, actual[0]!.Value);

                canvas.FillColor = Color.FromArgb("#FFD60A");
                foreach (PointF? point in actual)
                {
                    if (point.HasValue)
                        canvas.FillCircle(point.Value.X, point.Value.Y, 9);
                }
            }
        }
    }
}
