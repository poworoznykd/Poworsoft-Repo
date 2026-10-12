using CollectIQ.Interfaces;
using CollectIQ.Models.Inspection;
using CollectIQ.Models.Inspection.Geometry;
using CollectIQ.Models.Inspection.Registration;
using CollectIQ.Services.Inspection;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using OpenCvSharp;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace CollectIQ.Services.Inspection.Registration
{
    /// <summary>
    /// Surface-specific registration pipeline.
    ///
    /// IMPORTANT: this class deliberately exposes the five geometry stages instead
    /// of hiding them behind CardNormalizationService.  Every capture is reduced to
    /// a bounded OpenCV image, its physical card rectangle is found, the four
    /// corners are drawn for the user, and only then is the image perspective-warped
    /// into the common 750 x 1050 matrix used by the photometric solver.
    /// </summary>
    public sealed class CardRegistrationService : ICardRegistrationService
    {
        public const int CanonicalWidth = 750;
        public const int CanonicalHeight = 1050;
        private const float Epsilon = 0.0001f;
        private const int RegistrationPreviewMaximumDimension = 1000;
        private const double ExpectedPortraitRatio = 2.5 / 3.5;

        // Keep the constructor signature so existing DI/callers remain unchanged.
        // Surface does not modify or depend on the shared Centering normalizer here;
        // its five geometry checkpoints are intentionally visible and native OpenCV.
        public CardRegistrationService(ICardGeometryService geometryService)
        {
            _ = geometryService;
        }

        public Task<RegisteredCardFrame> RegisterSingleFrameAsync(
            string imagePath,
            string key,
            string outputDirectory,
            IReadOnlyList<CardPoint>? manualCorners = null,
            IProgress<SurfaceInspectionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                throw new FileNotFoundException("The card image could not be found.", imagePath);

            Directory.CreateDirectory(outputDirectory);
            return ProcessFrameAsync(
                key,
                imagePath,
                outputDirectory,
                manualCorners,
                null,
                3,
                progress,
                cancellationToken);
        }

        public async Task<CardRegistrationResult> RegisterAsync(
            IReadOnlyDictionary<string, string> captures,
            string referenceKey,
            string outputDirectory,
            IReadOnlyList<CardPoint>? manualReferenceCorners = null,
            IReadOnlyDictionary<string, IReadOnlyList<CardPoint>>? manualFrameCorners = null,
            bool stopAfterGeometry = false,
            IProgress<SurfaceInspectionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!captures.ContainsKey(referenceKey))
                throw new ArgumentException("The requested reference capture does not exist.", nameof(referenceKey));

            Directory.CreateDirectory(outputDirectory);
            Dictionary<string, RegisteredCardFrame> frames = new(StringComparer.OrdinalIgnoreCase);
            Stopwatch total = Stopwatch.StartNew();

            RegisteredCardFrame reference;
            try
            {
                reference = await ProcessFrameAsync(
                    referenceKey,
                    captures[referenceKey],
                    outputDirectory,
                    GetManualCornersForFrame(referenceKey, manualReferenceCorners, manualFrameCorners),
                    null,
                    3,
                    progress,
                    cancellationToken);
            }
            catch (SurfaceReferenceCardNotFoundException)
            {
                throw;
            }

            frames[referenceKey] = reference;
            reference.AlignmentConfidence = 100.0;

            CardPoint[] normalizedReferenceCorners = NormalizeCorners(
                reference.Geometry.Corners,
                reference.Geometry.SourceWidth,
                reference.Geometry.SourceHeight);

            string[] preferredOrder = { "Top", "Right", "Bottom", "Left" };
            int[] starts = { 19, 33, 47, 61 };

            for (int i = 0; i < preferredOrder.Length; i++)
            {
                string key = preferredOrder[i];
                if (!captures.TryGetValue(key, out string? path))
                    throw new InvalidOperationException($"The {key.ToUpperInvariant()} light capture is missing.");

                cancellationToken.ThrowIfCancellationRequested();
                RegisteredCardFrame frame = await ProcessFrameAsync(
                    key,
                    path,
                    outputDirectory,
                    GetManualCornersForFrame(key, null, manualFrameCorners),
                    normalizedReferenceCorners,
                    starts[i],
                    progress,
                    cancellationToken);
                frames[key] = frame;
            }

            List<RegisteredCardFrame> directionalFrames = preferredOrder
                .Select(key => frames[key])
                .ToList();

            if (stopAfterGeometry)
            {
                await InspectionDiagnosticLogger.WriteAsync(
                    "Surface/Registration",
                    "GEOMETRY REVIEW READY",
                    $"Elapsed={total.Elapsed.TotalSeconds:0.00}s; " +
                    string.Join("; ", frames.Values.Select(f => $"{f.Key}={f.Geometry.Confidence * 100.0:0}%")));

                double geometryOnlyQuality = frames.Values.Count == 0
                    ? 0.0
                    : frames.Values.Average(frame => frame.Geometry.Confidence * 100.0);

                return new CardRegistrationResult
                {
                    Frames = frames,
                    EdgeOverlayPath = reference.DetectionOverlayPath,
                    AlignmentAveragePath = reference.RegisteredImagePath,
                    OverallQuality = geometryOnlyQuality
                };
            }

            progress?.Report(new SurfaceInspectionProgress
            {
                Percent = 75,
                Stage = "ALL FIVE CARDS FLAT",
                Message = "REFERENCE, TOP, RIGHT, BOTTOM and LEFT all have four corners and a normalized flat image. Fine alignment is starting now.",
                RegisteredImagePath = reference.RegisteredImagePath
            });

            for (int alignmentIndex = 0; alignmentIndex < directionalFrames.Count; alignmentIndex++)
            {
                RegisteredCardFrame frame = directionalFrames[alignmentIndex];
                cancellationToken.ThrowIfCancellationRequested();
                Stopwatch alignmentTimer = Stopwatch.StartNew();
                progress?.Report(new SurfaceInspectionProgress
                {
                    Percent = 75 + alignmentIndex,
                    Stage = $"ALIGN {frame.Key.ToUpperInvariant()}",
                    Message = $"Fine-aligning {frame.Key.ToUpperInvariant()} to the flat REFERENCE image. Geometry is already locked; this is only a small X/Y correction.",
                    RegisteredImagePath = frame.RegisteredImagePath
                });

                Stopwatch searchTimer = Stopwatch.StartNew();
                SimilarityMatch match = FindBestTranslation(reference.Luminance, frame.Luminance);
                searchTimer.Stop();

                frame.AlignmentConfidence = match.Confidence * 100.0;
                frame.RotationDegrees = 0.0;
                frame.Scale = 1.0;
                frame.OffsetX = match.OffsetX;
                frame.OffsetY = match.OffsetY;

                progress?.Report(new SurfaceInspectionProgress
                {
                    Percent = 75 + alignmentIndex,
                    Stage = $"ALIGN {frame.Key.ToUpperInvariant()} — SHIFT FOUND",
                    Message = $"Residual shift is X={match.OffsetX}px, Y={match.OffsetY}px ({frame.AlignmentConfidence:0}% confidence). Applying that small translation now. Search took {searchTimer.Elapsed.TotalMilliseconds:0} ms.",
                    RegisteredImagePath = frame.RegisteredImagePath
                });

                Stopwatch arrayShiftTimer = Stopwatch.StartNew();
                frame.Luminance = ApplySimilarity(frame.Luminance, CanonicalWidth, CanonicalHeight, match);
                arrayShiftTimer.Stop();

                progress?.Report(new SurfaceInspectionProgress
                {
                    Percent = 75 + alignmentIndex,
                    Stage = $"ALIGN {frame.Key.ToUpperInvariant()} — APPLY SHIFT",
                    Message = "Updating the flattened display image with the same small X/Y correction using native OpenCV.",
                    RegisteredImagePath = frame.RegisteredImagePath
                });

                Stopwatch imageShiftTimer = Stopwatch.StartNew();
                await ApplySimilarityToImageAsync(frame.RegisteredImagePath, match, cancellationToken);
                imageShiftTimer.Stop();

                progress?.Report(new SurfaceInspectionProgress
                {
                    Percent = 76 + alignmentIndex,
                    Stage = $"ALIGN {frame.Key.ToUpperInvariant()} — COMPLETE",
                    Message = $"{frame.Key.ToUpperInvariant()} alignment complete. Search {searchTimer.Elapsed.TotalMilliseconds:0} ms • array shift {arrayShiftTimer.Elapsed.TotalMilliseconds:0} ms • image shift {imageShiftTimer.Elapsed.TotalMilliseconds:0} ms.",
                    RegisteredImagePath = frame.RegisteredImagePath
                });

                await InspectionDiagnosticLogger.WriteAsync(
                    "Surface/Registration",
                    $"ALIGN {frame.Key.ToUpperInvariant()} COMPLETE",
                    $"Shift=({match.OffsetX},{match.OffsetY}); Confidence={frame.AlignmentConfidence:0.0}%; " +
                    $"Search={searchTimer.Elapsed.TotalMilliseconds:0}ms; ArrayShift={arrayShiftTimer.Elapsed.TotalMilliseconds:0}ms; " +
                    $"ImageShift={imageShiftTimer.Elapsed.TotalMilliseconds:0}ms; Total={alignmentTimer.Elapsed.TotalMilliseconds:0}ms");
            }

            progress?.Report(new SurfaceInspectionProgress
            {
                Percent = 77,
                Stage = "FINE ALIGNMENT COMPLETE",
                Message = "Small X/Y camera jitter has been corrected. Building the five-image alignment diagnostics.",
                RegisteredImagePath = reference.RegisteredImagePath
            });

            foreach (RegisteredCardFrame frame in frames.Values)
            {
                frame.EdgeImagePath = Path.Combine(outputDirectory, $"edge_{SafeKey(frame.Key)}.png");
                float[] edges = GradientMagnitude(frame.Luminance, CanonicalWidth, CanonicalHeight);
                await SaveGrayscaleAsync(edges, frame.EdgeImagePath, cancellationToken, normalize: true);
            }

            string edgeOverlayPath = Path.Combine(outputDirectory, "registration_edge_overlay.png");
            string averagePath = Path.Combine(outputDirectory, "registration_average.png");
            await SaveColorOverlayAsync(directionalFrames, edgeOverlayPath, cancellationToken);
            List<float[]> averageFrames = new() { reference.Luminance };
            averageFrames.AddRange(directionalFrames.Select(frame => frame.Luminance));
            await SaveAverageAsync(averageFrames, averagePath, cancellationToken);

            progress?.Report(new SurfaceInspectionProgress
            {
                Percent = 79,
                Stage = "ALIGNMENT CHECK",
                Message = "Combined transformed images are ready. The photometric solver starts only after this checkpoint.",
                CombinedImagePath = averagePath
            });

            await InspectionDiagnosticLogger.WriteAsync(
                "Surface/Registration",
                "FIVE FRAME GEOMETRY COMPLETE",
                $"Elapsed={total.Elapsed.TotalSeconds:0.00}s; " +
                string.Join("; ", frames.Values.Select(f => $"{f.Key}={f.Geometry.Confidence * 100.0:0}%")));

            double alignmentQuality = directionalFrames.Average(frame => frame.AlignmentConfidence);
            double geometryQuality = frames.Values.Average(frame => frame.Geometry.Confidence * 100.0);

            return new CardRegistrationResult
            {
                Frames = frames,
                EdgeOverlayPath = edgeOverlayPath,
                AlignmentAveragePath = averagePath,
                OverallQuality = Math.Clamp((geometryQuality * 0.35) + (alignmentQuality * 0.65), 0.0, 100.0)
            };
        }

        private async Task<RegisteredCardFrame> ProcessFrameAsync(
            string key,
            string inputPath,
            string outputDirectory,
            IReadOnlyList<CardPoint>? manualCorners,
            IReadOnlyList<CardPoint>? normalizedReferenceCorners,
            int startPercent,
            IProgress<SurfaceInspectionProgress>? progress,
            CancellationToken cancellationToken)
        {
            string label = key.Equals("Reference", StringComparison.OrdinalIgnoreCase)
                ? "REFERENCE"
                : key.ToUpperInvariant();
            string safeKey = SafeKey(key);
            Stopwatch timer = Stopwatch.StartNew();

            progress?.Report(new SurfaceInspectionProgress
            {
                Percent = startPercent,
                Stage = $"{label} — LOAD",
                Message = $"Opening the {label} capture as a bounded native image…",
                FrameKey = key,
                SourceImagePath = inputPath
            });

            string previewPath = Path.Combine(outputDirectory, $"preview_{safeKey}.jpg");
            using Mat preview = LoadBoundedOpenCvPreview(inputPath, previewPath, cancellationToken);

            progress?.Report(new SurfaceInspectionProgress
            {
                Percent = startPercent + 2,
                Stage = $"{label} — PREVIEW READY",
                Message = $"{label} preview ready ({preview.Width}×{preview.Height}). Creating the physical-edge map.",
                FrameKey = key,
                SourceImagePath = previewPath
            });

            string edgePath = Path.Combine(outputDirectory, $"find_edges_{safeKey}.jpg");
            using Mat edgeMap = BuildEdgeMap(preview);
            Cv2.ImWrite(edgePath, edgeMap, new[] { (int)ImwriteFlags.JpegQuality, 90 });

            progress?.Report(new SurfaceInspectionProgress
            {
                Percent = startPercent + 4,
                Stage = $"{label} — EDGE MAP",
                Message = "Physical-edge map created. Finding the outer trading-card rectangle now.",
                FrameKey = key,
                SourceImagePath = previewPath
            });

            CardPoint[] corners;
            double confidence;
            string findMessage;

            if (manualCorners is not null)
            {
                corners = manualCorners.Select(p => new CardPoint(
                    p.X * Math.Max(preview.Width - 1, 1),
                    p.Y * Math.Max(preview.Height - 1, 1))).ToArray();
                confidence = 1.0;
                findMessage = key.Equals("Reference", StringComparison.OrdinalIgnoreCase)
                    ? "Using the four reference corners you confirmed manually."
                    : $"Using the four {label} corners you reviewed/confirmed manually.";
            }
            else
            {
                progress?.Report(new SurfaceInspectionProgress
                {
                    Percent = startPercent + 5,
                    Stage = $"{label} — FIND FOUR CORNERS",
                    Message = "IN RECTANGLE FIND — scanning Canny contours for the physical outside card boundary.",
                    FrameKey = key,
                    SourceImagePath = previewPath
                });

                bool found = TryFindCardRectangle(preview, edgeMap, out corners, out confidence, out int contours, out int quads, out int accepted);

                // Directional images get one safe recovery path. If glare prevents an
                // independent lock but the phone/card stayed fixed, inherit the neutral
                // reference geometry rather than failing the entire five-image scan.
                if (!found && normalizedReferenceCorners is not null)
                {
                    corners = normalizedReferenceCorners.Select(p => new CardPoint(
                        p.X * Math.Max(preview.Width - 1, 1),
                        p.Y * Math.Max(preview.Height - 1, 1))).ToArray();
                    confidence = 0.72;
                    findMessage = $"No independent {label} rectangle survived validation (contours={contours}, quads={quads}, accepted={accepted}). Using the locked REFERENCE geometry because the phone/card remained fixed.";
                }
                else if (!found)
                {
                    string message = $"REFERENCE CARD NOT FOUND — contours={contours}, 4-point candidates={quads}, accepted={accepted}. Set the four reference corners manually.";
                    await InspectionDiagnosticLogger.WriteAsync("Surface/Registration", "REFERENCE FOUR CORNERS FAILED", message);
                    progress?.Report(new SurfaceInspectionProgress
                    {
                        Percent = startPercent + 6,
                        Stage = "REFERENCE — CARD NOT FOUND",
                        Message = message,
                        FrameKey = key,
                        SourceImagePath = previewPath
                    });
                    throw new SurfaceReferenceCardNotFoundException(message, previewPath);
                }
                else
                {
                    findMessage = $"CARD FOUND — four physical corners accepted. contours={contours}, quads={quads}, accepted={accepted}, confidence={confidence * 100.0:0}%";
                }
            }

            // If an independent light-image lock jumped far away from the neutral
            // geometry, reject it and inherit the reference geometry. Moving glare
            // must not redefine the physical card.
            if (manualCorners is null &&
                !key.Equals("Reference", StringComparison.OrdinalIgnoreCase) &&
                normalizedReferenceCorners is not null && confidence > 0.72)
            {
                CardPoint[] detectedNormalized = NormalizeCorners(corners, preview.Width, preview.Height);
                double drift = AverageCornerDistance(detectedNormalized, normalizedReferenceCorners);
                if (drift > 0.085)
                {
                    corners = normalizedReferenceCorners.Select(p => new CardPoint(
                        p.X * Math.Max(preview.Width - 1, 1),
                        p.Y * Math.Max(preview.Height - 1, 1))).ToArray();
                    confidence = 0.76;
                    findMessage = $"{label} detector jumped away from the neutral card geometry (drift={drift:0.000}). REFERENCE geometry was used instead.";
                }
            }

            string overlayPath = Path.Combine(outputDirectory, $"corners_{safeKey}.jpg");
            SaveOpenCvCornerOverlay(preview, corners, overlayPath, label);

            progress?.Report(new SurfaceInspectionProgress
            {
                Percent = startPercent + 7,
                Stage = $"{label} — FOUR CORNERS FOUND",
                Message = findMessage,
                FrameKey = key,
                SourceImagePath = previewPath,
                OverlayImagePath = overlayPath
            });

            cancellationToken.ThrowIfCancellationRequested();
            string registeredPath = Path.Combine(outputDirectory, $"registered_{safeKey}.jpg");
            using Mat flat = WarpPerspectiveToCanonical(preview, corners);
            Cv2.ImWrite(registeredPath, flat, new[] { (int)ImwriteFlags.JpegQuality, 94 });
            float[] luminance = ExtractLuminance(flat);

            progress?.Report(new SurfaceInspectionProgress
            {
                Percent = startPercent + 10,
                Stage = $"{label} — FLAT / NORMALIZED",
                Message = $"{label} perspective transform complete. The detected four-corner card is now in the common 750×1050 matrix.",
                FrameKey = key,
                SourceImagePath = previewPath,
                OverlayImagePath = overlayPath,
                RegisteredImagePath = registeredPath
            });

            await InspectionDiagnosticLogger.WriteAsync(
                "Surface/Registration",
                $"{label} GEOMETRY COMPLETE",
                $"Confidence={confidence:0.000}; Elapsed={timer.Elapsed.TotalSeconds:0.00}s; " +
                $"Corners={string.Join("; ", corners.Select(p => $"({p.X:0},{p.Y:0})"))}");

            return new RegisteredCardFrame
            {
                Key = key,
                SourcePath = inputPath,
                PreviewImagePath = previewPath,
                DetectionOverlayPath = overlayPath,
                RegisteredImagePath = registeredPath,
                EdgeImagePath = edgePath,
                Geometry = new CardGeometryResult
                {
                    Success = true,
                    Corners = corners,
                    SourceWidth = preview.Width,
                    SourceHeight = preview.Height,
                    Confidence = confidence
                },
                Luminance = luminance,
                AlignmentConfidence = key.Equals("Reference", StringComparison.OrdinalIgnoreCase) ? 100.0 : 0.0
            };
        }

        private static IReadOnlyList<CardPoint>? GetManualCornersForFrame(
            string key,
            IReadOnlyList<CardPoint>? manualReferenceCorners,
            IReadOnlyDictionary<string, IReadOnlyList<CardPoint>>? manualFrameCorners)
        {
            if (manualFrameCorners is not null && manualFrameCorners.TryGetValue(key, out IReadOnlyList<CardPoint>? frameCorners))
                return frameCorners;

            if (key.Equals("Reference", StringComparison.OrdinalIgnoreCase))
                return manualReferenceCorners;

            return null;
        }

        private static Mat LoadBoundedOpenCvPreview(
            string inputPath,
            string outputPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using Mat decoded = Cv2.ImRead(inputPath, ImreadModes.ReducedColor2);
            if (decoded.Empty())
                throw new InvalidOperationException("Surface could not open the captured image.");

            cancellationToken.ThrowIfCancellationRequested();
            int maxDimension = Math.Max(decoded.Width, decoded.Height);
            Mat bounded = new();
            if (maxDimension > RegistrationPreviewMaximumDimension)
            {
                double scale = RegistrationPreviewMaximumDimension / (double)maxDimension;
                int width = Math.Max(1, (int)Math.Round(decoded.Width * scale));
                int height = Math.Max(1, (int)Math.Round(decoded.Height * scale));
                Cv2.Resize(decoded, bounded, new OpenCvSharp.Size(width, height), 0, 0, InterpolationFlags.Area);
            }
            else
            {
                decoded.CopyTo(bounded);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            if (!Cv2.ImWrite(outputPath, bounded, new[] { (int)ImwriteFlags.JpegQuality, 90 }))
            {
                bounded.Dispose();
                throw new IOException("Surface could not save the bounded card preview.");
            }
            return bounded;
        }

        private static Mat BuildEdgeMap(Mat bgr)
        {
            using Mat gray = new();
            using Mat claheImage = new();
            using Mat blurred = new();
            Mat edges = new();
            Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
            using CLAHE clahe = Cv2.CreateCLAHE(2.0, new OpenCvSharp.Size(8, 8));
            clahe.Apply(gray, claheImage);
            Cv2.GaussianBlur(claheImage, blurred, new OpenCvSharp.Size(5, 5), 0);
            Cv2.Canny(blurred, edges, 45, 150);
            using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
            Cv2.MorphologyEx(edges, edges, MorphTypes.Close, kernel, iterations: 1);
            return edges;
        }

        private static bool TryFindCardRectangle(
            Mat bgr,
            Mat primaryEdges,
            out CardPoint[] corners,
            out double confidence,
            out int contourCount,
            out int quadCount,
            out int acceptedCount)
        {
            corners = Array.Empty<CardPoint>();
            confidence = 0.0;
            contourCount = 0;
            quadCount = 0;
            acceptedCount = 0;
            double bestScore = double.NegativeInfinity;

            List<Mat> edgePasses = new() { primaryEdges.Clone() };
            try
            {
                using Mat gray = new();
                using Mat blurred = new();
                Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.GaussianBlur(gray, blurred, new OpenCvSharp.Size(5, 5), 0);
                foreach ((double low, double high) in new[] { (28.0, 95.0), (70.0, 210.0) })
                {
                    Mat pass = new();
                    Cv2.Canny(blurred, pass, low, high);
                    using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
                    Cv2.MorphologyEx(pass, pass, MorphTypes.Close, kernel, iterations: 1);
                    edgePasses.Add(pass);
                }

                foreach (Mat edges in edgePasses)
                {
                    Cv2.FindContours(edges, out OpenCvSharp.Point[][] contoursFound, out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);
                    contourCount += contoursFound.Length;

                    foreach (OpenCvSharp.Point[] contour in contoursFound)
                    {
                        double perimeter = Cv2.ArcLength(contour, true);
                        if (perimeter < Math.Min(bgr.Width, bgr.Height) * 0.35)
                            continue;

                        foreach (double epsilon in new[] { 0.012, 0.018, 0.026, 0.035 })
                        {
                            OpenCvSharp.Point[] quad = Cv2.ApproxPolyDP(contour, perimeter * epsilon, true);
                            if (quad.Length != 4 || !Cv2.IsContourConvex(quad))
                                continue;
                            quadCount++;

                            double area = Math.Abs(Cv2.ContourArea(quad));
                            double areaFraction = area / Math.Max(1.0, bgr.Width * (double)bgr.Height);
                            if (areaFraction < 0.035 || areaFraction > 0.82)
                                continue;

                            CardPoint[] ordered = OrderCorners(quad.Select(p => new CardPoint(p.X, p.Y)).ToArray());
                            if (!ValidateCardQuad(ordered, bgr.Width, bgr.Height, areaFraction, out double score))
                                continue;

                            acceptedCount++;
                            if (score <= bestScore)
                                continue;

                            bestScore = score;
                            corners = ordered;
                        }
                    }
                }
            }
            finally
            {
                foreach (Mat pass in edgePasses)
                    pass.Dispose();
            }

            if (corners.Length != 4)
                return false;

            confidence = Math.Clamp(0.58 + (Math.Max(bestScore, 0.0) * 0.40), 0.58, 0.98);
            return true;
        }

        private static CardPoint[] OrderCorners(IReadOnlyList<CardPoint> points)
        {
            if (points.Count != 4)
                throw new ArgumentException("Four points are required.", nameof(points));

            CardPoint tl = points.OrderBy(p => p.X + p.Y).First();
            CardPoint br = points.OrderByDescending(p => p.X + p.Y).First();
            CardPoint tr = points.OrderByDescending(p => p.X - p.Y).First();
            CardPoint bl = points.OrderBy(p => p.X - p.Y).First();
            return new[] { tl, tr, br, bl };
        }

        private static bool ValidateCardQuad(
            IReadOnlyList<CardPoint> p,
            int width,
            int height,
            double areaFraction,
            out double score)
        {
            score = 0.0;
            double top = Distance(p[0], p[1]);
            double right = Distance(p[1], p[2]);
            double bottom = Distance(p[2], p[3]);
            double left = Distance(p[3], p[0]);
            double avgW = (top + bottom) * 0.5;
            double avgH = (left + right) * 0.5;
            if (avgW < 40 || avgH < 60)
                return false;

            double shortLongRatio = Math.Min(avgW, avgH) / Math.Max(avgW, avgH);
            double ratioError = Math.Abs(shortLongRatio - ExpectedPortraitRatio);
            if (ratioError > 0.22)
                return false;

            double parallelW = Math.Min(top, bottom) / Math.Max(top, bottom);
            double parallelH = Math.Min(left, right) / Math.Max(left, right);
            if (parallelW < 0.58 || parallelH < 0.58)
                return false;

            // Avoid a tiny internal printed rectangle. Physical cards generally
            // occupy a meaningful fraction of the capture in this guided workflow.
            double sizeScore = Math.Clamp((areaFraction - 0.035) / 0.30, 0.0, 1.0);
            double ratioScore = Math.Clamp(1.0 - ratioError / 0.22, 0.0, 1.0);
            double parallelScore = (parallelW + parallelH) * 0.5;

            // Favor candidates away from exact frame boundaries but do not reject
            // close captures where the card approaches the image edge.
            double margin = Math.Min(
                Math.Min(Math.Min(p[0].X, p[3].X), width - 1 - Math.Max(p[1].X, p[2].X)),
                Math.Min(Math.Min(p[0].Y, p[1].Y), height - 1 - Math.Max(p[2].Y, p[3].Y)));
            double marginScore = Math.Clamp(margin / Math.Max(12.0, Math.Min(width, height) * 0.05), 0.0, 1.0);

            score = (sizeScore * 0.45) + (ratioScore * 0.30) + (parallelScore * 0.20) + (marginScore * 0.05);
            return true;
        }

        private static double Distance(CardPoint a, CardPoint b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static CardPoint[] NormalizeCorners(IReadOnlyList<CardPoint> corners, int width, int height)
        {
            return corners.Select(p => new CardPoint(
                p.X / Math.Max(width - 1.0f, 1.0f),
                p.Y / Math.Max(height - 1.0f, 1.0f))).ToArray();
        }

        private static double AverageCornerDistance(IReadOnlyList<CardPoint> a, IReadOnlyList<CardPoint> b)
        {
            if (a.Count != 4 || b.Count != 4)
                return 1.0;
            double sum = 0.0;
            for (int i = 0; i < 4; i++)
                sum += Distance(a[i], b[i]);
            return sum / 4.0;
        }

        private static void SaveOpenCvCornerOverlay(
            Mat source,
            IReadOnlyList<CardPoint> corners,
            string outputPath,
            string label)
        {
            using Mat overlay = source.Clone();
            Scalar green = new(20, 255, 57);
            Scalar yellow = new(30, 220, 255);
            for (int i = 0; i < 4; i++)
            {
                var a = new OpenCvSharp.Point((int)Math.Round(corners[i].X), (int)Math.Round(corners[i].Y));
                var b = new OpenCvSharp.Point((int)Math.Round(corners[(i + 1) % 4].X), (int)Math.Round(corners[(i + 1) % 4].Y));
                Cv2.Line(overlay, a, b, green, 4, LineTypes.AntiAlias);
                Cv2.Circle(overlay, a, 11, yellow, -1, LineTypes.AntiAlias);
                Cv2.Circle(overlay, a, 13, green, 2, LineTypes.AntiAlias);
                Cv2.PutText(overlay, new[] { "TL", "TR", "BR", "BL" }[i], a + new OpenCvSharp.Point(12, -12), HersheyFonts.HersheySimplex, 0.72, green, 2, LineTypes.AntiAlias);
            }
            Cv2.PutText(overlay, $"{label}: FOUR CORNERS FOUND", new OpenCvSharp.Point(18, 34), HersheyFonts.HersheySimplex, 0.85, green, 2, LineTypes.AntiAlias);
            Cv2.ImWrite(outputPath, overlay, new[] { (int)ImwriteFlags.JpegQuality, 94 });
        }

        private static CardPoint[] OrderCornersForPortraitWarp(IReadOnlyList<CardPoint> corners)
        {
            if (corners.Count != 4)
                throw new ArgumentException("Four card corners are required.", nameof(corners));

            double topBottom = (Distance(corners[0], corners[1]) + Distance(corners[2], corners[3])) * 0.5;
            double leftRight = (Distance(corners[1], corners[2]) + Distance(corners[3], corners[0])) * 0.5;

            if (topBottom <= leftRight)
                return corners.Select(p => new CardPoint(p.X, p.Y)).ToArray();

            return new[]
            {
                new CardPoint(corners[3].X, corners[3].Y),
                new CardPoint(corners[0].X, corners[0].Y),
                new CardPoint(corners[1].X, corners[1].Y),
                new CardPoint(corners[2].X, corners[2].Y)
            };
        }

        private static Mat WarpPerspectiveToCanonical(Mat source, IReadOnlyList<CardPoint> sourceCorners)
        {
            int padX = Math.Max(4, (int)Math.Round(CanonicalWidth * 0.03));
            int padY = Math.Max(4, (int)Math.Round(CanonicalHeight * 0.03));
            CardPoint[] portraitCorners = OrderCornersForPortraitWarp(sourceCorners);
            Point2f[] src = portraitCorners.Select(p => new Point2f(p.X, p.Y)).ToArray();
            Point2f[] dst =
            {
                new(padX, padY),
                new(CanonicalWidth - 1 - padX, padY),
                new(CanonicalWidth - 1 - padX, CanonicalHeight - 1 - padY),
                new(padX, CanonicalHeight - 1 - padY)
            };
            using Mat transform = Cv2.GetPerspectiveTransform(src, dst);
            Mat output = new(CanonicalHeight, CanonicalWidth, source.Type(), Scalar.Black);
            Cv2.WarpPerspective(source, output, transform, new OpenCvSharp.Size(CanonicalWidth, CanonicalHeight), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.Black);
            return output;
        }

        private static float[] ExtractLuminance(Mat bgr)
        {
            using Mat gray = new();
            Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
            Mat readable = gray.IsContinuous() ? gray : gray.Clone();
            try
            {
                byte[] bytes = new byte[readable.Width * readable.Height];
                Marshal.Copy(readable.Data, bytes, 0, bytes.Length);
                float[] values = new float[bytes.Length];
                for (int i = 0; i < bytes.Length; i++)
                    values[i] = bytes[i] / 255.0f;
                return values;
            }
            finally
            {
                if (!ReferenceEquals(readable, gray))
                    readable.Dispose();
            }
        }

        private static SimilarityMatch FindBestTranslation(float[] reference, float[] moving)
        {
            // Geometry is already perspective-normalized from four card corners.
            // Only a tiny integer X/Y correction is permitted here.  The older
            // implementation still performed bilinear transformed correlation for
            // every trial shift, which was unnecessarily expensive on Android.
            const int factor = 4;
            const int maximumFullResolutionShift = 12;
            int width = CanonicalWidth / factor;
            int height = CanonicalHeight / factor;

            float[] referenceSmall = Downsample(reference, CanonicalWidth, CanonicalHeight, factor);
            float[] movingSmall = Downsample(moving, CanonicalWidth, CanonicalHeight, factor);
            float[] referenceEdges = GradientMagnitude(referenceSmall, width, height);
            float[] movingEdges = GradientMagnitude(movingSmall, width, height);

            int maxShift = Math.Max(1, maximumFullResolutionShift / factor);
            SimilarityMatch best = new()
            {
                Score = double.NegativeInfinity,
                Scale = 1.0,
                AngleDegrees = 0.0
            };

            for (int dy = -maxShift; dy <= maxShift; dy++)
            {
                for (int dx = -maxShift; dx <= maxShift; dx++)
                {
                    double score = CorrelateTranslationFast(
                        referenceEdges, movingEdges, width, height, dx, dy);
                    double penalty = (Math.Abs(dx) + Math.Abs(dy)) * 0.0007;
                    double penalizedScore = score - penalty;
                    if (penalizedScore > best.Score)
                    {
                        best = new SimilarityMatch
                        {
                            Score = penalizedScore,
                            AngleDegrees = 0.0,
                            Scale = 1.0,
                            OffsetX = dx * factor,
                            OffsetY = dy * factor
                        };
                    }
                }
            }

            best.Confidence = Math.Clamp((best.Score + 1.0) * 0.5, 0.0, 1.0);
            return best;
        }

        private static double CorrelateTranslationFast(
            float[] reference,
            float[] moving,
            int width,
            int height,
            int dx,
            int dy)
        {
            // Sample a 3x3 interior grid.  Direct integer indexing is enough because
            // fine alignment is translation-only after the perspective transform.
            List<double> regionScores = new(9);
            int marginX = Math.Max(4, width / 20);
            int marginY = Math.Max(4, height / 20);
            int usableW = width - (marginX * 2);
            int usableH = height - (marginY * 2);

            for (int ry = 0; ry < 3; ry++)
            {
                for (int rx = 0; rx < 3; rx++)
                {
                    int x0 = marginX + (usableW * rx / 3);
                    int x1 = marginX + (usableW * (rx + 1) / 3);
                    int y0 = marginY + (usableH * ry / 3);
                    int y1 = marginY + (usableH * (ry + 1) / 3);
                    double sumR = 0, sumM = 0, sumRR = 0, sumMM = 0, sumRM = 0;
                    int count = 0;

                    for (int y = y0; y < y1; y += 3)
                    {
                        int my = y - dy;
                        if (my < 0 || my >= height) continue;
                        for (int x = x0; x < x1; x += 3)
                        {
                            int mx = x - dx;
                            if (mx < 0 || mx >= width) continue;
                            double r = reference[(y * width) + x];
                            double m = moving[(my * width) + mx];
                            sumR += r; sumM += m; sumRR += r * r; sumMM += m * m; sumRM += r * m; count++;
                        }
                    }

                    if (count < 12)
                    {
                        regionScores.Add(-1.0);
                        continue;
                    }

                    double numerator = sumRM - ((sumR * sumM) / count);
                    double denomR = sumRR - ((sumR * sumR) / count);
                    double denomM = sumMM - ((sumM * sumM) / count);
                    double denominator = Math.Sqrt(Math.Max(denomR * denomM, 1e-12));
                    regionScores.Add(numerator / denominator);
                }
            }

            regionScores.Sort();
            return regionScores.Skip(2).Average();
        }

        // Retained for reference/debugging, but External Light no longer calls this
        // unconstrained rotate/scale search.
        private static SimilarityMatch FindBestSimilarity(float[] reference, float[] moving)
        {
            const int factor = 6;
            int width = CanonicalWidth / factor;
            int height = CanonicalHeight / factor;
            float[] referenceSmall = Downsample(reference, CanonicalWidth, CanonicalHeight, factor);
            float[] movingSmall = Downsample(moving, CanonicalWidth, CanonicalHeight, factor);
            float[] referenceEdges = GradientMagnitude(referenceSmall, width, height);
            float[] movingEdges = GradientMagnitude(movingSmall, width, height);

            SimilarityMatch best = new() { Score = double.NegativeInfinity, Scale = 1.0 };
            double[] angles = { -3, -2, -1, 0, 1, 2, 3 };
            double[] scales = { 0.96, 0.98, 1.00, 1.02, 1.04 };

            foreach (double angle in angles)
            {
                foreach (double scale in scales)
                {
                    for (int dy = -5; dy <= 5; dy++)
                    {
                        for (int dx = -5; dx <= 5; dx++)
                        {
                            double score = CorrelateTransformed(referenceEdges, movingEdges, width, height, angle, scale, dx, dy);
                            if (score > best.Score)
                            {
                                best = new SimilarityMatch { Score = score, AngleDegrees = angle, Scale = scale, OffsetX = dx * factor, OffsetY = dy * factor };
                            }
                        }
                    }
                }
            }

            // Fine search around the coarse winner.
            SimilarityMatch refined = best;
            for (double angle = best.AngleDegrees - 0.75; angle <= best.AngleDegrees + 0.75; angle += 0.5)
            {
                for (double scale = best.Scale - 0.012; scale <= best.Scale + 0.012; scale += 0.008)
                {
                    int coarseDx = (int)Math.Round(best.OffsetX / (double)factor);
                    int coarseDy = (int)Math.Round(best.OffsetY / (double)factor);
                    for (int dy = coarseDy - 2; dy <= coarseDy + 2; dy++)
                    {
                        for (int dx = coarseDx - 2; dx <= coarseDx + 2; dx++)
                        {
                            double score = CorrelateTransformed(referenceEdges, movingEdges, width, height, angle, scale, dx, dy);
                            if (score > refined.Score)
                            {
                                refined = new SimilarityMatch { Score = score, AngleDegrees = angle, Scale = scale, OffsetX = dx * factor, OffsetY = dy * factor };
                            }
                        }
                    }
                }
            }

            refined.Confidence = Math.Clamp((refined.Score + 1.0) * 0.5, 0.0, 1.0);
            return refined;
        }

        private static double CorrelateTransformed(
            float[] reference,
            float[] moving,
            int width,
            int height,
            double angleDegrees,
            double scale,
            int dx,
            int dy)
        {
            // Moving directional illumination changes the global brightness pattern
            // dramatically.  A single whole-card correlation can therefore align to
            // glare instead of geometry.  Score nine independent interior regions and
            // average the best-consistent seven.  Printed/card-border edges must agree
            // across the card for a transform to score well.
            List<double> regionScores = new(9);
            double angle = angleDegrees * Math.PI / 180.0;
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            double cx = (width - 1) * 0.5;
            double cy = (height - 1) * 0.5;
            int marginX = Math.Max(5, width / 18);
            int marginY = Math.Max(5, height / 18);
            int usableW = width - (marginX * 2);
            int usableH = height - (marginY * 2);

            for (int ry = 0; ry < 3; ry++)
            {
                for (int rx = 0; rx < 3; rx++)
                {
                    int x0 = marginX + (usableW * rx / 3);
                    int x1 = marginX + (usableW * (rx + 1) / 3);
                    int y0 = marginY + (usableH * ry / 3);
                    int y1 = marginY + (usableH * (ry + 1) / 3);

                    double sumR = 0, sumM = 0, sumRR = 0, sumMM = 0, sumRM = 0;
                    int count = 0;
                    for (int y = y0; y < y1; y += 2)
                    {
                        for (int x = x0; x < x1; x += 2)
                        {
                            double outputX = x - dx - cx;
                            double outputY = y - dy - cy;
                            double mx = ((cos * outputX) + (sin * outputY)) / scale + cx;
                            double my = ((-sin * outputX) + (cos * outputY)) / scale + cy;
                            if (mx < 1 || my < 1 || mx >= width - 2 || my >= height - 2)
                                continue;

                            double r = reference[(y * width) + x];
                            double m = SampleBilinear(moving, width, height, (float)mx, (float)my);
                            sumR += r; sumM += m; sumRR += r * r; sumMM += m * m; sumRM += r * m; count++;
                        }
                    }

                    if (count < 20)
                    {
                        regionScores.Add(-1.0);
                        continue;
                    }

                    double numerator = sumRM - ((sumR * sumM) / count);
                    double denomR = sumRR - ((sumR * sumR) / count);
                    double denomM = sumMM - ((sumM * sumM) / count);
                    double denominator = Math.Sqrt(Math.Max(denomR * denomM, 1e-12));
                    regionScores.Add(numerator / denominator);
                }
            }

            regionScores.Sort();
            // Ignore the two worst regions because a moving specular highlight can
            // legitimately destroy edge contrast in a localized area.
            return regionScores.Skip(2).Average();
        }

        private static float[] ApplySimilarity(float[] source, int width, int height, SimilarityMatch match)
        {
            float[] output = new float[source.Length];
            int dx = match.OffsetX;
            int dy = match.OffsetY;
            for (int y = 0; y < height; y++)
            {
                int sy = y - dy;
                if (sy < 0 || sy >= height) continue;
                for (int x = 0; x < width; x++)
                {
                    int sx = x - dx;
                    if (sx < 0 || sx >= width) continue;
                    output[(y * width) + x] = source[(sy * width) + sx];
                }
            }
            return output;
        }

        private static Task ApplySimilarityToImageAsync(string path, SimilarityMatch match, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The card is already perspective-normalized to 750 x 1050. Fine alignment
            // is translation-only, so do not round-trip through ImageSharp and managed
            // pixel arrays here. OpenCV performs this native affine shift in one pass.
            if (match.OffsetX == 0 && match.OffsetY == 0)
            {
                return Task.CompletedTask;
            }

            using Mat source = Cv2.ImRead(path, ImreadModes.Color);
            if (source.Empty())
            {
                throw new InvalidOperationException($"Could not reopen normalized Surface image for fine alignment: {path}");
            }

            using Mat transform = Mat.Eye(2, 3, MatType.CV_64FC1);
            transform.Set(0, 2, (double)match.OffsetX);
            transform.Set(1, 2, (double)match.OffsetY);

            using Mat shifted = new();
            Cv2.WarpAffine(
                source,
                shifted,
                transform,
                new OpenCvSharp.Size(CanonicalWidth, CanonicalHeight),
                InterpolationFlags.Linear,
                BorderTypes.Constant,
                Scalar.Black);

            cancellationToken.ThrowIfCancellationRequested();

            string temporaryPath = path + ".align_tmp.png";
            Cv2.ImWrite(temporaryPath, shifted);
            File.Move(temporaryPath, path, true);

            return Task.CompletedTask;
        }

        private static async Task SaveColorOverlayAsync(IReadOnlyList<RegisteredCardFrame> frames, string path, CancellationToken token)
        {
            if (frames.Count == 0) return;
            List<float[]> edges = frames.Select(frame => GradientMagnitude(frame.Luminance, CanonicalWidth, CanonicalHeight)).ToList();
            float[] maxima = edges.Select(values => Math.Max(values.Max(), Epsilon)).ToArray();
            using SixLabors.ImageSharp.Image<Rgba32> image = new(CanonicalWidth, CanonicalHeight);

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < CanonicalHeight; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < CanonicalWidth; x++)
                    {
                        int i = y * CanonicalWidth + x;
                        float a = edges.Count > 0 ? edges[0][i] / maxima[0] : 0;
                        float b = edges.Count > 1 ? edges[1][i] / maxima[1] : 0;
                        float c = edges.Count > 2 ? edges[2][i] / maxima[2] : 0;
                        float d = edges.Count > 3 ? edges[3][i] / maxima[3] : 0;
                        row[x] = new Rgba32(
                            (byte)Math.Clamp((a + d) * 255, 0, 255),
                            (byte)Math.Clamp((b + d) * 255, 0, 255),
                            (byte)Math.Clamp(c * 255, 0, 255), 255);
                    }
                }
            });
            await SaveImageAsync(image, path, token);
        }

        private static async Task SaveAverageAsync(IReadOnlyList<float[]> images, string path, CancellationToken token)
        {
            float[] average = new float[CanonicalWidth * CanonicalHeight];
            foreach (float[] values in images)
                for (int i = 0; i < average.Length; i++) average[i] += values[i];
            float divisor = Math.Max(images.Count, 1);
            for (int i = 0; i < average.Length; i++) average[i] /= divisor;
            await SaveGrayscaleAsync(average, path, token, false);
        }

        private static async Task SaveDetectionOverlayAsync(SixLabors.ImageSharp.Image<Rgba32> source, IReadOnlyList<CardPoint> corners, string path, CancellationToken token)
        {
            using SixLabors.ImageSharp.Image<Rgba32> overlay = source.Clone();
            Rgba32 green = new(57, 255, 20, 255); Rgba32 red = new(255, 64, 64, 255);
            for (int i = 0; i < corners.Count; i++)
            {
                DrawLine(overlay, corners[i], corners[(i + 1) % corners.Count], green, 3);
                DrawPoint(overlay, corners[i], red, 7);
            }
            await SaveImageAsync(overlay, path, token);
        }

        private static void DrawLine(SixLabors.ImageSharp.Image<Rgba32> image, CardPoint start, CardPoint end, Rgba32 color, int thickness)
        {
            int x0 = (int)Math.Round(start.X), y0 = (int)Math.Round(start.Y), x1 = (int)Math.Round(end.X), y1 = (int)Math.Round(end.Y);
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx - dy;
            while (true)
            {
                DrawPoint(image, new CardPoint(x0, y0), color, thickness);
                if (x0 == x1 && y0 == y1) break;
                int e2 = err * 2;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }

        private static void DrawPoint(SixLabors.ImageSharp.Image<Rgba32> image, CardPoint point, Rgba32 color, int radius)
        {
            int cx = (int)Math.Round(point.X), cy = (int)Math.Round(point.Y);
            for (int y = cy - radius; y <= cy + radius; y++)
                for (int x = cx - radius; x <= cx + radius; x++)
                    if ((uint)x < (uint)image.Width && (uint)y < (uint)image.Height && ((x - cx) * (x - cx)) + ((y - cy) * (y - cy)) <= radius * radius)
                        image[x, y] = color;
        }

        private static double[] SolveHomography(IReadOnlyList<CardPoint> source, IReadOnlyList<CardPoint> destination)
        {
            double[,] matrix = new double[8, 9];
            for (int i = 0; i < 4; i++)
            {
                double x = source[i].X, y = source[i].Y, u = destination[i].X, v = destination[i].Y; int row = i * 2;
                matrix[row, 0] = x; matrix[row, 1] = y; matrix[row, 2] = 1; matrix[row, 6] = -u * x; matrix[row, 7] = -u * y; matrix[row, 8] = u;
                matrix[row + 1, 3] = x; matrix[row + 1, 4] = y; matrix[row + 1, 5] = 1; matrix[row + 1, 6] = -v * x; matrix[row + 1, 7] = -v * y; matrix[row + 1, 8] = v;
            }
            for (int pivot = 0; pivot < 8; pivot++)
            {
                int best = pivot; double value = Math.Abs(matrix[pivot, pivot]);
                for (int row = pivot + 1; row < 8; row++) if (Math.Abs(matrix[row, pivot]) > value) { value = Math.Abs(matrix[row, pivot]); best = row; }
                if (value < 1e-10) throw new InvalidOperationException("The detected card corners do not define a valid perspective transform.");
                if (best != pivot) for (int col = pivot; col < 9; col++) (matrix[pivot, col], matrix[best, col]) = (matrix[best, col], matrix[pivot, col]);
                double p = matrix[pivot, pivot]; for (int col = pivot; col < 9; col++) matrix[pivot, col] /= p;
                for (int row = 0; row < 8; row++) if (row != pivot) { double f = matrix[row, pivot]; for (int col = pivot; col < 9; col++) matrix[row, col] -= f * matrix[pivot, col]; }
            }
            return Enumerable.Range(0, 8).Select(row => matrix[row, 8]).ToArray();
        }

        private static void MapProjective(double[] h, int x, int y, out float sx, out float sy)
        {
            double d = (h[6] * x) + (h[7] * y) + 1.0;
            sx = (float)(((h[0] * x) + (h[1] * y) + h[2]) / d);
            sy = (float)(((h[3] * x) + (h[4] * y) + h[5]) / d);
        }

        private static Rgba32[] CopyPixels(SixLabors.ImageSharp.Image<Rgba32> source)
        {
            Rgba32[] pixels = new Rgba32[source.Width * source.Height];
            source.ProcessPixelRows(a => { for (int y = 0; y < source.Height; y++) a.GetRowSpan(y).CopyTo(pixels.AsSpan(y * source.Width, source.Width)); });
            return pixels;
        }

        private static float[] ExtractLuminance(SixLabors.ImageSharp.Image<Rgba32> image)
        {
            float[] values = new float[image.Width * image.Height];
            image.ProcessPixelRows(a => { for (int y = 0; y < image.Height; y++) { Span<Rgba32> row = a.GetRowSpan(y); for (int x = 0; x < image.Width; x++) { Rgba32 p = row[x]; values[y * image.Width + x] = ((.2126f * p.R) + (.7152f * p.G) + (.0722f * p.B)) / 255f; } } });
            return values;
        }

        private static float[] Downsample(float[] source, int width, int height, int factor)
        {
            int outW = width / factor, outH = height / factor; float[] output = new float[outW * outH];
            for (int y = 0; y < outH; y++) for (int x = 0; x < outW; x++) output[y * outW + x] = source[Math.Min(y * factor, height - 1) * width + Math.Min(x * factor, width - 1)];
            return output;
        }

        private static float[] GradientMagnitude(float[] source, int width, int height)
        {
            float[] result = new float[source.Length];
            for (int y = 1; y < height - 1; y++) for (int x = 1; x < width - 1; x++)
            {
                int t = (y - 1) * width, m = y * width, b = (y + 1) * width;
                float gx = -source[t + x - 1] + source[t + x + 1] - 2 * source[m + x - 1] + 2 * source[m + x + 1] - source[b + x - 1] + source[b + x + 1];
                float gy = -source[t + x - 1] - 2 * source[t + x] - source[t + x + 1] + source[b + x - 1] + 2 * source[b + x] + source[b + x + 1];
                result[m + x] = MathF.Sqrt(gx * gx + gy * gy);
            }
            return result;
        }

        private static float SampleBilinear(float[] values, int width, int height, float x, float y)
        {
            if (x < 0 || y < 0 || x > width - 1 || y > height - 1) return 0;
            int x0 = Math.Clamp((int)MathF.Floor(x), 0, width - 1), y0 = Math.Clamp((int)MathF.Floor(y), 0, height - 1);
            int x1 = Math.Min(x0 + 1, width - 1), y1 = Math.Min(y0 + 1, height - 1); float tx = x - x0, ty = y - y0;
            float top = values[y0 * width + x0] + (values[y0 * width + x1] - values[y0 * width + x0]) * tx;
            float bottom = values[y1 * width + x0] + (values[y1 * width + x1] - values[y1 * width + x0]) * tx;
            return top + (bottom - top) * ty;
        }

        private static Rgba32 SampleBilinear(Rgba32[] values, int width, int height, float x, float y)
        {
            if (x < 0 || y < 0 || x > width - 1 || y > height - 1) return new Rgba32(0, 0, 0, 255);
            int x0 = Math.Clamp((int)MathF.Floor(x), 0, width - 1), y0 = Math.Clamp((int)MathF.Floor(y), 0, height - 1);
            int x1 = Math.Min(x0 + 1, width - 1), y1 = Math.Min(y0 + 1, height - 1); float tx = x - x0, ty = y - y0;
            Rgba32 a = values[y0 * width + x0], b = values[y0 * width + x1], c = values[y1 * width + x0], d = values[y1 * width + x1];
            byte Mix(byte p00, byte p10, byte p01, byte p11) { float top = p00 + (p10 - p00) * tx; float bottom = p01 + (p11 - p01) * tx; return (byte)Math.Clamp(MathF.Round(top + (bottom - top) * ty), 0, 255); }
            return new Rgba32(Mix(a.R, b.R, c.R, d.R), Mix(a.G, b.G, c.G, d.G), Mix(a.B, b.B, c.B, d.B), 255);
        }

        private static async Task SaveGrayscaleAsync(float[] values, string path, CancellationToken token, bool normalize)
        {
            float max = normalize ? Math.Max(values.Max(), Epsilon) : 1f;
            using SixLabors.ImageSharp.Image<Rgba32> image = new(CanonicalWidth, CanonicalHeight);
            image.ProcessPixelRows(a => { for (int y = 0; y < CanonicalHeight; y++) { Span<Rgba32> row = a.GetRowSpan(y); for (int x = 0; x < CanonicalWidth; x++) { byte v = (byte)(Math.Clamp(values[y * CanonicalWidth + x] / max, 0, 1) * 255); row[x] = new Rgba32(v, v, v, 255); } } });
            await SaveImageAsync(image, path, token);
        }

        private static async Task SaveImageAsync(SixLabors.ImageSharp.Image<Rgba32> image, string path, CancellationToken token)
        {
            await using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
            await image.SaveAsync(stream, new PngEncoder(), token);
        }

        private static string SafeKey(string value) => new(value.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_').ToArray());

        private sealed class SimilarityMatch
        {
            public double Score { get; set; }
            public double Confidence { get; set; }
            public double AngleDegrees { get; set; }
            public double Scale { get; set; } = 1.0;
            public int OffsetX { get; set; }
            public int OffsetY { get; set; }
        }
    }
}
