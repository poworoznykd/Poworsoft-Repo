using CollectIQ.Interfaces;
using CollectIQ.Models.Inspection.Geometry;
using OpenCvSharp;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImageSharpImage = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;

namespace CollectIQ.Services.Inspection.Geometry
{
    /// <summary>
    /// Common physical-card normalization used before every inspection type.
    /// Detection is delegated to CardGeometryService; this class owns the
    /// orientation, perspective flattening and fixed-ratio output.
    /// </summary>
    public sealed class CardNormalizationService : ICardNormalizationService
    {
        private const int WorkingMaximumDimension = 1200;
        private readonly ICardGeometryService geometryService;

        public CardNormalizationService(ICardGeometryService geometryService)
        {
            this.geometryService = geometryService;
        }

        public async Task<CardNormalizationResult> NormalizeAsync(
            string imagePath,
            string outputDirectory,
            int normalizedWidth,
            int normalizedHeight,
            IProgress<CardNormalizationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                throw new FileNotFoundException("The inspection image could not be found.", imagePath);

            if (normalizedWidth < 200 || normalizedHeight < 280)
                throw new ArgumentOutOfRangeException(nameof(normalizedWidth), "The normalized inspection image is too small.");

            Directory.CreateDirectory(outputDirectory);
            cancellationToken.ThrowIfCancellationRequested();

            Stopwatch totalTimer = Stopwatch.StartNew();
            Stopwatch stageTimer = Stopwatch.StartNew();

            progress?.Report(new CardNormalizationProgress
            {
                Stage = "LOAD",
                Message = "Loading captured image…"
            });

            await InspectionDiagnosticLogger.WriteAsync("CardNormalization", "LOAD START", imagePath);

            using ImageSharpImage source = SixLabors.ImageSharp.Image.Load<Rgba32>(imagePath);

            // Respect the camera/EXIF orientation exactly once. Do NOT rotate again
            // based only on Width/Height; Android may already have encoded the
            // correct visual orientation in the metadata.
            source.Mutate(x => x.AutoOrient());

            await InspectionDiagnosticLogger.WriteAsync(
                "CardNormalization",
                "LOAD COMPLETE",
                $"{source.Width}x{source.Height}; EXIF orientation applied once; " +
                $"Elapsed={stageTimer.Elapsed.TotalSeconds:0.00}s");

            stageTimer.Restart();
            cancellationToken.ThrowIfCancellationRequested();

            CardGeometryResult geometry;

            // Generate a bounded, auto-oriented preview before attempting any
            // rectangle detector. Android must never render the full camera bitmap
            // inside the interactive manual fallback.
            using (ImageSharpImage preview = CreateBoundedPreview(source, 900))
            {
                string manualPreviewPath = Path.Combine(outputDirectory, "card_preview.jpg");
                await using (FileStream manualPreviewStream = File.Create(manualPreviewPath))
                {
                    await preview.SaveAsync(
                        manualPreviewStream,
                        new JpegEncoder { Quality = 88 },
                        cancellationToken);
                }

                progress?.Report(new CardNormalizationProgress
                {
                    Stage = "PREVIEW_READY",
                    Message = "Card preview ready. Creating the Canny edge image…",
                    PreviewImagePath = manualPreviewPath
                });

                // Same grayscale / Gaussian / Canny stages as the rectangle search,
                // displayed BEFORE detection so a timeout cannot hide edge evidence.
                using Mat previewBgr = CreateBgrMat(preview);
                using Mat gray = new();
                using Mat blurred = new();
                using Mat edges = new();
                Cv2.CvtColor(previewBgr, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.GaussianBlur(gray, blurred, new OpenCvSharp.Size(5, 5), 0);
                Cv2.Canny(blurred, edges, 75, 200);
                string edgePath = Path.Combine(outputDirectory, "card_edges.jpg");
                if (!Cv2.ImWrite(edgePath, edges))
                    throw new IOException("Could not save the physical-edge preview.");

                progress?.Report(new CardNormalizationProgress
                {
                    Stage = "EDGES_READY",
                    Message = "Canny edge map ready. Checking rectangular contours…",
                    PreviewImagePath = manualPreviewPath,
                    EdgeImagePath = edgePath
                });

                // Visualize the actual quadrilateral candidates before choosing a card.
                // This diagnostic is available even when every detector rejects the image.
                using Mat candidates = previewBgr.Clone();
                Cv2.FindContours(edges, out OpenCvSharp.Point[][] contours,
                    out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);
                CardPoint[]? selectedPreviewCorners = null;
                double bestScore = double.NegativeInfinity;
                int candidatesDrawn = 0;
                foreach (OpenCvSharp.Point[] contour in contours)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    double perimeter = Cv2.ArcLength(contour, true);
                    if (perimeter < 80) continue;
                    OpenCvSharp.Point[] quad = Cv2.ApproxPolyDP(contour, perimeter * 0.02, true);
                    if (quad.Length != 4 || !Cv2.IsContourConvex(quad)) continue;
                    double area = Math.Abs(Cv2.ContourArea(quad));
                    double imageArea = preview.Width * (double)preview.Height;
                    if (area < imageArea * 0.06 || area > imageArea * 0.94) continue;
                    OpenCvSharp.Rect box = Cv2.BoundingRect(quad);
                    if (box.X <= 1 || box.Y <= 1 || box.Right >= preview.Width - 1 ||
                        box.Bottom >= preview.Height - 1) continue;
                    double ratio = Math.Min(box.Width, box.Height) /
                                   (double)Math.Max(box.Width, box.Height);
                    if (ratio < 0.48 || ratio > 0.92) continue;
                    CardPoint[]? ordered = OrderQuad(quad);
                    if (ordered is null) continue;
                    if (candidatesDrawn < 40)
                    {
                        Cv2.Polylines(candidates, new[] { quad }, true,
                            new Scalar(255, 100, 255), 2);
                        candidatesDrawn++;
                    }
                    double centerOffset = Math.Abs((box.X + box.Width / 2.0) / preview.Width - 0.5)
                                        + Math.Abs((box.Y + box.Height / 2.0) / preview.Height - 0.5);
                    double score = area / imageArea - 0.13 * Math.Abs(ratio - 2.5 / 3.5)
                                   - 0.07 * centerOffset;
                    if (score <= bestScore) continue;
                    bestScore = score;
                    selectedPreviewCorners = ordered;
                }
                if (selectedPreviewCorners is not null)
                {
                    OpenCvSharp.Point[] chosen = selectedPreviewCorners
                        .Select(pt => new OpenCvSharp.Point((int)Math.Round(pt.X), (int)Math.Round(pt.Y)))
                        .ToArray();
                    Cv2.Polylines(candidates, new[] { chosen }, true, new Scalar(0, 255, 0), 3);
                    foreach (OpenCvSharp.Point point in chosen)
                        Cv2.Circle(candidates, point, 7, new Scalar(0, 255, 255), 2);
                }
                string rectanglePath = Path.Combine(outputDirectory, "rectangle_candidates.jpg");
                if (!Cv2.ImWrite(rectanglePath, candidates))
                    throw new IOException("Could not save rectangle detection view.");
                progress?.Report(new CardNormalizationProgress
                {
                    Stage = "RECTANGLE_CANDIDATES_READY",
                    Message = selectedPreviewCorners is null
                        ? "Rectangle candidates shown; running four-edge fallback…"
                        : "Rectangle candidate selected. Creating Card Lock…",
                    RectangleImagePath = rectanglePath
                });

                // Do not send the full multi-megapixel photo through multiple
                // segmentation and contour passes. Use this already-oriented 900px
                // preview. The source-corner coordinates are scaled back afterward.
                if (selectedPreviewCorners is not null)
                {
                    geometry = new CardGeometryResult
                    {
                        Success = true,
                        Corners = ScaleCorners(selectedPreviewCorners,
                            preview.Width, preview.Height, source.Width, source.Height),
                        SourceWidth = source.Width,
                        SourceHeight = source.Height,
                        Confidence = Math.Clamp(bestScore, 0.05, 1.0)
                    };
                }
                else
                {
                    progress?.Report(new CardNormalizationProgress
                    {
                        Stage = "DETECT",
                        Message = "Fitting outer card edges from the bounded image…"
                    });
                    CardGeometryResult boundedGeometry = geometryService.DetectCard(preview);
                    geometry = new CardGeometryResult
                    {
                        Success = boundedGeometry.Success,
                        Corners = boundedGeometry.Success && boundedGeometry.Corners.Length == 4
                            ? ScaleCorners(boundedGeometry.Corners,
                                preview.Width, preview.Height, source.Width, source.Height)
                            : Array.Empty<CardPoint>(),
                        SourceWidth = source.Width,
                        SourceHeight = source.Height,
                        Confidence = boundedGeometry.Confidence
                    };
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            await InspectionDiagnosticLogger.WriteAsync(
                "CardNormalization", "RECTANGLE DETECTION COMPLETE",
                $"Success={geometry.Success}; Confidence={geometry.Confidence:0.000}; " +
                $"Elapsed={stageTimer.Elapsed.TotalSeconds:0.00}s");


            if (!geometry.Success || geometry.Corners.Length != 4)
            {
                throw new InvalidOperationException(
                    "CollectIQ could not find one complete physical card rectangle. " +
                    "Keep all four card edges visible on a plain contrasting background.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new CardNormalizationProgress
            {
                Stage = "PREPARE",
                Message = "Preparing the detected card for normalization…"
            });

            await InspectionDiagnosticLogger.WriteAsync(
                "CardNormalization",
                "BUILD WORKING IMAGE START",
                $"Source={source.Width}x{source.Height}; Max={WorkingMaximumDimension}");

            using ImageSharpImage working = CreateBoundedPreview(
                source,
                WorkingMaximumDimension);

            CardPoint[] workingCorners = ScaleCorners(
                geometry.Corners,
                source.Width,
                source.Height,
                working.Width,
                working.Height);

            await InspectionDiagnosticLogger.WriteAsync(
                "CardNormalization",
                "BUILD WORKING IMAGE COMPLETE",
                $"Working={working.Width}x{working.Height}; " +
                $"Elapsed={stageTimer.Elapsed.TotalSeconds:0.00}s");

            stageTimer.Restart();

            // The original capture already exists and is shown directly in the
            // UI. Do not spend time encoding a second source-preview PNG before
            // Card Lock / TrueForm.
            string previewPath = imagePath;

            await InspectionDiagnosticLogger.WriteAsync(
                "CardNormalization",
                "SOURCE PREVIEW REUSE",
                "Using the original capture directly; no duplicate PNG encode.");

            stageTimer.Restart();
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new CardNormalizationProgress
            {
                Stage = "CARD_LOCK",
                Message = "Creating Card Lock View so you can verify the detected card…"
            });

            await InspectionDiagnosticLogger.WriteAsync(
                "CardNormalization",
                "CARD LOCK OVERLAY START");

            string detectionOverlayPath =
                Path.Combine(outputDirectory, "card_lock_view.jpg");

            using (ImageSharpImage detectionOverlay = working.Clone())
            {
                DrawDetectedCardOverlay(
                    detectionOverlay,
                    workingCorners,
                    new Rgba32(0, 255, 70, 255));

                await using FileStream detectionOverlayStream =
                    File.Create(detectionOverlayPath);

                // Card Lock is a visual checkpoint, not the measurement source.
                // JPEG is dramatically cheaper to encode than a large PNG on
                // Android and is more than sufficient for the detection proof.
                await detectionOverlay.SaveAsync(
                    detectionOverlayStream,
                    new JpegEncoder { Quality = 92 },
                    cancellationToken);
            }

            await InspectionDiagnosticLogger.WriteAsync(
                "CardNormalization",
                "CARD LOCK OVERLAY COMPLETE",
                $"{detectionOverlayPath}; Elapsed={stageTimer.Elapsed.TotalSeconds:0.00}s");

            progress?.Report(new CardNormalizationProgress
            {
                Stage = "CARD_LOCK_COMPLETE",
                Message = "Card Lock complete. The neon-green rectangle and four corner targets are the exact geometry CollectIQ found. TrueForm starts next.",
                CardLockImagePath = detectionOverlayPath
            });

            // Card Lock is an explicit user-visible checkpoint. Give the UI event
            // loop a short opportunity to render it before the transform begins.
            await Task.Delay(150, cancellationToken);

            stageTimer.Restart();
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new CardNormalizationProgress
            {
                Stage = "TRUEFORM",
                Message = "Flattening and scaling the detected card into TrueForm View…"
            });

            await InspectionDiagnosticLogger.WriteAsync(
                "CardNormalization",
                "PERSPECTIVE NORMALIZATION START",
                $"Working={working.Width}x{working.Height}; Output={normalizedWidth}x{normalizedHeight}");

            string normalizedPath = Path.Combine(outputDirectory, "normalized_card.png");

            // Native OpenCV performs the perspective transform in optimized native
            // code. Do not map/sample 787,500 output pixels in managed C#.
            WarpPerspectiveWithOpenCv(
                working,
                workingCorners,
                normalizedWidth,
                normalizedHeight,
                normalizedPath,
                cancellationToken);

            await InspectionDiagnosticLogger.WriteAsync(
                "CardNormalization",
                "PERSPECTIVE NORMALIZATION COMPLETE",
                $"{normalizedPath}; Elapsed={stageTimer.Elapsed.TotalSeconds:0.00}s; " +
                $"Total={totalTimer.Elapsed.TotalSeconds:0.00}s");

            progress?.Report(new CardNormalizationProgress
            {
                Stage = "TRUEFORM_COMPLETE",
                Message = "TrueForm View created. Measuring the printed/image frame for automatic centering…",
                CardLockImagePath = detectionOverlayPath,
                TrueFormImagePath = normalizedPath
            });

            return new CardNormalizationResult
            {
                SourcePreviewPath = previewPath,
                DetectionOverlayPath = detectionOverlayPath,
                NormalizedImagePath = normalizedPath,
                SourceCorners = geometry.Corners.ToArray(),
                GeometryConfidence = geometry.Confidence,
                NormalizedWidth = normalizedWidth,
                NormalizedHeight = normalizedHeight
            };
        }

        public async Task<CardNormalizationResult> NormalizeFromRelativeRectangleAsync(
            string imagePath,
            string outputDirectory,
            double left,
            double right,
            double top,
            double bottom,
            int normalizedWidth,
            int normalizedHeight,
            IProgress<CardNormalizationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                throw new FileNotFoundException("The inspection image could not be found.", imagePath);

            left = Math.Clamp(left, 0.0, 0.95);
            right = Math.Clamp(right, 0.05, 1.0);
            top = Math.Clamp(top, 0.0, 0.95);
            bottom = Math.Clamp(bottom, 0.05, 1.0);

            if (right - left < 0.12 || bottom - top < 0.12)
                throw new InvalidOperationException("The manual card rectangle is too small.");

            Directory.CreateDirectory(outputDirectory);
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new CardNormalizationProgress
            {
                Stage = "MANUAL_LOAD",
                Message = "Using your four green card-edge lines…"
            });

            using ImageSharpImage source = SixLabors.ImageSharp.Image.Load<Rgba32>(imagePath);
            source.Mutate(x => x.AutoOrient());

            CardPoint[] sourceCorners =
            {
                new((float)(left * (source.Width - 1)),  (float)(top * (source.Height - 1))),
                new((float)(right * (source.Width - 1)), (float)(top * (source.Height - 1))),
                new((float)(right * (source.Width - 1)), (float)(bottom * (source.Height - 1))),
                new((float)(left * (source.Width - 1)),  (float)(bottom * (source.Height - 1)))
            };

            progress?.Report(new CardNormalizationProgress
            {
                Stage = "MANUAL_CARD_LOCK",
                Message = "Creating Card Lock from your manual outer-card lines…"
            });

            using ImageSharpImage working = CreateBoundedPreview(source, WorkingMaximumDimension);
            CardPoint[] workingCorners = ScaleCorners(
                sourceCorners,
                source.Width,
                source.Height,
                working.Width,
                working.Height);

            string detectionOverlayPath = Path.Combine(
                outputDirectory,
                "manual_card_lock_view.jpg");

            using (ImageSharpImage detectionOverlay = working.Clone())
            {
                DrawDetectedCardOverlay(
                    detectionOverlay,
                    workingCorners,
                    new Rgba32(0, 255, 70, 255));

                await using FileStream overlayStream = File.Create(detectionOverlayPath);
                await detectionOverlay.SaveAsync(
                    overlayStream,
                    new JpegEncoder { Quality = 92 },
                    cancellationToken);
            }

            progress?.Report(new CardNormalizationProgress
            {
                Stage = "MANUAL_CARD_LOCK_COMPLETE",
                Message = "Manual Card Lock complete. The green rectangle is exactly what will be flattened next.",
                CardLockImagePath = detectionOverlayPath
            });

            await Task.Delay(100, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new CardNormalizationProgress
            {
                Stage = "MANUAL_TRUEFORM",
                Message = "Flattening your manual card rectangle into TrueForm View…"
            });

            string normalizedPath = Path.Combine(
                outputDirectory,
                "manual_normalized_card.png");

            WarpPerspectiveWithOpenCv(
                working,
                workingCorners,
                normalizedWidth,
                normalizedHeight,
                normalizedPath,
                cancellationToken);

            progress?.Report(new CardNormalizationProgress
            {
                Stage = "MANUAL_TRUEFORM_COMPLETE",
                Message = "TrueForm created from your card edges. Measuring centering next…",
                CardLockImagePath = detectionOverlayPath,
                TrueFormImagePath = normalizedPath
            });

            return new CardNormalizationResult
            {
                SourcePreviewPath = imagePath,
                DetectionOverlayPath = detectionOverlayPath,
                NormalizedImagePath = normalizedPath,
                SourceCorners = sourceCorners,
                GeometryConfidence = 1.0,
                NormalizedWidth = normalizedWidth,
                NormalizedHeight = normalizedHeight
            };
        }

