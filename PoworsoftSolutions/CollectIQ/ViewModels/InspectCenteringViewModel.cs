using CollectIQ.Interfaces;
using CollectIQ.Models.Inspection.Geometry;
using CollectIQ.Services.Inspection.Geometry;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ImageSharpImage = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace CollectIQ.Views
{
    public class InspectCenteringViewModel : INotifyPropertyChanged
    {
        private const int CanonicalWidth = 750;
        private const int CanonicalHeight = 1050;

        private readonly ICardGeometryService geometryService;

        private string? selectedImagePath;
        private string? rawDisplayPath;
        private string? canonicalImagePath;
        private string? overlayDisplayPath;
        private string? outputDirectory;
        private ImageSource? cardImageSource;
        private string centeringSummary = "Capture or load a clear front card photo and tap Auto Analyze.";
        private string horizontalCenteringText = "Not analyzed";
        private string verticalCenteringText = "Not analyzed";
        private string recommendation = "Use a full front photo on a solid background so the card edges are easy to detect.";
        private string statusMessage = "No image selected.";
        private double zoomLevel = 1.0;
        private double tolerance = 3.0;
        private bool isBusy;
        private bool hasAnalysis;
        private bool showingOverlay = true;
        private bool isManualMode;
        private double confidencePercent;
        private double leftAdjust;
        private double rightAdjust;
        private double topAdjust;
        private double bottomAdjust;

        private CenteringMeasurement baseMeasurement = new();
        private CenteringMeasurement currentMeasurement = new();

        public InspectCenteringViewModel() : this(new CardGeometryService()) { }

        public InspectCenteringViewModel(ICardGeometryService geometryService)
        {
            this.geometryService = geometryService;
            CapturePhotoCommand = new Command(async () => await ExecuteCapturePhotoAsync(), () => !IsBusy);
            PickImageCommand = new Command(async () => await ExecutePickImageAsync(), () => !IsBusy);
            AnalyzeCommand = new Command(async () => await ExecuteAnalyzeAsync(), () => !IsBusy);
            ToggleOverlayCommand = new Command(ExecuteToggleOverlay, () => !IsBusy && HasAnalysis);
            ToggleManualModeCommand = new Command(ExecuteToggleManualMode, () => !IsBusy && HasAnalysis);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ImageSource? CardImageSource { get => cardImageSource; set => SetProperty(ref cardImageSource, value); }
        public string CenteringSummary { get => centeringSummary; set => SetProperty(ref centeringSummary, value); }
        public string HorizontalCenteringText { get => horizontalCenteringText; set => SetProperty(ref horizontalCenteringText, value); }
        public string VerticalCenteringText { get => verticalCenteringText; set => SetProperty(ref verticalCenteringText, value); }
        public string Recommendation { get => recommendation; set => SetProperty(ref recommendation, value); }
        public string StatusMessage { get => statusMessage; set => SetProperty(ref statusMessage, value); }
        public double ZoomLevel { get => zoomLevel; set => SetProperty(ref zoomLevel, value); }
        public double Tolerance { get => tolerance; set { if (SetProperty(ref tolerance, value) && HasAnalysis) UpdateRecommendation(); } }
        public bool HasAnalysis { get => hasAnalysis; set { if (SetProperty(ref hasAnalysis, value)) RaiseCanExecutes(); } }
        public bool IsBusy { get => isBusy; set { if (SetProperty(ref isBusy, value)) RaiseCanExecutes(); } }
        public bool IsManualMode { get => isManualMode; set => SetProperty(ref isManualMode, value); }
        public double ConfidencePercent { get => confidencePercent; set => SetProperty(ref confidencePercent, value); }
        public double LeftAdjust { get => leftAdjust; set { if (SetProperty(ref leftAdjust, value) && HasAnalysis && IsManualMode) ApplyManualAdjustments(); } }
        public double RightAdjust { get => rightAdjust; set { if (SetProperty(ref rightAdjust, value) && HasAnalysis && IsManualMode) ApplyManualAdjustments(); } }
        public double TopAdjust { get => topAdjust; set { if (SetProperty(ref topAdjust, value) && HasAnalysis && IsManualMode) ApplyManualAdjustments(); } }
        public double BottomAdjust { get => bottomAdjust; set { if (SetProperty(ref bottomAdjust, value) && HasAnalysis && IsManualMode) ApplyManualAdjustments(); } }

        public ICommand CapturePhotoCommand { get; }
        public ICommand PickImageCommand { get; }
        public ICommand AnalyzeCommand { get; }
        public ICommand ToggleOverlayCommand { get; }
        public ICommand ToggleManualModeCommand { get; }

        private void RaiseCanExecutes()
        {
            (CapturePhotoCommand as Command)?.ChangeCanExecute();
            (PickImageCommand as Command)?.ChangeCanExecute();
            (AnalyzeCommand as Command)?.ChangeCanExecute();
            (ToggleOverlayCommand as Command)?.ChangeCanExecute();
            (ToggleManualModeCommand as Command)?.ChangeCanExecute();
        }

        private async Task ExecuteCapturePhotoAsync()
        {
            if (IsBusy) return;
            try
            {
                FileResult? photo = await MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions
                {
                    Title = "Capture a front card photo"
                });
                if (photo == null) return;
                await LoadPickedFileAsync(photo, "captured_front");
                StatusMessage = "Photo captured. Tap Auto Analyze to detect the card and estimate centering.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not capture a photo: {ex.Message}";
            }
        }

        private async Task ExecutePickImageAsync()
        {
            if (IsBusy) return;
            try
            {
                FileResult? photo = await MediaPicker.Default.PickPhotoAsync(new MediaPickerOptions
                {
                    Title = "Select a front card image"
                });
                if (photo == null) return;
                await LoadPickedFileAsync(photo, "picked_front");
                StatusMessage = "Image loaded. Tap Auto Analyze to detect the card and estimate centering.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not load the image: {ex.Message}";
            }
        }

        private async Task LoadPickedFileAsync(FileResult photo, string prefix)
        {
            string ext = Path.GetExtension(photo.FileName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
            string targetDir = Path.Combine(FileSystem.AppDataDirectory, "Centering", "Inputs");
            Directory.CreateDirectory(targetDir);
            string localPath = Path.Combine(targetDir, $"{prefix}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}{ext}");
            await using Stream source = await photo.OpenReadAsync();
            await using FileStream destination = new(localPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(destination);

            selectedImagePath = localPath;
            rawDisplayPath = localPath;
            canonicalImagePath = null;
            overlayDisplayPath = null;
            CardImageSource = ImageSource.FromFile(localPath);
            HasAnalysis = false;
            IsManualMode = false;
            ConfidencePercent = 0;
            CenteringSummary = "Ready to analyze.";
            HorizontalCenteringText = "Not analyzed";
            VerticalCenteringText = "Not analyzed";
            Recommendation = "Tap Auto Analyze to find the card edges and estimate centering.";
            ResetAdjustments();
        }

        private async Task ExecuteAnalyzeAsync()
        {
            if (IsBusy) return;
            if (string.IsNullOrWhiteSpace(selectedImagePath) || !File.Exists(selectedImagePath))
            {
                StatusMessage = "Capture or load a photo first.";
                return;
            }

            IsBusy = true;
            try
            {
                StatusMessage = "Detecting card edges and measuring centering...";
                outputDirectory = Path.Combine(FileSystem.AppDataDirectory, "Centering", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
                Directory.CreateDirectory(outputDirectory);

                using ImageSharpImage source = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(selectedImagePath);
                source.Mutate(x => x.AutoOrient());
                CardGeometryResult geometry = geometryService.DetectCard(source);
                if (!geometry.Success || geometry.Corners.Length != 4)
                {
                    throw new InvalidOperationException("CollectIQ could not detect the physical outside edges of the card. Use a full front photo with the entire card visible on a solid background.");
                }

                using ImageSharpImage canonical = WarpToCanonical(source, geometry.Corners);
                canonicalImagePath = Path.Combine(outputDirectory, "canonical.png");
                await using (FileStream canonicalStream = new(canonicalImagePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await canonical.SaveAsync(canonicalStream, new PngEncoder());
                }

                float[] gray = ExtractLuminance(canonical);
                baseMeasurement = EstimateCentering(gray);
                if (!baseMeasurement.Success)
                {
                    throw new InvalidOperationException("CollectIQ normalized the card but could not reliably find the inner frame or border for centering. Try a straighter photo or a card with clearer inner borders.");
                }

                ResetAdjustments();
                currentMeasurement = baseMeasurement;
                await RebuildOverlayAsync();
                HasAnalysis = true;
                IsManualMode = false;
                StatusMessage = $"Analysis complete. Inner frame detected with {currentMeasurement.Confidence:0}% confidence.";
                UpdateDisplayedMeasurements();
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
                HasAnalysis = false;
                ConfidencePercent = 0;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ExecuteToggleOverlay()
        {
            if (!HasAnalysis) return;
            if (showingOverlay && !string.IsNullOrWhiteSpace(rawDisplayPath) && File.Exists(rawDisplayPath))
            {
                CardImageSource = ImageSource.FromFile(rawDisplayPath);
                StatusMessage = "Showing the original card photo.";
                showingOverlay = false;
            }
            else if (!string.IsNullOrWhiteSpace(overlayDisplayPath) && File.Exists(overlayDisplayPath))
            {
                CardImageSource = ImageSource.FromFile(overlayDisplayPath);
                StatusMessage = "Showing the centering overlay and detected measurement guides.";
                showingOverlay = true;
            }
        }

        private void ExecuteToggleManualMode()
        {
            if (!HasAnalysis) return;
            IsManualMode = !IsManualMode;
            StatusMessage = IsManualMode
                ? "Manual mode enabled. Move the four line sliders to adjust the detected inner frame."
                : "Manual mode disabled. Auto-detected measurement lines restored.";

            if (!IsManualMode)
            {
                ResetAdjustments();
                currentMeasurement = baseMeasurement;
                _ = RebuildOverlayAsync();
                UpdateDisplayedMeasurements();
            }
        }

        private void ResetAdjustments()
        {
            leftAdjust = 0; OnPropertyChanged(nameof(LeftAdjust));
            rightAdjust = 0; OnPropertyChanged(nameof(RightAdjust));
            topAdjust = 0; OnPropertyChanged(nameof(TopAdjust));
            bottomAdjust = 0; OnPropertyChanged(nameof(BottomAdjust));
        }

        private void ApplyManualAdjustments()
        {
            currentMeasurement = new CenteringMeasurement
            {
                Success = true,
                LeftInset = ClampInset(baseMeasurement.LeftInset + (int)Math.Round(LeftAdjust), CanonicalWidth),
                RightInset = ClampInset(baseMeasurement.RightInset + (int)Math.Round(RightAdjust), CanonicalWidth),
                TopInset = ClampInset(baseMeasurement.TopInset + (int)Math.Round(TopAdjust), CanonicalHeight),
                BottomInset = ClampInset(baseMeasurement.BottomInset + (int)Math.Round(BottomAdjust), CanonicalHeight),
                Confidence = Math.Max(20.0f, baseMeasurement.Confidence * 0.92f)
            };

            float hTotal = currentMeasurement.LeftInset + currentMeasurement.RightInset;
            float vTotal = currentMeasurement.TopInset + currentMeasurement.BottomInset;
            currentMeasurement.LeftPercent = (currentMeasurement.LeftInset / hTotal) * 100.0f;
            currentMeasurement.RightPercent = 100.0f - currentMeasurement.LeftPercent;
            currentMeasurement.TopPercent = (currentMeasurement.TopInset / vTotal) * 100.0f;
            currentMeasurement.BottomPercent = 100.0f - currentMeasurement.TopPercent;
            _ = RebuildOverlayAsync();
            UpdateDisplayedMeasurements();
        }

        private static int ClampInset(int value, int size)
        {
            return Math.Clamp(value, 4, (int)Math.Round(size * 0.35));
        }

        private async Task RebuildOverlayAsync()
        {
            if (string.IsNullOrWhiteSpace(canonicalImagePath) || !File.Exists(canonicalImagePath) || string.IsNullOrWhiteSpace(outputDirectory))
                return;

            using ImageSharpImage canonical = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(canonicalImagePath);
            overlayDisplayPath = Path.Combine(outputDirectory, "centering_overlay.png");
            await SaveCenteringOverlayAsync(canonical, currentMeasurement, overlayDisplayPath);
            if (showingOverlay || CardImageSource == null)
            {
                CardImageSource = ImageSource.FromFile(overlayDisplayPath);
                showingOverlay = true;
            }
        }

        private void UpdateDisplayedMeasurements()
        {
            HorizontalCenteringText = $"{currentMeasurement.LeftPercent:0}/{currentMeasurement.RightPercent:0}";
            VerticalCenteringText = $"{currentMeasurement.TopPercent:0}/{currentMeasurement.BottomPercent:0}";
            ConfidencePercent = currentMeasurement.Confidence;
            CenteringSummary = $"Estimated centering • Horizontal {HorizontalCenteringText} • Vertical {VerticalCenteringText}";
            UpdateRecommendation();
        }

        private void UpdateRecommendation()
        {
            double horizontalDelta = Math.Abs(currentMeasurement.LeftPercent - 50.0);
            double verticalDelta = Math.Abs(currentMeasurement.TopPercent - 50.0);
            double worst = Math.Max(horizontalDelta, verticalDelta);
            if (worst <= Tolerance)
            {
                Recommendation = $"Looks well centered within ±{Tolerance:0.0}% tolerance. Confidence {ConfidencePercent:0}%";
            }
            else if (horizontalDelta > verticalDelta)
            {
                Recommendation = currentMeasurement.LeftPercent > 50.0
                    ? $"The design appears heavier on the left side. Confidence {ConfidencePercent:0}%"
                    : $"The design appears heavier on the right side. Confidence {ConfidencePercent:0}%";
            }
            else
            {
                Recommendation = currentMeasurement.TopPercent > 50.0
                    ? $"The design appears heavier toward the top. Confidence {ConfidencePercent:0}%"
                    : $"The design appears heavier toward the bottom. Confidence {ConfidencePercent:0}%";
            }
        }

        private static CenteringMeasurement EstimateCentering(float[] gray)
        {
            int leftInset = SearchBorderInset(gray, verticalEdge: true, fromStart: true);
            int rightInset = SearchBorderInset(gray, verticalEdge: true, fromStart: false);
            int topInset = SearchBorderInset(gray, verticalEdge: false, fromStart: true);
            int bottomInset = SearchBorderInset(gray, verticalEdge: false, fromStart: false);
            if (leftInset <= 0 || rightInset <= 0 || topInset <= 0 || bottomInset <= 0)
                return new CenteringMeasurement();

            float hTotal = leftInset + rightInset;
            float vTotal = topInset + bottomInset;
            if (hTotal < 10 || vTotal < 10) return new CenteringMeasurement();

            float leftPercent = (leftInset / hTotal) * 100.0f;
            float rightPercent = 100.0f - leftPercent;
            float topPercent = (topInset / vTotal) * 100.0f;
            float bottomPercent = 100.0f - topPercent;
            float horizontalError = MathF.Abs(leftPercent - 50.0f);
            float verticalError = MathF.Abs(topPercent - 50.0f);
            float confidence = Math.Clamp(100.0f - ((horizontalError + verticalError) * 1.25f), 30.0f, 100.0f);
            if (leftInset > CanonicalWidth * 0.30f || rightInset > CanonicalWidth * 0.30f || topInset > CanonicalHeight * 0.22f || bottomInset > CanonicalHeight * 0.22f)
                confidence *= 0.55f;

            return new CenteringMeasurement
            {
                Success = confidence >= 30.0f,
                LeftInset = leftInset,
                RightInset = rightInset,
                TopInset = topInset,
                BottomInset = bottomInset,
                LeftPercent = leftPercent,
                RightPercent = rightPercent,
                TopPercent = topPercent,
                BottomPercent = bottomPercent,
                Confidence = confidence
            };
        }

        private static int SearchBorderInset(float[] gray, bool verticalEdge, bool fromStart)
        {
            int primaryLength = verticalEdge ? CanonicalWidth : CanonicalHeight;
            int secondaryLength = verticalEdge ? CanonicalHeight : CanonicalWidth;
            int start = Math.Max(8, (int)Math.Round(primaryLength * 0.02f));
            int end = Math.Min((int)Math.Round(primaryLength * 0.26f), primaryLength / 3);
            int secondaryStart = (int)Math.Round(secondaryLength * 0.10f);
            int secondaryEnd = (int)Math.Round(secondaryLength * 0.90f);

            float bestScore = 0;
            int bestInset = 0;
            for (int inset = start; inset <= end; inset++)
            {
                float sum = 0;
                int samples = 0;
                for (int s = secondaryStart; s < secondaryEnd; s += 2)
                {
                    int xA, yA, xB, yB;
                    if (verticalEdge)
                    {
                        xA = fromStart ? inset : CanonicalWidth - inset - 1;
                        xB = fromStart ? xA - 1 : xA + 1;
                        yA = yB = s;
                    }
                    else
                    {
                        yA = fromStart ? inset : CanonicalHeight - inset - 1;
                        yB = fromStart ? yA - 1 : yA + 1;
                        xA = xB = s;
                    }
                    if (xA <= 0 || yA <= 0 || xB <= 0 || yB <= 0 || xA >= CanonicalWidth - 1 || xB >= CanonicalWidth - 1 || yA >= CanonicalHeight - 1 || yB >= CanonicalHeight - 1)
                        continue;
                    float diff = MathF.Abs(gray[(yA * CanonicalWidth) + xA] - gray[(yB * CanonicalWidth) + xB]);
                    sum += diff;
                    samples++;
                }
                if (samples == 0) continue;
                float score = sum / samples;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestInset = inset;
                }
            }
            return bestInset;
        }

        private static ImageSharpImage WarpToCanonical(ImageSharpImage source, IReadOnlyList<CardPoint> sourceCorners)
        {
            CardPoint[] destination =
            {
                new(0, 0), new(CanonicalWidth - 1, 0), new(CanonicalWidth - 1, CanonicalHeight - 1), new(0, CanonicalHeight - 1)
            };
            double[] matrix = SolveHomography(destination, sourceCorners);
            Rgba32[] sourcePixels = CopyPixels(source);
            ImageSharpImage output = new(CanonicalWidth, CanonicalHeight);
            output.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < CanonicalHeight; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < CanonicalWidth; x++)
                    {
                        MapProjective(matrix, x, y, out float sx, out float sy);
                        row[x] = SampleBilinear(sourcePixels, source.Width, source.Height, sx, sy);
                    }
                }
            });
            return output;
        }

        private static float[] ExtractLuminance(ImageSharpImage image)
        {
            float[] gray = new float[image.Width * image.Height];
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < image.Height; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    int rowOffset = y * image.Width;
                    for (int x = 0; x < image.Width; x++)
                    {
                        Rgba32 pixel = row[x];
                        gray[rowOffset + x] = ((0.2126f * pixel.R) + (0.7152f * pixel.G) + (0.0722f * pixel.B)) / 255.0f;
                    }
                }
            });
            return gray;
        }

        private static async Task SaveCenteringOverlayAsync(ImageSharpImage canonical, CenteringMeasurement m, string path)
        {
            using ImageSharpImage overlay = canonical.Clone();
            DrawRectangle(overlay, 4, 4, CanonicalWidth - 5, CanonicalHeight - 5, new Rgba32(57, 255, 20), 2);
            int innerLeft = m.LeftInset;
            int innerTop = m.TopInset;
            int innerRight = CanonicalWidth - m.RightInset - 1;
            int innerBottom = CanonicalHeight - m.BottomInset - 1;
            DrawRectangle(overlay, innerLeft, innerTop, innerRight, innerBottom, new Rgba32(255, 221, 0), 3);
            DrawLine(overlay, CanonicalWidth / 2, 0, CanonicalWidth / 2, CanonicalHeight - 1, new Rgba32(0, 225, 255, 180));
            DrawLine(overlay, 0, CanonicalHeight / 2, CanonicalWidth - 1, CanonicalHeight / 2, new Rgba32(0, 225, 255, 180));
            await using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
            await overlay.SaveAsync(stream, new PngEncoder());
        }

        private static void DrawRectangle(ImageSharpImage image, int x1, int y1, int x2, int y2, Rgba32 color, int thickness)
        {
            for (int t = 0; t < thickness; t++)
            {
                DrawLine(image, x1 + t, y1 + t, x2 - t, y1 + t, color);
                DrawLine(image, x2 - t, y1 + t, x2 - t, y2 - t, color);
                DrawLine(image, x2 - t, y2 - t, x1 + t, y2 - t, color);
                DrawLine(image, x1 + t, y2 - t, x1 + t, y1 + t, color);
            }
        }

        private static void DrawLine(ImageSharpImage image, int x1, int y1, int x2, int y2, Rgba32 color)
        {
            int dx = Math.Abs(x2 - x1), sx = x1 < x2 ? 1 : -1;
            int dy = -Math.Abs(y2 - y1), sy = y1 < y2 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                if (x1 >= 0 && x1 < image.Width && y1 >= 0 && y1 < image.Height) image[x1, y1] = color;
                if (x1 == x2 && y1 == y2) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x1 += sx; }
                if (e2 <= dx) { err += dx; y1 += sy; }
            }
        }

        private static Rgba32[] CopyPixels(ImageSharpImage image)
        {
            Rgba32[] pixels = new Rgba32[image.Width * image.Height];
            image.CopyPixelDataTo(pixels);
            return pixels;
        }

        private static Rgba32 SampleBilinear(Rgba32[] pixels, int width, int height, float x, float y)
        {
            if (float.IsNaN(x) || float.IsNaN(y) || x < 0 || y < 0 || x > width - 1 || y > height - 1)
                return new Rgba32(0, 0, 0, 255);
            int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
            int x1 = Math.Min(x0 + 1, width - 1), y1 = Math.Min(y0 + 1, height - 1);
            float tx = x - x0, ty = y - y0;
            Rgba32 c00 = pixels[(y0 * width) + x0], c10 = pixels[(y0 * width) + x1], c01 = pixels[(y1 * width) + x0], c11 = pixels[(y1 * width) + x1];
            byte r = Blend(Blend(c00.R, c10.R, tx), Blend(c01.R, c11.R, tx), ty);
            byte g = Blend(Blend(c00.G, c10.G, tx), Blend(c01.G, c11.G, tx), ty);
            byte b = Blend(Blend(c00.B, c10.B, tx), Blend(c01.B, c11.B, tx), ty);
            byte a = Blend(Blend(c00.A, c10.A, tx), Blend(c01.A, c11.A, tx), ty);
            return new Rgba32(r, g, b, a);
        }

        private static byte Blend(byte a, byte b, float t) => (byte)Math.Clamp(MathF.Round(a + ((b - a) * t)), 0, 255);

        private static void MapProjective(double[] matrix, float x, float y, out float sourceX, out float sourceY)
        {
            double denominator = (matrix[6] * x) + (matrix[7] * y) + matrix[8];
            if (Math.Abs(denominator) < 1e-8) { sourceX = -1; sourceY = -1; return; }
            sourceX = (float)(((matrix[0] * x) + (matrix[1] * y) + matrix[2]) / denominator);
            sourceY = (float)(((matrix[3] * x) + (matrix[4] * y) + matrix[5]) / denominator);
        }

        private static double[] SolveHomography(IReadOnlyList<CardPoint> destination, IReadOnlyList<CardPoint> source)
        {
            double[,] a = new double[8, 8];
            double[] b = new double[8];
            for (int i = 0; i < 4; i++)
            {
                double x = destination[i].X, y = destination[i].Y, u = source[i].X, v = source[i].Y;
                int row = i * 2;
                a[row, 0] = x; a[row, 1] = y; a[row, 2] = 1.0; a[row, 6] = -u * x; a[row, 7] = -u * y; b[row] = u;
                a[row + 1, 3] = x; a[row + 1, 4] = y; a[row + 1, 5] = 1.0; a[row + 1, 6] = -v * x; a[row + 1, 7] = -v * y; b[row + 1] = v;
            }
            double[] h = SolveLinearSystem(a, b);
            return new[] { h[0], h[1], h[2], h[3], h[4], h[5], h[6], h[7], 1.0 };
        }

        private static double[] SolveLinearSystem(double[,] a, double[] b)
        {
            int n = b.Length;
            double[,] augmented = new double[n, n + 1];
            for (int row = 0; row < n; row++)
            {
                for (int col = 0; col < n; col++) augmented[row, col] = a[row, col];
                augmented[row, n] = b[row];
            }
            for (int pivot = 0; pivot < n; pivot++)
            {
                int bestRow = pivot;
                double bestValue = Math.Abs(augmented[pivot, pivot]);
                for (int row = pivot + 1; row < n; row++)
                {
                    double value = Math.Abs(augmented[row, pivot]);
                    if (value > bestValue) { bestValue = value; bestRow = row; }
                }
                if (bestRow != pivot)
                {
                    for (int col = pivot; col <= n; col++)
                    {
                        double temp = augmented[pivot, col];
                        augmented[pivot, col] = augmented[bestRow, col];
                        augmented[bestRow, col] = temp;
                    }
                }
                double pivotValue = augmented[pivot, pivot];
                if (Math.Abs(pivotValue) < 1e-12) throw new InvalidOperationException("Centering homography solve failed because the detected corners were degenerate.");
                for (int col = pivot; col <= n; col++) augmented[pivot, col] /= pivotValue;
                for (int row = 0; row < n; row++)
                {
                    if (row == pivot) continue;
                    double factor = augmented[row, pivot];
                    if (Math.Abs(factor) < 1e-12) continue;
                    for (int col = pivot; col <= n; col++) augmented[row, col] -= factor * augmented[pivot, col];
                }
            }
            double[] result = new double[n];
            for (int i = 0; i < n; i++) result[i] = augmented[i, n];
            return result;
        }

        private bool SetProperty<T>(ref T backingField, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(backingField, value)) return false;
            backingField = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private sealed class CenteringMeasurement
        {
            public bool Success { get; init; }
            public int LeftInset { get; set; }
            public int RightInset { get; set; }
            public int TopInset { get; set; }
            public int BottomInset { get; set; }
            public float LeftPercent { get; set; }
            public float RightPercent { get; set; }
            public float TopPercent { get; set; }
            public float BottomPercent { get; set; }
            public float Confidence { get; set; }
        }
    }
}
