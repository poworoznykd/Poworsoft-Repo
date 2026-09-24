using CollectIQ.Interfaces;
using CollectIQ.Models.Inspection.Geometry;
using CollectIQ.Services.Inspection;
using CollectIQ.Services.Inspection.Geometry;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System.Collections.Generic;
using System.Linq;
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

        // The original capture stays untouched on disk. Result/overlay rendering
        // does not need a multi-megapixel bitmap, so use a bounded full-frame
        // preview to avoid long PNG encodes and Android memory churn.
        private const int DisplayMaximumDimension = 1800;

        private readonly ICardGeometryService geometryService;
        private readonly ICardNormalizationService normalizationService;

        private string? selectedImagePath;
        private string? rawDisplayPath;
        private string? canonicalImagePath;
        private string? orientedFullImagePath;
        private CardPoint[]? detectedOuterCorners;
        private string? overlayDisplayPath;
        private string? outputDirectory;
        private ImageSource? cardImageSource;
        private ImageSource? originalImageSource;
        private ImageSource? cardLockImageSource;
        private ImageSource? trueFormImageSource;
        private ImageSource? measurementImageSource;
        private bool hasOriginalImage;
        private ImageSource? edgeImageSource;
        private ImageSource? rectangleImageSource;
        private bool hasRectangleImage;
        private ImageSource? manualPreviewImageSource;
        private bool hasEdgeImage;
        private bool hasCardLockImage;
        private bool hasTrueFormImage;
        private bool needsManualOuterCard;
        private double manualOuterLeft = 0.07;
        private double manualOuterRight = 0.93;
        private double manualOuterTop = 0.05;
        private double manualOuterBottom = 0.95;
        private string analysisStageText = "Waiting for a card photo.";
        private bool hasMeasurementImage;
        private string centeringSummary = "Capture or load a clear front card photo. Analysis starts automatically.";
        private string horizontalCenteringText = "Not analyzed";
        private string verticalCenteringText = "Not analyzed";
        private string recommendation = "Use a full front photo on a solid background so the card edges are easy to detect.";
        private string statusMessage = "No image selected.";
        private double zoomLevel = 1.0; // Display is always 1:1 scale inside AspectFit; no UI zoom/crop.
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
        private double outerLeftAdjust;
        private double outerRightAdjust;
        private double outerTopAdjust;
        private double outerBottomAdjust;
        private long overlayRenderVersion;

        private CenteringMeasurement baseMeasurement = new();
        private CenteringMeasurement currentMeasurement = new();

        public InspectCenteringViewModel() : this(new CardGeometryService()) { }

        public InspectCenteringViewModel(ICardGeometryService geometryService)
            : this(geometryService, new CardNormalizationService(geometryService))
        {
        }

        public InspectCenteringViewModel(
            ICardGeometryService geometryService,
            ICardNormalizationService normalizationService)
        {
            this.geometryService = geometryService;
            this.normalizationService = normalizationService;
            CapturePhotoCommand = new Command(async () => await ExecuteCapturePhotoAsync(), () => !IsBusy);
            PickImageCommand = new Command(async () => await ExecutePickImageAsync(), () => !IsBusy);
            AnalyzeCommand = new Command(async () => await ExecuteAnalyzeAsync(), () => !IsBusy);
            ToggleOverlayCommand = new Command(ExecuteToggleOverlay, () => !IsBusy && HasAnalysis);
            ToggleManualModeCommand = new Command(ExecuteToggleManualMode, () => !IsBusy && HasAnalysis);
            AdjustManualLineCommand = new Command<string>(async parameter => await ExecuteAdjustManualLineAsync(parameter), parameter => !IsBusy && HasAnalysis && IsManualMode);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ImageSource? CardImageSource { get => cardImageSource; set => SetProperty(ref cardImageSource, value); }

        /// <summary>
        /// The untouched camera/picker image shown to the user for comparison.
        /// </summary>
        public ImageSource? OriginalImageSource
        {
            get => originalImageSource;
            private set => SetProperty(ref originalImageSource, value);
        }

        /// <summary>
        /// TrueForm View: CollectIQ's perspective-corrected, standardized 5:7 card.
        /// </summary>
        /// <summary>
        /// Card Lock View: the source image with the detected physical-card
        /// boundary and corners overlaid.
        /// </summary>
        public ImageSource? EdgeImageSource
        {
            get => edgeImageSource;
            private set => SetProperty(ref edgeImageSource, value);
        }

        public bool HasEdgeImage
        {
            get => hasEdgeImage;
            private set => SetProperty(ref hasEdgeImage, value);
        }

        public ImageSource? RectangleImageSource
        {
            get => rectangleImageSource;
            private set => SetProperty(ref rectangleImageSource, value);
        }

        public bool HasRectangleImage
        {
            get => hasRectangleImage;
            private set => SetProperty(ref hasRectangleImage, value);
        }

        public ImageSource? ManualPreviewImageSource
        {
            get => manualPreviewImageSource;
            private set => SetProperty(ref manualPreviewImageSource, value);
        }

        public ImageSource? CardLockImageSource
        {
            get => cardLockImageSource;
            private set => SetProperty(ref cardLockImageSource, value);
        }

        public ImageSource? TrueFormImageSource
        {
            get => trueFormImageSource;
            private set => SetProperty(ref trueFormImageSource, value);
        }

        public bool HasOriginalImage
        {
            get => hasOriginalImage;
            private set => SetProperty(ref hasOriginalImage, value);
        }

        public bool HasCardLockImage
        {
            get => hasCardLockImage;
            private set => SetProperty(ref hasCardLockImage, value);
        }

        public bool HasTrueFormImage
        {
            get => hasTrueFormImage;
            private set => SetProperty(ref hasTrueFormImage, value);
        }

        public bool NeedsManualOuterCard
        {
            get => needsManualOuterCard;
            private set => SetProperty(ref needsManualOuterCard, value);
        }

        public double ManualOuterLeft => manualOuterLeft;
        public double ManualOuterRight => manualOuterRight;
        public double ManualOuterTop => manualOuterTop;
        public double ManualOuterBottom => manualOuterBottom;

        public void SetManualOuterGuide(string guideName, double normalizedPosition)
        {
            normalizedPosition = Math.Clamp(normalizedPosition, 0.0, 1.0);
            const double minimumGap = 0.04;

            switch (guideName)
            {
                case "Left":
                    manualOuterLeft = Math.Clamp(
                        normalizedPosition,
                        0.0,
                        manualOuterRight - minimumGap);
                    OnPropertyChanged(nameof(ManualOuterLeft));
                    break;

                case "Right":
                    manualOuterRight = Math.Clamp(
                        normalizedPosition,
                        manualOuterLeft + minimumGap,
                        1.0);
                    OnPropertyChanged(nameof(ManualOuterRight));
                    break;

                case "Top":
                    manualOuterTop = Math.Clamp(
                        normalizedPosition,
                        0.0,
                        manualOuterBottom - minimumGap);
                    OnPropertyChanged(nameof(ManualOuterTop));
                    break;

                case "Bottom":
                    manualOuterBottom = Math.Clamp(
                        normalizedPosition,
                        manualOuterTop + minimumGap,
                        1.0);
                    OnPropertyChanged(nameof(ManualOuterBottom));
                    break;
            }
        }

        public double GetManualOuterGuide(string guideName) => guideName switch
        {
            "Left" => manualOuterLeft,
            "Right" => manualOuterRight,
            "Top" => manualOuterTop,
            "Bottom" => manualOuterBottom,
            _ => 0.0
        };

        public string AnalysisStageText
        {
            get => analysisStageText;
            private set => SetProperty(ref analysisStageText, value);
        }

        public ImageSource? MeasurementImageSource
        {
            get => measurementImageSource;
            private set => SetProperty(ref measurementImageSource, value);
        }

        public bool HasMeasurementImage
        {
            get => hasMeasurementImage;
            private set => SetProperty(ref hasMeasurementImage, value);
        }
        public string CenteringSummary { get => centeringSummary; set => SetProperty(ref centeringSummary, value); }
        public string HorizontalCenteringText { get => horizontalCenteringText; set => SetProperty(ref horizontalCenteringText, value); }
        public string VerticalCenteringText { get => verticalCenteringText; set => SetProperty(ref verticalCenteringText, value); }
        public string Recommendation { get => recommendation; set => SetProperty(ref recommendation, value); }
        public string StatusMessage { get => statusMessage; set => SetProperty(ref statusMessage, value); }
        public double ZoomLevel { get => zoomLevel; set => SetProperty(ref zoomLevel, value); }
        public double Tolerance { get => tolerance; set { if (SetProperty(ref tolerance, value) && HasAnalysis) UpdateRecommendation(); } }
        public bool HasAnalysis { get => hasAnalysis; set { if (SetProperty(ref hasAnalysis, value)) RaiseCanExecutes(); } }
        public bool IsBusy { get => isBusy; set { if (SetProperty(ref isBusy, value)) RaiseCanExecutes(); } }
        public bool IsManualMode
        {
            get => isManualMode;
            set
            {
                if (SetProperty(ref isManualMode, value))
                {
                    RaiseCanExecutes();
                    OnPropertyChanged(nameof(ManualGuideStateText));
                }
            }
        }

        public string ManualGuideStateText =>
            IsManualMode ? "LOCK MANUAL GUIDES" : "ADJUST CENTERING";
        public double ConfidencePercent { get => confidencePercent; set => SetProperty(ref confidencePercent, value); }
        public double LeftAdjust { get => leftAdjust; set { if (SetProperty(ref leftAdjust, value) && HasAnalysis && IsManualMode) ApplyManualAdjustments(); } }
        public double RightAdjust { get => rightAdjust; set { if (SetProperty(ref rightAdjust, value) && HasAnalysis && IsManualMode) ApplyManualAdjustments(); } }
        public double TopAdjust { get => topAdjust; set { if (SetProperty(ref topAdjust, value) && HasAnalysis && IsManualMode) ApplyManualAdjustments(); } }
        public double BottomAdjust { get => bottomAdjust; set { if (SetProperty(ref bottomAdjust, value) && HasAnalysis && IsManualMode) ApplyManualAdjustments(); } }
        public double OuterLeftAdjust { get => outerLeftAdjust; set { if (SetProperty(ref outerLeftAdjust, value) && HasAnalysis && IsManualMode) ApplyManualAdjustments(); } }
        public double OuterRightAdjust { get => outerRightAdjust; set { if (SetProperty(ref outerRightAdjust, value) && HasAnalysis && IsManualMode) ApplyManualAdjustments(); } }
        public double OuterTopAdjust { get => outerTopAdjust; set { if (SetProperty(ref outerTopAdjust, value) && HasAnalysis && IsManualMode) ApplyManualAdjustments(); } }
        public double OuterBottomAdjust { get => outerBottomAdjust; set { if (SetProperty(ref outerBottomAdjust, value) && HasAnalysis && IsManualMode) ApplyManualAdjustments(); } }

        public int CanonicalWidthPixels => CanonicalWidth;
        public int CanonicalHeightPixels => CanonicalHeight;

        public int OuterLeftGuidePixel => currentMeasurement.OuterLeft;
        public int OuterRightGuidePixel => currentMeasurement.OuterRight;
        public int OuterTopGuidePixel => currentMeasurement.OuterTop;
        public int OuterBottomGuidePixel => currentMeasurement.OuterBottom;
        public int InnerLeftGuidePixel => currentMeasurement.InnerLeft;
        public int InnerRightGuidePixel => currentMeasurement.InnerRight;
        public int InnerTopGuidePixel => currentMeasurement.InnerTop;
        public int InnerBottomGuidePixel => currentMeasurement.InnerBottom;

        public ICommand CapturePhotoCommand { get; }
        public ICommand PickImageCommand { get; }
        public ICommand AnalyzeCommand { get; }
        public ICommand ToggleOverlayCommand { get; }
        public ICommand ToggleManualModeCommand { get; }
        public ICommand AdjustManualLineCommand { get; }

        private void RaiseCanExecutes()
        {
            (CapturePhotoCommand as Command)?.ChangeCanExecute();
            (PickImageCommand as Command)?.ChangeCanExecute();
            (AnalyzeCommand as Command)?.ChangeCanExecute();
            (ToggleOverlayCommand as Command)?.ChangeCanExecute();
            (ToggleManualModeCommand as Command)?.ChangeCanExecute();
            (AdjustManualLineCommand as Command<string>)?.ChangeCanExecute();
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
                StatusMessage = "Photo captured. Automatic card detection is starting…";
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
                StatusMessage = "Image loaded. Automatic card detection is starting…";
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

            LoadLocalImage(localPath);
        }

        /// <summary>
        /// Loads a photo captured by CollectIQ's embedded centering CameraView.
        /// This keeps camera ownership in the page while the centering model
        /// remains responsible for analysis state.
        /// </summary>
        public void LoadCapturedImage(string localPath)
        {
            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
                throw new FileNotFoundException("The centering capture could not be found.", localPath);

            LoadLocalImage(localPath);
            StatusMessage = "Photo captured. Automatic card detection is starting…";
        }

        private void LoadLocalImage(string localPath)
        {
            selectedImagePath = localPath;
            rawDisplayPath = localPath;
            canonicalImagePath = null;
            orientedFullImagePath = null;
            detectedOuterCorners = null;
            overlayDisplayPath = null;
            MeasurementImageSource = null;
            HasMeasurementImage = false;
            CardImageSource = ImageSource.FromFile(localPath);
            OriginalImageSource = ImageSource.FromFile(localPath);
            ManualPreviewImageSource = OriginalImageSource;
            EdgeImageSource = null;
            HasEdgeImage = false;
            RectangleImageSource = null;
            HasRectangleImage = false;
            HasOriginalImage = true;
            CardLockImageSource = null;
            HasCardLockImage = false;
            TrueFormImageSource = null;
            HasTrueFormImage = false;
            NeedsManualOuterCard = false;
            manualOuterLeft = 0.07;
            manualOuterRight = 0.93;
            manualOuterTop = 0.05;
            manualOuterBottom = 0.95;
            OnPropertyChanged(nameof(ManualOuterLeft));
            OnPropertyChanged(nameof(ManualOuterRight));
            OnPropertyChanged(nameof(ManualOuterTop));
            OnPropertyChanged(nameof(ManualOuterBottom));
            AnalysisStageText = "Photo ready. Automatic analysis is starting…";
            HasAnalysis = false;
            IsManualMode = false;
            ConfidencePercent = 0;
            CenteringSummary = "Ready for automatic analysis.";
            HorizontalCenteringText = "Not analyzed";
            VerticalCenteringText = "Not analyzed";
            Recommendation = "Automatic analysis begins immediately after capture or image selection.";
            ResetAdjustments();
        }

        /// <summary>
        /// Runs centering analysis for an image that was captured by the embedded camera.
        /// </summary>
        public Task AnalyzeLoadedImageAsync() => ExecuteAnalyzeAsync();

        private async Task ExecuteAnalyzeAsync()
        {
            if (IsBusy)
                return;

            if (string.IsNullOrWhiteSpace(selectedImagePath) || !File.Exists(selectedImagePath))
            {
                StatusMessage = "Capture or load a photo first.";
                return;
            }

            IsBusy = true;
            HasAnalysis = false;
            ConfidencePercent = 0;

            string inputPath = selectedImagePath;
            outputDirectory = Path.Combine(
                FileSystem.AppDataDirectory,
                "Centering",
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));

            Directory.CreateDirectory(outputDirectory);

            await InspectionDiagnosticLogger.StartRunAsync("Centering", $"Input={inputPath}");

            try
            {
                StatusMessage = "Automatic centering started.";
                AnalysisStageText = "Loading captured image…";

                Progress<CardNormalizationProgress> normalizationProgress = new(progress =>
                {
                    AnalysisStageText = progress.Message;
                    StatusMessage = progress.Message;

                    if (!string.IsNullOrWhiteSpace(progress.PreviewImagePath) &&
                        File.Exists(progress.PreviewImagePath))
                        ManualPreviewImageSource = ImageSource.FromFile(progress.PreviewImagePath);

                    if (!string.IsNullOrWhiteSpace(progress.EdgeImagePath) &&
                        File.Exists(progress.EdgeImagePath))
                    {
                        EdgeImageSource = ImageSource.FromFile(progress.EdgeImagePath);
                        HasEdgeImage = true;
                    }

                    if (!string.IsNullOrWhiteSpace(progress.RectangleImagePath) &&
                        File.Exists(progress.RectangleImagePath))
                    {
                        RectangleImageSource = ImageSource.FromFile(progress.RectangleImagePath);
                        HasRectangleImage = true;
                    }

                    if (!string.IsNullOrWhiteSpace(progress.CardLockImagePath) &&
                        File.Exists(progress.CardLockImagePath))
                    {
                        CardLockImageSource = ImageSource.FromFile(progress.CardLockImagePath);
                        HasCardLockImage = true;
                    }

                    if (!string.IsNullOrWhiteSpace(progress.TrueFormImagePath) &&
                        File.Exists(progress.TrueFormImagePath))
                    {
                        TrueFormImageSource = ImageSource.FromFile(progress.TrueFormImagePath);
                        HasTrueFormImage = true;
                    }
                });

                CardNormalizationResult normalized = await InspectionExecution.RunAsync(
                    "Centering",
                    "Common card normalization",
                    cancellationToken => normalizationService.NormalizeAsync(
                        inputPath,
                        outputDirectory,
                        CanonicalWidth,
                        CanonicalHeight,
                        normalizationProgress,
                        cancellationToken),
                    TimeSpan.FromSeconds(120));

                rawDisplayPath = normalized.SourcePreviewPath;
                orientedFullImagePath = normalized.SourcePreviewPath;
                canonicalImagePath = normalized.NormalizedImagePath;
                detectedOuterCorners = normalized.SourceCorners;

                // Preserve the original capture for comparison while exposing the
                // normalized image separately as TrueForm View.
                CardImageSource = ImageSource.FromFile(selectedImagePath);
                OriginalImageSource = ImageSource.FromFile(selectedImagePath);
                CardLockImageSource = ImageSource.FromFile(normalized.DetectionOverlayPath);
                HasCardLockImage = true;
                TrueFormImageSource = ImageSource.FromFile(canonicalImagePath);
                HasTrueFormImage = true;
                AnalysisStageText = "TrueForm complete. Measuring the printed/image frame…";

                AnalysisStageText = "Measuring the printed/image frame for automatic centering…";

                using ImageSharpImage canonical = SixLabors.ImageSharp.Image.Load<Rgba32>(canonicalImagePath);
                float[] gray = ExtractLuminance(canonical);
                baseMeasurement = EstimateCentering(gray);

                // Auto detection is an initial suggestion. Centering is intentionally
                // user-verifiable: the user aligns both the physical-edge rectangle
                // and the printed/image rectangle before accepting the percentages.
                if (!baseMeasurement.Success)
                {
                    baseMeasurement = CreateFallbackMeasurement();
                    StatusMessage = "The card was flattened successfully. The printed-frame estimate is weak, so adjust the yellow lines manually.";
                }

                ResetAdjustments();
                currentMeasurement = baseMeasurement.Clone();
                RecalculateFromGuideLines();

                HasAnalysis = true;

                // Automatic centering is the primary result. The guides start
                // locked so the user can see exactly what CollectIQ calculated
                // before choosing whether to correct anything manually.
                IsManualMode = false;
                RaiseCanExecutes();

                MeasurementImageSource = ImageSource.FromFile(canonicalImagePath);
                HasMeasurementImage = true;
                NotifyGuidePositionsChanged();
                UpdateDisplayedMeasurements();

                AnalysisStageText = "Automatic centering complete.";

                StatusMessage =
                    "Automatic centering complete. Review the Original Capture, Card Lock View, TrueForm View, and Centering Map below. " +
                    "If CollectIQ placed a guide incorrectly, tap ADJUST CENTERING and drag the line. Results recalculate live.";

                await InspectionDiagnosticLogger.WriteAsync(
                    "Centering",
                    "AUTOMATIC CENTERING COMPLETE",
                    $"GeometryConfidence={normalized.GeometryConfidence:0.000}; " +
                    $"LR={currentMeasurement.LeftPercent:0.0}/{currentMeasurement.RightPercent:0.0}; " +
                    $"TB={currentMeasurement.TopPercent:0.0}/{currentMeasurement.BottomPercent:0.0}");
            }
            catch (TimeoutException ex)
            {
                await InspectionDiagnosticLogger.WriteAsync("Centering", "ANALYSIS TIMEOUT", exception: ex);
                EnterManualOuterCardMode(
                    $"Automatic processing did not finish within 120 seconds while: {AnalysisStageText}");
            }
            catch (Exception ex)
            {
                await InspectionDiagnosticLogger.WriteAsync("Centering", "ANALYSIS FAILED", exception: ex);
                EnterManualOuterCardMode(
                    $"Automatic card processing could not finish: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                await InspectionDiagnosticLogger.WriteAsync("Centering", "BUSY STATE RELEASED");
            }
        }

        private void EnterManualOuterCardMode(string reason)
        {
            if (ManualPreviewImageSource is null &&
                !string.IsNullOrWhiteSpace(selectedImagePath) && File.Exists(selectedImagePath))
                ManualPreviewImageSource = ImageSource.FromFile(selectedImagePath);
            NeedsManualOuterCard = true;
            HasAnalysis = false;
            IsManualMode = false;
            AnalysisStageText = "MANUAL CARD LOCK — move the four green lines to the physical outside edges.";
            StatusMessage =
                $"{reason} Use MANUAL CARD LOCK below: move LEFT, RIGHT, TOP and BOTTOM green lines onto the real outside edges of the card, then tap CONTINUE WITH THESE CARD EDGES.";
            RaiseCanExecutes();
        }

        /// <summary>
        /// Continues Centering from user-positioned physical-card edges. This
        /// deliberately skips automatic outer-card detection.
        /// </summary>
        public async Task ContinueWithManualOuterEdgesAsync()
        {
            if (IsBusy ||
                string.IsNullOrWhiteSpace(selectedImagePath) ||
                !File.Exists(selectedImagePath))
                return;

            IsBusy = true;
            HasAnalysis = false;

            string manualOutputDirectory = Path.Combine(
                FileSystem.AppDataDirectory,
                "Centering",
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_manual");

            Directory.CreateDirectory(manualOutputDirectory);
            outputDirectory = manualOutputDirectory;

            try
            {
                AnalysisStageText = "Using your manual physical-card edges…";
                StatusMessage = "Creating Card Lock and TrueForm directly from your four green lines…";

                Progress<CardNormalizationProgress> progressReporter = new(progress =>
                {
                    AnalysisStageText = progress.Message;
                    StatusMessage = progress.Message;

                    if (!string.IsNullOrWhiteSpace(progress.CardLockImagePath) &&
                        File.Exists(progress.CardLockImagePath))
                    {
                        CardLockImageSource = ImageSource.FromFile(progress.CardLockImagePath);
                        HasCardLockImage = true;
                    }

                    if (!string.IsNullOrWhiteSpace(progress.TrueFormImagePath) &&
                        File.Exists(progress.TrueFormImagePath))
                    {
                        TrueFormImageSource = ImageSource.FromFile(progress.TrueFormImagePath);
                        HasTrueFormImage = true;
                    }
                });

                CardNormalizationResult normalized = await InspectionExecution.RunAsync(
                    "Centering",
                    "Manual card-edge normalization",
                    cancellationToken => normalizationService.NormalizeFromRelativeRectangleAsync(
                        selectedImagePath,
                        manualOutputDirectory,
                        manualOuterLeft,
                        manualOuterRight,
                        manualOuterTop,
                        manualOuterBottom,
                        CanonicalWidth,
                        CanonicalHeight,
                        progressReporter,
                        cancellationToken),
                    TimeSpan.FromSeconds(120));

                rawDisplayPath = normalized.SourcePreviewPath;
                orientedFullImagePath = normalized.SourcePreviewPath;
                canonicalImagePath = normalized.NormalizedImagePath;
                detectedOuterCorners = normalized.SourceCorners;

                CardImageSource = ImageSource.FromFile(selectedImagePath);
                OriginalImageSource = ImageSource.FromFile(selectedImagePath);
                CardLockImageSource = ImageSource.FromFile(normalized.DetectionOverlayPath);
                HasCardLockImage = true;
                TrueFormImageSource = ImageSource.FromFile(canonicalImagePath);
                HasTrueFormImage = true;

                AnalysisStageText = "Measuring printed/image frame on TrueForm…";

                using ImageSharpImage canonical =
                    SixLabors.ImageSharp.Image.Load<Rgba32>(canonicalImagePath);

                float[] gray = ExtractLuminance(canonical);
                baseMeasurement = EstimateCentering(gray);

                if (!baseMeasurement.Success)
                {
                    baseMeasurement = CreateFallbackMeasurement();
                }

                ResetAdjustments();
                currentMeasurement = baseMeasurement.Clone();
                RecalculateFromGuideLines();

                HasAnalysis = true;
                NeedsManualOuterCard = false;
                IsManualMode = false;
                RaiseCanExecutes();

                MeasurementImageSource = ImageSource.FromFile(canonicalImagePath);
                HasMeasurementImage = true;
                NotifyGuidePositionsChanged();
                UpdateDisplayedMeasurements();

                AnalysisStageText = "Centering complete from your manual card edges.";
                StatusMessage =
                    "Centering complete. Review Card Lock, TrueForm and the Centering Map. Tap ADJUST CENTERING if you want to move any of the final green/yellow measurement lines.";
            }
            catch (TimeoutException ex)
            {
                await InspectionDiagnosticLogger.WriteAsync(
                    "Centering",
                    "MANUAL NORMALIZATION TIMEOUT",
                    exception: ex);

                NeedsManualOuterCard = true;
                StatusMessage =
                    $"Manual card-edge transform did not finish while: {AnalysisStageText}. Your four green lines are still available to adjust and retry.";
            }
            catch (Exception ex)
            {
                await InspectionDiagnosticLogger.WriteAsync(
                    "Centering",
                    "MANUAL NORMALIZATION FAILED",
                    exception: ex);

                NeedsManualOuterCard = true;
                StatusMessage =
                    $"Could not create TrueForm from the manual card edges: {ex.Message}. Adjust the four green lines and retry.";
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
            RaiseCanExecutes();
            StatusMessage = IsManualMode
                ? "Manual alignment enabled. Green = physical card edge; yellow = printed/image edge."
                : "Manual alignment locked. The current guide positions and percentages are preserved.";
        }

        /// <summary>
        /// Sets one guide directly in normalized-card pixel coordinates.
        /// Called by the interactive guide editor while the user drags a line.
        /// </summary>
        public void SetGuidePosition(string guideName, int pixel)
        {
            if (!HasAnalysis || !IsManualMode)
                return;

            switch (guideName)
            {
                case "OuterLeft":
                    currentMeasurement.OuterLeft = Math.Clamp(
                        pixel,
                        0,
                        currentMeasurement.InnerLeft - 2);
                    break;

                case "OuterRight":
                    currentMeasurement.OuterRight = Math.Clamp(
                        pixel,
                        currentMeasurement.InnerRight + 2,
                        CanonicalWidth - 1);
                    break;

                case "OuterTop":
                    currentMeasurement.OuterTop = Math.Clamp(
                        pixel,
                        0,
                        currentMeasurement.InnerTop - 2);
                    break;

                case "OuterBottom":
                    currentMeasurement.OuterBottom = Math.Clamp(
                        pixel,
                        currentMeasurement.InnerBottom + 2,
                        CanonicalHeight - 1);
                    break;

                case "InnerLeft":
                    currentMeasurement.InnerLeft = Math.Clamp(
                        pixel,
                        currentMeasurement.OuterLeft + 2,
                        currentMeasurement.InnerRight - 2);
                    break;

                case "InnerRight":
                    currentMeasurement.InnerRight = Math.Clamp(
                        pixel,
                        currentMeasurement.InnerLeft + 2,
                        currentMeasurement.OuterRight - 2);
                    break;

                case "InnerTop":
                    currentMeasurement.InnerTop = Math.Clamp(
                        pixel,
                        currentMeasurement.OuterTop + 2,
                        currentMeasurement.InnerBottom - 2);
                    break;

                case "InnerBottom":
                    currentMeasurement.InnerBottom = Math.Clamp(
                        pixel,
                        currentMeasurement.InnerTop + 2,
                        currentMeasurement.OuterBottom - 2);
                    break;

                default:
                    return;
            }

            RecalculateFromGuideLines();
            UpdateDisplayedMeasurements();
            NotifyGuidePositionsChanged();
            StatusMessage =
                "Manual guide adjusted. Green = physical card edge; yellow = printed/image edge.";
        }

        public int GetGuidePosition(string guideName)
        {
            return guideName switch
            {
                "OuterLeft" => currentMeasurement.OuterLeft,
                "OuterRight" => currentMeasurement.OuterRight,
                "OuterTop" => currentMeasurement.OuterTop,
                "OuterBottom" => currentMeasurement.OuterBottom,
                "InnerLeft" => currentMeasurement.InnerLeft,
                "InnerRight" => currentMeasurement.InnerRight,
                "InnerTop" => currentMeasurement.InnerTop,
                "InnerBottom" => currentMeasurement.InnerBottom,
                _ => 0
            };
        }

        public void ResetManualGuides()
        {
            if (!HasAnalysis)
                return;

            currentMeasurement = baseMeasurement.Clone();
            ResetAdjustments();
            RecalculateFromGuideLines();
            UpdateDisplayedMeasurements();
            NotifyGuidePositionsChanged();
            IsManualMode = false;
            StatusMessage =
                "Automatic centering restored. Tap ADJUST CENTERING only if you want to correct a guide manually.";
        }

        private void NotifyGuidePositionsChanged()
        {
            OnPropertyChanged(nameof(OuterLeftGuidePixel));
            OnPropertyChanged(nameof(OuterRightGuidePixel));
            OnPropertyChanged(nameof(OuterTopGuidePixel));
            OnPropertyChanged(nameof(OuterBottomGuidePixel));
            OnPropertyChanged(nameof(InnerLeftGuidePixel));
            OnPropertyChanged(nameof(InnerRightGuidePixel));
            OnPropertyChanged(nameof(InnerTopGuidePixel));
            OnPropertyChanged(nameof(InnerBottomGuidePixel));
        }

        private void ResetAdjustments()
        {
            leftAdjust = 0; OnPropertyChanged(nameof(LeftAdjust));
            rightAdjust = 0; OnPropertyChanged(nameof(RightAdjust));
            topAdjust = 0; OnPropertyChanged(nameof(TopAdjust));
            bottomAdjust = 0; OnPropertyChanged(nameof(BottomAdjust));
            outerLeftAdjust = 0; OnPropertyChanged(nameof(OuterLeftAdjust));
            outerRightAdjust = 0; OnPropertyChanged(nameof(OuterRightAdjust));
            outerTopAdjust = 0; OnPropertyChanged(nameof(OuterTopAdjust));
            outerBottomAdjust = 0; OnPropertyChanged(nameof(OuterBottomAdjust));
        }

        private async Task ExecuteAdjustManualLineAsync(string? parameter)
        {
            if (!HasAnalysis || !IsManualMode || string.IsNullOrWhiteSpace(parameter))
                return;

            string[] pieces = parameter.Split(':');
            if (pieces.Length != 2 || !int.TryParse(pieces[1], out int delta) || delta == 0 || Math.Abs(delta) > 5)
                return;

            switch (pieces[0])
            {
                case "OuterLeft": OuterLeftAdjust = Math.Clamp(Math.Round(OuterLeftAdjust) + delta, 0, 80); break;
                case "OuterRight": OuterRightAdjust = Math.Clamp(Math.Round(OuterRightAdjust) + delta, 0, 80); break;
                case "OuterTop": OuterTopAdjust = Math.Clamp(Math.Round(OuterTopAdjust) + delta, 0, 100); break;
                case "OuterBottom": OuterBottomAdjust = Math.Clamp(Math.Round(OuterBottomAdjust) + delta, 0, 100); break;
                case "InnerLeft": LeftAdjust = Math.Clamp(Math.Round(LeftAdjust) + delta, -140, 140); break;
                case "InnerRight": RightAdjust = Math.Clamp(Math.Round(RightAdjust) + delta, -140, 140); break;
                case "InnerTop": TopAdjust = Math.Clamp(Math.Round(TopAdjust) + delta, -180, 180); break;
                case "InnerBottom": BottomAdjust = Math.Clamp(Math.Round(BottomAdjust) + delta, -180, 180); break;
                default: return;
            }

            ApplyManualAdjustments();
            NotifyGuidePositionsChanged();
            StatusMessage = "Guide moved. Align green to the physical card edge and yellow to the printed/image edge.";
        }

        private void ApplyManualAdjustments()
        {
            currentMeasurement = baseMeasurement.Clone();
            currentMeasurement.OuterLeft = Math.Clamp((int)Math.Round(OuterLeftAdjust), 0, CanonicalWidth / 5);
            currentMeasurement.OuterRight = Math.Clamp(CanonicalWidth - 1 - (int)Math.Round(OuterRightAdjust), CanonicalWidth * 4 / 5, CanonicalWidth - 1);
            currentMeasurement.OuterTop = Math.Clamp((int)Math.Round(OuterTopAdjust), 0, CanonicalHeight / 5);
            currentMeasurement.OuterBottom = Math.Clamp(CanonicalHeight - 1 - (int)Math.Round(OuterBottomAdjust), CanonicalHeight * 4 / 5, CanonicalHeight - 1);

            currentMeasurement.InnerLeft = Math.Clamp(baseMeasurement.InnerLeft + (int)Math.Round(LeftAdjust), currentMeasurement.OuterLeft + 2, currentMeasurement.OuterRight - 4);
            currentMeasurement.InnerRight = Math.Clamp(baseMeasurement.InnerRight + (int)Math.Round(RightAdjust), currentMeasurement.InnerLeft + 2, currentMeasurement.OuterRight - 2);
            currentMeasurement.InnerTop = Math.Clamp(baseMeasurement.InnerTop + (int)Math.Round(TopAdjust), currentMeasurement.OuterTop + 2, currentMeasurement.OuterBottom - 4);
            currentMeasurement.InnerBottom = Math.Clamp(baseMeasurement.InnerBottom + (int)Math.Round(BottomAdjust), currentMeasurement.InnerTop + 2, currentMeasurement.OuterBottom - 2);

            RecalculateFromGuideLines();
            UpdateDisplayedMeasurements();
            NotifyGuidePositionsChanged();
        }

        private void RecalculateFromGuideLines()
        {
            currentMeasurement.LeftInset = Math.Max(1, currentMeasurement.InnerLeft - currentMeasurement.OuterLeft);
            currentMeasurement.RightInset = Math.Max(1, currentMeasurement.OuterRight - currentMeasurement.InnerRight);
            currentMeasurement.TopInset = Math.Max(1, currentMeasurement.InnerTop - currentMeasurement.OuterTop);
            currentMeasurement.BottomInset = Math.Max(1, currentMeasurement.OuterBottom - currentMeasurement.InnerBottom);

            float horizontalTotal = currentMeasurement.LeftInset + currentMeasurement.RightInset;
            float verticalTotal = currentMeasurement.TopInset + currentMeasurement.BottomInset;

            currentMeasurement.LeftPercent = currentMeasurement.LeftInset / horizontalTotal * 100.0f;
            currentMeasurement.RightPercent = 100.0f - currentMeasurement.LeftPercent;
            currentMeasurement.TopPercent = currentMeasurement.TopInset / verticalTotal * 100.0f;
            currentMeasurement.BottomPercent = 100.0f - currentMeasurement.TopPercent;
        }

        private static CenteringMeasurement CreateFallbackMeasurement()
        {
            int left = (int)Math.Round(CanonicalWidth * 0.10);
            int right = (int)Math.Round(CanonicalWidth * 0.90);
            int top = (int)Math.Round(CanonicalHeight * 0.08);
            int bottom = (int)Math.Round(CanonicalHeight * 0.92);

            return new CenteringMeasurement
            {
                Success = true,
                OuterLeft = 0,
                OuterRight = CanonicalWidth - 1,
                OuterTop = 0,
                OuterBottom = CanonicalHeight - 1,
                InnerLeft = left,
                InnerRight = right,
                InnerTop = top,
                InnerBottom = bottom,
                Confidence = 25.0f
            };
        }

        private async Task RebuildOverlayAsync()
        {
            if (string.IsNullOrWhiteSpace(canonicalImagePath) ||
                !File.Exists(canonicalImagePath) ||
                string.IsNullOrWhiteSpace(outputDirectory))
                return;

            long version = Interlocked.Increment(ref overlayRenderVersion);
            using ImageSharpImage normalized = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(canonicalImagePath);

            // Green rectangle = physical card perimeter.
            DrawRectangle(
                normalized,
                currentMeasurement.OuterLeft,
                currentMeasurement.OuterTop,
                currentMeasurement.OuterRight,
                currentMeasurement.OuterBottom,
                new Rgba32(0, 230, 118, 255),
                3);

            // Yellow rectangle = printed/image/frame perimeter used for centering.
            DrawRectangle(
                normalized,
                currentMeasurement.InnerLeft,
                currentMeasurement.InnerTop,
                currentMeasurement.InnerRight,
                currentMeasurement.InnerBottom,
                new Rgba32(255, 214, 10, 255),
                3);

            string nextOverlayPath = Path.Combine(outputDirectory, $"centering_guides_{version:000000}.png");
            await using FileStream stream = new(nextOverlayPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await normalized.SaveAsync(stream, new PngEncoder());

            if (version == Interlocked.Read(ref overlayRenderVersion))
            {
                overlayDisplayPath = nextOverlayPath;
                MeasurementImageSource = ImageSource.FromFile(nextOverlayPath);
                HasMeasurementImage = true;
            }
        }

        private void UpdateDisplayedMeasurements()
        {
            HorizontalCenteringText = $"{currentMeasurement.LeftPercent:0}/{currentMeasurement.RightPercent:0}";
            VerticalCenteringText = $"{currentMeasurement.TopPercent:0}/{currentMeasurement.BottomPercent:0}";
            ConfidencePercent = currentMeasurement.Confidence;
            CenteringSummary = $"Measured centering • Left/Right {HorizontalCenteringText} • Top/Bottom {VerticalCenteringText}";
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
                OuterLeft = 0,
                OuterRight = CanonicalWidth - 1,
                OuterTop = 0,
                OuterBottom = CanonicalHeight - 1,
                InnerLeft = leftInset,
                InnerRight = CanonicalWidth - 1 - rightInset,
                InnerTop = topInset,
                InnerBottom = CanonicalHeight - 1 - bottomInset,
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

        /// <summary>
        /// Creates a full-frame display copy without cropping. Only resolution is
        /// reduced; the complete captured scene remains visible.
        /// </summary>
        private static ImageSharpImage CreateBoundedPreview(
            ImageSharpImage source,
            int maximumDimension)
        {
            int largest = Math.Max(source.Width, source.Height);

            if (largest <= maximumDimension)
            {
                return source.Clone();
            }

            double scale = maximumDimension / (double)largest;
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));

            return source.Clone(context =>
                context.Resize(new ResizeOptions
                {
                    Size = new SixLabors.ImageSharp.Size(width, height),
                    Mode = SixLabors.ImageSharp.Processing.ResizeMode.Stretch,
                    Sampler = KnownResamplers.Bicubic
                }));
        }

        /// <summary>
        /// Maps geometry from the source photo into the bounded full-frame preview.
        /// </summary>
        private static CardPoint[] ScaleCorners(
            IReadOnlyList<CardPoint> corners,
            int sourceWidth,
            int sourceHeight,
            int targetWidth,
            int targetHeight)
        {
            double scaleX = targetWidth / (double)Math.Max(sourceWidth, 1);
            double scaleY = targetHeight / (double)Math.Max(sourceHeight, 1);

            return corners
                .Select(point => new CardPoint(
                    (float)(point.X * scaleX),
                    (float)(point.Y * scaleY)))
                .ToArray();
        }

        private static ImageSharpImage WarpToCanonical(
            ImageSharpImage source,
            IReadOnlyList<CardPoint> sourceCorners,
            CancellationToken cancellationToken)
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
                    if ((y & 31) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

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

        private static async Task SaveCenteringOverlayOnFullPhotoAsync(
            ImageSharpImage fullPhoto,
            IReadOnlyList<CardPoint> outerCorners,
            CenteringMeasurement m,
            string path,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using ImageSharpImage overlay = fullPhoto.Clone();

            // The outer green quadrilateral is the physical card detected in the
            // original/full capture. Nothing is cropped or zoomed for display.
            Rgba32 outerColor = new(57, 255, 20, 255);
            DrawPolygon(overlay, outerCorners, outerColor, 5);

            // Map the measurement-only normalized coordinates back into the original
            // photo so the yellow centering frame is shown in the correct physical
            // location without replacing the user's capture with a card crop.
            CardPoint[] canonicalCorners =
            {
                new(0, 0),
                new(CanonicalWidth - 1, 0),
                new(CanonicalWidth - 1, CanonicalHeight - 1),
                new(0, CanonicalHeight - 1)
            };

            double[] canonicalToSource = SolveHomography(canonicalCorners, outerCorners);

            float innerLeft = m.LeftInset;
            float innerTop = m.TopInset;
            float innerRight = CanonicalWidth - m.RightInset - 1;
            float innerBottom = CanonicalHeight - m.BottomInset - 1;

            CardPoint[] innerCanonical =
            {
                new(innerLeft, innerTop),
                new(innerRight, innerTop),
                new(innerRight, innerBottom),
                new(innerLeft, innerBottom)
            };

            CardPoint[] innerSource = innerCanonical
                .Select(p => MapProjectivePoint(canonicalToSource, p.X, p.Y))
                .ToArray();

            DrawPolygon(overlay, innerSource, new Rgba32(255, 221, 0, 255), 5);

            // Cyan center axes are also mapped back onto the full photo.
            CardPoint centerTop = MapProjectivePoint(canonicalToSource, CanonicalWidth / 2.0f, 0);
            CardPoint centerBottom = MapProjectivePoint(canonicalToSource, CanonicalWidth / 2.0f, CanonicalHeight - 1);
            CardPoint centerLeft = MapProjectivePoint(canonicalToSource, 0, CanonicalHeight / 2.0f);
            CardPoint centerRight = MapProjectivePoint(canonicalToSource, CanonicalWidth - 1, CanonicalHeight / 2.0f);

            DrawLine(
                overlay,
                (int)Math.Round(centerTop.X),
                (int)Math.Round(centerTop.Y),
                (int)Math.Round(centerBottom.X),
                (int)Math.Round(centerBottom.Y),
                new Rgba32(0, 225, 255, 210));

            DrawLine(
                overlay,
                (int)Math.Round(centerLeft.X),
                (int)Math.Round(centerLeft.Y),
                (int)Math.Round(centerRight.X),
                (int)Math.Round(centerRight.Y),
                new Rgba32(0, 225, 255, 210));

            await using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
            await overlay.SaveAsync(
                stream,
                new PngEncoder(),
                cancellationToken);
        }

        private static CardPoint MapProjectivePoint(double[] matrix, float x, float y)
        {
            MapProjective(matrix, x, y, out float mappedX, out float mappedY);
            return new CardPoint(mappedX, mappedY);
        }

        private static void DrawPolygon(
            ImageSharpImage image,
            IReadOnlyList<CardPoint> points,
            Rgba32 color,
            int thickness)
        {
            if (points.Count < 2)
                return;

            for (int i = 0; i < points.Count; i++)
            {
                CardPoint a = points[i];
                CardPoint b = points[(i + 1) % points.Count];

                for (int t = -(thickness / 2); t <= thickness / 2; t++)
                {
                    DrawLine(
                        image,
                        (int)Math.Round(a.X) + t,
                        (int)Math.Round(a.Y),
                        (int)Math.Round(b.X) + t,
                        (int)Math.Round(b.Y),
                        color);
                    DrawLine(
                        image,
                        (int)Math.Round(a.X),
                        (int)Math.Round(a.Y) + t,
                        (int)Math.Round(b.X),
                        (int)Math.Round(b.Y) + t,
                        color);
                }
            }
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

        /// <summary>
        /// Worker-thread output for one complete centering run.
        /// </summary>
        private sealed class CenteringAnalysisResult
        {
            public string OutputDirectory { get; init; } = string.Empty;

            public string OrientedFullImagePath { get; init; } = string.Empty;

            public string CanonicalImagePath { get; init; } = string.Empty;

            public string OverlayImagePath { get; init; } = string.Empty;

            public CardPoint[] OuterCorners { get; init; } = Array.Empty<CardPoint>();

            public CenteringMeasurement Measurement { get; init; } = new();

            public double GeometryConfidence { get; init; }
        }

        private sealed class CenteringMeasurement
        {
            public bool Success { get; set; }
            public int OuterLeft { get; set; }
            public int OuterRight { get; set; }
            public int OuterTop { get; set; }
            public int OuterBottom { get; set; }
            public int InnerLeft { get; set; }
            public int InnerRight { get; set; }
            public int InnerTop { get; set; }
            public int InnerBottom { get; set; }
            public int LeftInset { get; set; }
            public int RightInset { get; set; }
            public int TopInset { get; set; }
            public int BottomInset { get; set; }
            public float LeftPercent { get; set; }
            public float RightPercent { get; set; }
            public float TopPercent { get; set; }
            public float BottomPercent { get; set; }
            public float Confidence { get; set; }

            public CenteringMeasurement Clone()
            {
                return (CenteringMeasurement)MemberwiseClone();
            }
        }
    }
}