        private static ImageSharpImage CreateBoundedPreview(ImageSharpImage source, int maximumDimension)
        {
            int largest = Math.Max(source.Width, source.Height);
            if (largest <= maximumDimension)
                return source.Clone();

            double scale = maximumDimension / (double)largest;
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));

            return source.Clone(context => context.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(width, height),
                Mode = SixLabors.ImageSharp.Processing.ResizeMode.Stretch,
                Sampler = KnownResamplers.Bicubic
            }));
        }

        /// <summary>
        /// Draws the detected physical-card quadrilateral and four corner targets.
        /// This is intentionally generated before perspective normalization so the
        /// user can verify that CollectIQ locked onto the correct physical card.
        /// </summary>
        private static void DrawDetectedCardOverlay(
            ImageSharpImage image,
            IReadOnlyList<CardPoint> corners,
            Rgba32 color)
        {
            if (corners.Count != 4)
                return;

            for (int i = 0; i < 4; i++)
            {
                CardPoint start = corners[i];
                CardPoint end = corners[(i + 1) % 4];
                DrawLine(
                    image,
                    (int)Math.Round(start.X),
                    (int)Math.Round(start.Y),
                    (int)Math.Round(end.X),
                    (int)Math.Round(end.Y),
                    color,
                    10);
            }

            foreach (CardPoint corner in corners)
            {
                DrawCornerTarget(
                    image,
                    (int)Math.Round(corner.X),
                    (int)Math.Round(corner.Y),
                    color);
            }
        }

        private static void DrawCornerTarget(
            ImageSharpImage image,
            int cx,
            int cy,
            Rgba32 color)
        {
            const int radius = 20;

            DrawLine(image, cx - radius, cy, cx + radius, cy, color, 8);
            DrawLine(image, cx, cy - radius, cx, cy + radius, color, 8);
        }

        private static void DrawLine(
            ImageSharpImage image,
            int x1,
            int y1,
            int x2,
            int y2,
            Rgba32 color,
            int thickness)
        {
            int dx = Math.Abs(x2 - x1);
            int sx = x1 < x2 ? 1 : -1;
            int dy = -Math.Abs(y2 - y1);
            int sy = y1 < y2 ? 1 : -1;
            int error = dx + dy;

            while (true)
            {
                DrawThickPoint(image, x1, y1, color, thickness);

                if (x1 == x2 && y1 == y2)
                    break;

                int twiceError = 2 * error;

                if (twiceError >= dy)
                {
                    error += dy;
                    x1 += sx;
                }

                if (twiceError <= dx)
                {
                    error += dx;
                    y1 += sy;
                }
            }
        }

        private static void DrawThickPoint(
            ImageSharpImage image,
            int cx,
            int cy,
            Rgba32 color,
            int thickness)
        {
            int radius = Math.Max(1, thickness / 2);

            for (int y = cy - radius; y <= cy + radius; y++)
            {
                if (y < 0 || y >= image.Height)
                    continue;

                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    if (x < 0 || x >= image.Width)
                        continue;

                    image[x, y] = color;
                }
            }
        }

        private static CardPoint[]? OrderQuad(OpenCvSharp.Point[] points)
        {
            if (points.Length != 4) return null;
            OpenCvSharp.Point tl = points.OrderBy(p => p.X + p.Y).First();
            OpenCvSharp.Point br = points.OrderByDescending(p => p.X + p.Y).First();
            OpenCvSharp.Point tr = points.OrderByDescending(p => p.X - p.Y).First();
            OpenCvSharp.Point bl = points.OrderBy(p => p.X - p.Y).First();
            if (new[] { tl, tr, br, bl }.Distinct().Count() != 4) return null;
            return new[] {
                new CardPoint(tl.X, tl.Y), new CardPoint(tr.X, tr.Y),
                new CardPoint(br.X, br.Y), new CardPoint(bl.X, bl.Y) };
        }

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

        /// <summary>
        /// Uses OpenCV's native perspective warp for the normalized card.
        /// The four input corners must be ordered TL, TR, BR, BL.
        /// </summary>
        private static void WarpPerspectiveWithOpenCv(
            ImageSharpImage source,
            IReadOnlyList<CardPoint> sourceCorners,
            int width,
            int height,
            string outputPath,
            CancellationToken cancellationToken)
        {
            if (sourceCorners.Count != 4)
                throw new ArgumentException("Four ordered card corners are required.", nameof(sourceCorners));

            cancellationToken.ThrowIfCancellationRequested();

            using Mat sourceBgr = CreateBgrMat(source);

            Point2f[] src =
            {
                new(sourceCorners[0].X, sourceCorners[0].Y),
                new(sourceCorners[1].X, sourceCorners[1].Y),
                new(sourceCorners[2].X, sourceCorners[2].Y),
                new(sourceCorners[3].X, sourceCorners[3].Y)
            };

            Point2f[] dst =
            {
                new(0, 0),
                new(width - 1, 0),
                new(width - 1, height - 1),
                new(0, height - 1)
            };

            using Mat transform = Cv2.GetPerspectiveTransform(src, dst);
            using Mat normalized = new();

            Cv2.WarpPerspective(
                sourceBgr,
                normalized,
                transform,
                new OpenCvSharp.Size(width, height),
                InterpolationFlags.Linear,
                BorderTypes.Constant,
                Scalar.Black);

            cancellationToken.ThrowIfCancellationRequested();

            if (normalized.Empty())
                throw new InvalidOperationException("OpenCV produced an empty TrueForm image.");

            if (!Cv2.ImWrite(outputPath, normalized))
                throw new IOException("OpenCV could not save the TrueForm image.");
        }

        /// <summary>
        /// Converts an ImageSharp RGBA image into an OpenCV BGR Mat once.
        /// </summary>
        private static Mat CreateBgrMat(ImageSharpImage image)
        {
            byte[] rgbaBytes = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(rgbaBytes);

            using Mat rgba = new(image.Height, image.Width, MatType.CV_8UC4);
            Marshal.Copy(rgbaBytes, 0, rgba.Data, rgbaBytes.Length);

            Mat bgr = new();
            Cv2.CvtColor(rgba, bgr, ColorConversionCodes.RGBA2BGR);
            return bgr;
        }

        private static ImageSharpImage WarpToRectangle(
            ImageSharpImage source,
            IReadOnlyList<CardPoint> sourceCorners,
            int width,
            int height,
            CancellationToken cancellationToken)
        {
            CardPoint[] destinationCorners =
            {
                new(0, 0),
                new(width - 1, 0),
                new(width - 1, height - 1),
                new(0, height - 1)
            };

            double[] destinationToSource = SolveHomography(destinationCorners, sourceCorners);

            cancellationToken.ThrowIfCancellationRequested();

            Rgba32[] sourcePixels = new Rgba32[source.Width * source.Height];
            source.CopyPixelDataTo(sourcePixels);

            cancellationToken.ThrowIfCancellationRequested();

            ImageSharpImage output = new(width, height);
            output.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    if ((y & 31) == 0)
                        cancellationToken.ThrowIfCancellationRequested();

                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        if ((x & 255) == 0)
                            cancellationToken.ThrowIfCancellationRequested();

                        MapProjective(destinationToSource, x, y, out float sx, out float sy);
                        row[x] = SampleBilinear(sourcePixels, source.Width, source.Height, sx, sy);
                    }
                }
            });

            return output;
        }

        private static Rgba32 SampleBilinear(Rgba32[] pixels, int width, int height, float x, float y)
        {
            if (float.IsNaN(x) || float.IsNaN(y) || x < 0 || y < 0 || x > width - 1 || y > height - 1)
                return new Rgba32(0, 0, 0, 255);

            int x0 = (int)MathF.Floor(x);
            int y0 = (int)MathF.Floor(y);
            int x1 = Math.Min(x0 + 1, width - 1);
            int y1 = Math.Min(y0 + 1, height - 1);
            float tx = x - x0;
            float ty = y - y0;

            Rgba32 c00 = pixels[(y0 * width) + x0];
            Rgba32 c10 = pixels[(y0 * width) + x1];
            Rgba32 c01 = pixels[(y1 * width) + x0];
            Rgba32 c11 = pixels[(y1 * width) + x1];

            return new Rgba32(
                Blend(Blend(c00.R, c10.R, tx), Blend(c01.R, c11.R, tx), ty),
                Blend(Blend(c00.G, c10.G, tx), Blend(c01.G, c11.G, tx), ty),
                Blend(Blend(c00.B, c10.B, tx), Blend(c01.B, c11.B, tx), ty),
                Blend(Blend(c00.A, c10.A, tx), Blend(c01.A, c11.A, tx), ty));
        }

        private static byte Blend(byte a, byte b, float t) =>
            (byte)Math.Clamp(MathF.Round(a + ((b - a) * t)), 0, 255);

        private static void MapProjective(double[] matrix, float x, float y, out float sourceX, out float sourceY)
        {
            double denominator = (matrix[6] * x) + (matrix[7] * y) + matrix[8];
            if (Math.Abs(denominator) < 1e-8)
            {
                sourceX = -1;
                sourceY = -1;
                return;
            }

            sourceX = (float)(((matrix[0] * x) + (matrix[1] * y) + matrix[2]) / denominator);
            sourceY = (float)(((matrix[3] * x) + (matrix[4] * y) + matrix[5]) / denominator);
        }

        private static double[] SolveHomography(IReadOnlyList<CardPoint> destination, IReadOnlyList<CardPoint> source)
        {
            double[,] a = new double[8, 8];
            double[] b = new double[8];

            for (int i = 0; i < 4; i++)
            {
                double x = destination[i].X;
                double y = destination[i].Y;
                double u = source[i].X;
                double v = source[i].Y;
                int row = i * 2;

                a[row, 0] = x; a[row, 1] = y; a[row, 2] = 1.0;
                a[row, 6] = -u * x; a[row, 7] = -u * y; b[row] = u;
                a[row + 1, 3] = x; a[row + 1, 4] = y; a[row + 1, 5] = 1.0;
                a[row + 1, 6] = -v * x; a[row + 1, 7] = -v * y; b[row + 1] = v;
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
                for (int col = 0; col < n; col++)
                    augmented[row, col] = a[row, col];
                augmented[row, n] = b[row];
            }

            for (int pivot = 0; pivot < n; pivot++)
            {
                int bestRow = pivot;
                double bestValue = Math.Abs(augmented[pivot, pivot]);
                for (int row = pivot + 1; row < n; row++)
                {
                    double value = Math.Abs(augmented[row, pivot]);
                    if (value > bestValue)
                    {
                        bestValue = value;
                        bestRow = row;
                    }
                }

                if (bestValue < 1e-12)
                    throw new InvalidOperationException("The detected card corners cannot form a stable perspective transform.");

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
                for (int col = pivot; col <= n; col++)
                    augmented[pivot, col] /= pivotValue;

                for (int row = 0; row < n; row++)
                {
                    if (row == pivot) continue;
                    double factor = augmented[row, pivot];
                    if (Math.Abs(factor) < 1e-12) continue;
                    for (int col = pivot; col <= n; col++)
                        augmented[row, col] -= factor * augmented[pivot, col];
                }
            }

            double[] result = new double[n];
            for (int i = 0; i < n; i++)
                result[i] = augmented[i, n];
            return result;
        }
    }
}
