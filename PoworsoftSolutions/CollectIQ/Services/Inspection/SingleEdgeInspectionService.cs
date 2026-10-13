using OpenCvSharp;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CollectIQ.Services.Inspection
{
    public enum EdgePosition
    {
        Top,
        Right,
        Bottom,
        Left
    }

    public sealed class SingleEdgeInspectionService
    {
        private const int MaxAnalysisSide = 900;

        public async Task<SingleEdgeInspectionResult> AnalyzeAsync(
            string imagePath,
            EdgePosition edge,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                throw new InvalidOperationException("Capture or load a close-up edge image first.");

            Stopwatch total = Stopwatch.StartNew();
            await InspectionDiagnosticLogger.WriteAsync("SingleEdge", "STAGE 1 - OpenCV load START");

            using Mat reduced = Cv2.ImRead(imagePath, ImreadModes.ReducedColor4);
            if (reduced.Empty())
                throw new InvalidOperationException("CollectIQ could not open the captured edge image.");

            Mat? fullFallback = null;
            Mat original = reduced;
            if (Math.Max(reduced.Width, reduced.Height) < 420)
            {
                fullFallback = Cv2.ImRead(imagePath, ImreadModes.Color);
                if (!fullFallback.Empty())
                    original = fullFallback;
            }

            cancellationToken.ThrowIfCancellationRequested();

            double scale = Math.Min(1.0, MaxAnalysisSide / (double)Math.Max(original.Width, original.Height));
            int targetWidth = Math.Max(1, (int)Math.Round(original.Width * scale));
            int targetHeight = Math.Max(1, (int)Math.Round(original.Height * scale));

            using Mat analysis = new();
            if (scale < 0.999)
                Cv2.Resize(original, analysis, new OpenCvSharp.Size(targetWidth, targetHeight), 0, 0, InterpolationFlags.Area);
            else
                original.CopyTo(analysis);

            NormalizeOrientation(analysis, edge);
            cancellationToken.ThrowIfCancellationRequested();

            await InspectionDiagnosticLogger.WriteAsync(
                "SingleEdge",
                "STAGE 1 - OpenCV load/orient COMPLETE",
                $"Elapsed={total.Elapsed.TotalSeconds:0.00}s; Analysis={analysis.Width}x{analysis.Height}; Edge={edge}");

            using Mat gray = new();
            Cv2.CvtColor(analysis, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.GaussianBlur(gray, gray, new OpenCvSharp.Size(5, 5), 0.9);

            cancellationToken.ThrowIfCancellationRequested();
            EdgeGeometry geometry = DetectPhysicalEdge(analysis, gray);

            if (geometry.GeometryConfidence < 40)
            {
                fullFallback?.Dispose();
                throw new InvalidOperationException(
                    $"Edge geometry confidence was only {geometry.GeometryConfidence:0}/100. Align the physical edge with the yellow guide, keep a little background visible outside the card, and retake the close-up.");
            }

            double captureQuality = CalculateCaptureQuality(gray, geometry);
            if (captureQuality < 25)
            {
                fullFallback?.Dispose();
                throw new InvalidOperationException(
                    "The edge image is too soft for a reliable inspection. Move closer, hold the phone steady, and retake the photo in better focus.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            EdgeDefectMetrics metrics = AnalyzeDefects(analysis, gray, geometry);
            double overallCondition = CalculateOverallCondition(metrics);

            await InspectionDiagnosticLogger.WriteAsync(
                "SingleEdge",
                "STAGE 2 - geometry/scoring COMPLETE",
                $"Elapsed={total.Elapsed.TotalSeconds:0.00}s; Geometry={geometry.GeometryConfidence:0}; Quality={captureQuality:0}");

            string outputDirectory = Path.Combine(
                FileSystem.AppDataDirectory,
                "EdgeInspections",
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(outputDirectory);

            string closeupPath = Path.Combine(outputDirectory, "edge_closeup.jpg");
            Cv2.ImWrite(closeupPath, analysis, new[] { (int)ImwriteFlags.JpegQuality, 91 });

            using Mat overlay = analysis.Clone();
            DrawOverlay(overlay, geometry, metrics);
            string overlayPath = Path.Combine(outputDirectory, "edge_analysis.jpg");
            Cv2.ImWrite(overlayPath, overlay, new[] { (int)ImwriteFlags.JpegQuality, 92 });

            await InspectionDiagnosticLogger.WriteAsync(
                "SingleEdge",
                "STAGE 3 - output COMPLETE",
                $"Elapsed={total.Elapsed.TotalSeconds:0.00}s");

            fullFallback?.Dispose();

            return new SingleEdgeInspectionResult
            {
                Edge = edge,
                OverallConditionScore = overallCondition,
                StraightnessConditionScore = ToCondition(metrics.StraightnessSeverity),
                WhiteningConditionScore = ToCondition(metrics.WhiteningSeverity),
                FrayingConditionScore = ToCondition(metrics.FrayingSeverity),
                NickConditionScore = ToCondition(metrics.NickSeverity),
                MissingMaterialConditionScore = ToCondition(metrics.MissingMaterialSeverity),
                DeformationConditionScore = ToCondition(metrics.DeformationSeverity),
                CaptureQuality = captureQuality,
                GeometryConfidence = geometry.GeometryConfidence,
                CloseupImagePath = closeupPath,
                AnalysisOverlayPath = overlayPath,
                Summary = BuildSummary(metrics, overallCondition),
                StraightnessExplanation = Explain("Straightness / waviness", metrics.StraightnessSeverity,
                    "The traced physical boundary is compared with its best-fit straight edge."),
                WhiteningExplanation = Explain("Whitening / chipping", metrics.WhiteningSeverity,
                    "The narrow band immediately inside the physical edge is compared with deeper card material for localized bright, low-colour exposed areas."),
                FrayingExplanation = Explain("Fraying / roughness", metrics.FrayingSeverity,
                    "CollectIQ measures rapid small contour changes and rough high-frequency structure along the physical edge."),
                NickExplanation = Explain("Dents / nicks", metrics.NickSeverity,
                    "Short localized inward deviations from the fitted physical edge are measured as possible nicks or dents."),
                MissingMaterialExplanation = Explain("Missing material", metrics.MissingMaterialSeverity,
                    "Larger or sustained inward losses from the expected straight boundary are measured as possible missing material."),
                DeformationExplanation = Explain("Localized deformation", metrics.DeformationSeverity,
                    "Longer-wave bends and local edge curvature are measured separately from tiny surface roughness.")
            };
        }

        private static void NormalizeOrientation(Mat image, EdgePosition edge)
        {
            switch (edge)
            {
                case EdgePosition.Right:
                    Cv2.Rotate(image, image, RotateFlags.Rotate90Counterclockwise);
                    break;
                case EdgePosition.Bottom:
                    Cv2.Flip(image, image, FlipMode.X);
                    break;
                case EdgePosition.Left:
                    Cv2.Rotate(image, image, RotateFlags.Rotate90Clockwise);
                    break;
            }
        }

        private static EdgeGeometry DetectPhysicalEdge(Mat color, Mat gray)
        {
            int width = gray.Width;
            int height = gray.Height;
            if (width < 180 || height < 180)
                throw new InvalidOperationException("Move closer so the selected edge occupies more of the image.");

            // Every selected edge is normalized so BACKGROUND is above and CARD is below.
            // The camera guide is intentionally a band rather than a pixel-perfect line.
            // Search only around the guide corridor so strong printed lines deeper inside the
            // card cannot steal the lock from the real physical boundary.
            using Mat blurred = new();
            Cv2.GaussianBlur(color, blurred, new OpenCvSharp.Size(5, 5), 0.9);

            using Mat gradY16 = new();
            using Mat gradY = new();
            Cv2.Sobel(gray, gradY16, MatType.CV_16S, 0, 1, 3);
            Cv2.ConvertScaleAbs(gradY16, gradY);

            int x0 = Math.Max(12, (int)(width * 0.06));
            int x1 = Math.Min(width - 13, (int)(width * 0.94));
            int guideY = height / 2;

            // Primary corridor corresponds to the visible thick guide. A slightly wider second
            // pass absorbs preview/capture crop differences without reopening the whole image.
            int primaryHalfBand = Math.Max(22, (int)Math.Round(height * 0.075));
            int fallbackHalfBand = Math.Max(primaryHalfBand + 22, (int)Math.Round(height * 0.200));

            List<EdgePoint> transitions = FindGuideBandTransitions(
                blurred, gradY, x0, x1, guideY, primaryHalfBand, false);

            bool usedFallbackBand = false;
            if (transitions.Count < 30)
            {
                transitions = FindGuideBandTransitions(
                    blurred, gradY, x0, x1, guideY, fallbackHalfBand, true);
                usedFallbackBand = true;
            }

            if (transitions.Count < 30)
                throw new InvalidOperationException(
                    "CollectIQ could not find enough of the physical edge inside the yellow guide band. Keep BACKGROUND on one side, CARD on the other, and place the edge anywhere inside the band.");

            // The transition finder already scans from known BACKGROUND toward CARD, so at this
            // point a rough edge is not evidence of bad geometry. Build a tolerant robust reference
            // line without throwing away the very deviations we want to grade.
            double medianY = Percentile(transitions.Select(p => (double)p.Y).ToList(), 0.50);
            double roughTolerance = Math.Max(14.0, height * 0.045);
            List<EdgePoint> rough = transitions
                .Where(p => Math.Abs(p.Y - medianY) <= roughTolerance)
                .ToList();

            if (rough.Count < 18)
                rough = transitions.ToList();

            LineFit first = FitLine(rough);
            double tolerantResidual = Math.Max(12.0, Math.Max(height * 0.032, first.ResidualStd * 3.5 + 2.0));
            List<EdgePoint> inliers = transitions
                .Where(p => Math.Abs(p.Y - (first.Slope * p.X + first.Intercept)) <= tolerantResidual)
                .ToList();

            // Do not reject a damaged edge simply because it contains nicks, fibres or missing
            // material. We only need enough of the physical boundary to establish its reference.
            if (inliers.Count < 16)
                inliers = rough;

            if (inliers.Count < 14)
                throw new InvalidOperationException(
                    "CollectIQ can see the guide-band transition but not enough of one physical card edge. Keep the edge inside the yellow band with visible background on the BACKGROUND side and retake.");

            LineFit fit = FitLine(inliers);
            double angleDegrees = Math.Abs(Math.Atan(fit.Slope) * 180.0 / Math.PI);
            double coverage = transitions.Count / (double)Math.Max(1, ((x1 - x0) / 3) + 1);
            double span = (transitions.Max(p => p.X) - transitions.Min(p => p.X)) / (double)Math.Max(1, x1 - x0);
            double inlierRatio = inliers.Count / (double)Math.Max(1, transitions.Count);
            double residualScore = Clamp01(1.0 - fit.ResidualStd / 18.0);
            double angleScore = Clamp01(1.0 - angleDegrees / 18.0);
            double strength = Clamp01((transitions.Average(p => p.Strength) - 5.0) / 42.0);

            // The physical edge may sit anywhere in the visible tolerance band. The thin line is
            // only an aiming center and contributes almost nothing to acceptance.
            double centerAtMidpoint = fit.Slope * (width / 2.0) + fit.Intercept;
            double guideDistance = Math.Abs(centerAtMidpoint - guideY);
            double guideTolerance = usedFallbackBand ? fallbackHalfBand : primaryHalfBand;
            double guideScore = Clamp01(1.0 - Math.Max(0.0, guideDistance - primaryHalfBand) /
                Math.Max(1.0, guideTolerance - primaryHalfBand + 1.0));

            // Confirm direction rather than straightness: immediately outside should mostly resemble
            // background and immediately inside should mostly differ from it. A ragged edge is allowed.
            int sideChecks = 0;
            int sidePasses = 0;
            for (int x = x0; x <= x1; x += Math.Max(9, width / 55))
            {
                int y = Math.Clamp((int)Math.Round(fit.Slope * x + fit.Intercept), 24, height - 30);
                int bgA = Math.Max(2, y - Math.Max(48, primaryHalfBand + 18));
                int bgB = Math.Max(bgA + 6, y - Math.Max(13, primaryHalfBand / 3));
                BgrStats background = MeasureBgr(blurred, x, bgA, bgB, 2);
                Vec3d outside = AverageBgrDouble(blurred, x, y - 9, 2);
                Vec3d inside1 = AverageBgrDouble(blurred, x, y + 10, 2);
                Vec3d inside2 = AverageBgrDouble(blurred, x, y + 20, 2);
                double threshold = Math.Max(11.0, background.MeanDistance * 1.8 + 5.5);
                bool outsideMatches = BgrDistance(outside, background.Mean) < threshold * 1.65;
                bool insideChanged =
                    BgrDistance(inside1, background.Mean) > threshold * 0.60 ||
                    BgrDistance(inside2, background.Mean) > threshold * 0.60;
                if (outsideMatches && insideChanged)
                    sidePasses++;
                sideChecks++;
            }

            double sideScore = sideChecks == 0 ? 0 : sidePasses / (double)sideChecks;
            double confidence = 100.0 * (
                0.24 * Clamp01(span) +
                0.20 * Clamp01(coverage) +
                0.14 * Clamp01(inlierRatio) +
                0.08 * residualScore +
                0.09 * angleScore +
                0.07 * strength +
                0.14 * sideScore +
                0.04 * guideScore);

            // Hard rejection is reserved for geometry that is clearly not the selected outer edge.
            // High residual by itself is NEVER a rejection reason because high residual may be the defect.
            if (angleDegrees > 20 || span < 0.42 || coverage < 0.20 || sideScore < 0.18 || guideDistance > fallbackHalfBand + 14)
                confidence = Math.Min(confidence, 34);

            int[] trace = BuildTrace(width, fit, transitions, height);
            return new EdgeGeometry(fit.Slope, fit.Intercept, fit.ResidualStd, angleDegrees, confidence, trace, inliers);
        }

        private static List<EdgePoint> FindGuideBandTransitions(
            Mat blurred,
            Mat gradY,
            int x0,
            int x1,
            int guideY,
            int halfBand,
            bool relaxed)
        {
            int height = blurred.Height;
            int scanY0 = Math.Max(18, guideY - halfBand);
            int scanY1 = Math.Min(height - 24, guideY + halfBand);
            int backgroundY0 = Math.Max(2, scanY0 - Math.Max(42, halfBand / 2));
            int backgroundY1 = Math.Max(backgroundY0 + 8, scanY0 - 8);

            List<EdgePoint> points = new();
            for (int x = x0; x <= x1; x += 3)
            {
                BgrStats background = MeasureBgr(blurred, x, backgroundY0, backgroundY1, 2);
                double threshold = Math.Max(relaxed ? 12.0 : 14.0,
                    background.MeanDistance * (relaxed ? 1.9 : 2.2) + (relaxed ? 5.5 : 7.0));

                // Walk from BACKGROUND toward CARD and accept the first persistent transition.
                // The pixel just outside the candidate must still look like the learned background.
                // That single rule is what prevents a red/white printed stripe deeper inside the
                // card from being mistaken for the physical edge.
                for (int y = scanY0; y <= scanY1; y++)
                {
                    Vec3d outside = AverageBgrDouble(blurred, x, Math.Max(0, y - 8), 1);
                    double outsideDistance = BgrDistance(outside, background.Mean);
                    if (outsideDistance > threshold * (relaxed ? 1.65 : 1.45))
                        continue;

                    int persistent = 0;
                    int[] offsets = { 4, 9, 15, 22, 30 };
                    foreach (int offset in offsets)
                    {
                        int yy = Math.Min(height - 1, y + offset);
                        Vec3d sample = AverageBgrDouble(blurred, x, yy, 1);
                        if (BgrDistance(sample, background.Mean) >= threshold)
                            persistent++;
                    }

                    if (persistent < (relaxed ? 2 : 3))
                        continue;

                    int localBestY = y;
                    byte localBestGradient = 0;
                    int localStart = Math.Max(scanY0, y - 3);
                    int localEnd = Math.Min(scanY1, y + 5);
                    for (int yy = localStart; yy <= localEnd; yy++)
                    {
                        byte gradient = gradY.At<byte>(yy, x);
                        if (gradient > localBestGradient)
                        {
                            localBestGradient = gradient;
                            localBestY = yy;
                        }
                    }

                    if (localBestGradient >= (relaxed ? 3 : 5))
                        points.Add(new EdgePoint(x, localBestY, localBestGradient));
                    break;
                }
            }

            return points;
        }

        private static BgrStats MeasureBgr(Mat image, int x, int y0, int y1, int halfWidth)
        {
            y0 = Math.Clamp(y0, 0, image.Height - 1);
            y1 = Math.Clamp(y1, y0, image.Height - 1);
            int xa = Math.Max(0, x - halfWidth);
            int xb = Math.Min(image.Width - 1, x + halfWidth);

            double b = 0, g = 0, r = 0;
            int count = 0;
            for (int y = y0; y <= y1; y += 2)
            for (int xx = xa; xx <= xb; xx++)
            {
                Vec3b p = image.At<Vec3b>(y, xx);
                b += p.Item0;
                g += p.Item1;
                r += p.Item2;
                count++;
            }

            if (count == 0)
                return new BgrStats(new Vec3d(), 0);

            Vec3d mean = new(b / count, g / count, r / count);
            double distance = 0;
            int distanceCount = 0;
            for (int y = y0; y <= y1; y += 3)
            for (int xx = xa; xx <= xb; xx++)
            {
                Vec3b p = image.At<Vec3b>(y, xx);
                double db = p.Item0 - mean.Item0;
                double dg = p.Item1 - mean.Item1;
                double dr = p.Item2 - mean.Item2;
                distance += Math.Sqrt(db * db + dg * dg + dr * dr);
                distanceCount++;
            }

            return new BgrStats(mean, distanceCount == 0 ? 0 : distance / distanceCount);
        }

        private static Vec3d AverageBgrDouble(Mat image, int x, int y, int halfWidth)
        {
            int xa = Math.Max(0, x - halfWidth);
            int xb = Math.Min(image.Width - 1, x + halfWidth);
            y = Math.Clamp(y, 0, image.Height - 1);
            double b = 0, g = 0, r = 0;
            int count = 0;
            for (int xx = xa; xx <= xb; xx++)
            {
                Vec3b p = image.At<Vec3b>(y, xx);
                b += p.Item0;
                g += p.Item1;
                r += p.Item2;
                count++;
            }
            return count == 0 ? new Vec3d() : new Vec3d(b / count, g / count, r / count);
        }

        private static double BgrDistance(Vec3d a, Vec3d b)
        {
            double db = a.Item0 - b.Item0;
            double dg = a.Item1 - b.Item1;
            double dr = a.Item2 - b.Item2;
            return Math.Sqrt(db * db + dg * dg + dr * dr);
        }

        private static int[] BuildTrace(int width, LineFit fit, List<EdgePoint> points, int height)
        {
            int[] trace = new int[width];
            for (int x = 0; x < width; x++)
                trace[x] = Math.Clamp((int)Math.Round(fit.Slope * x + fit.Intercept), 2, height - 3);

            foreach (EdgePoint p in points)
                trace[p.X] = Math.Clamp(p.Y, 2, height - 3);

            int lastKnown = -1;
            for (int x = 0; x < width; x++)
            {
                if (points.Any(p => p.X == x))
                {
                    if (lastKnown >= 0 && x - lastKnown > 1)
                    {
                        int ya = trace[lastKnown];
                        int yb = trace[x];
                        for (int k = lastKnown + 1; k < x; k++)
                            trace[k] = (int)Math.Round(ya + (yb - ya) * ((k - lastKnown) / (double)(x - lastKnown)));
                    }
                    lastKnown = x;
                }
            }

            // Gentle median smoothing keeps real nicks but removes single-pixel jitter.
            int[] smoothed = (int[])trace.Clone();
            for (int x = 3; x < width - 3; x++)
            {
                int[] w = { trace[x - 3], trace[x - 2], trace[x - 1], trace[x], trace[x + 1], trace[x + 2], trace[x + 3] };
                Array.Sort(w);
                smoothed[x] = w[3];
            }
            return smoothed;
        }

        private static LineFit FitLine(IReadOnlyList<EdgePoint> points)
        {
            double meanX = points.Average(p => (double)p.X);
            double meanY = points.Average(p => (double)p.Y);
            double num = 0;
            double den = 0;
            foreach (EdgePoint p in points)
            {
                double dx = p.X - meanX;
                num += dx * (p.Y - meanY);
                den += dx * dx;
            }

            double slope = den <= 1e-6 ? 0 : num / den;
            double intercept = meanY - slope * meanX;
            double variance = points.Average(p =>
            {
                double r = p.Y - (slope * p.X + intercept);
                return r * r;
            });
            return new LineFit(slope, intercept, Math.Sqrt(variance));
        }

        private static double CalculateCaptureQuality(Mat gray, EdgeGeometry geometry)
        {
            using Mat lap = new();
            Cv2.Laplacian(gray, lap, MatType.CV_64F);
            Cv2.MeanStdDev(lap, out _, out Scalar stddev);
            double variance = stddev.Val0 * stddev.Val0;
            double sharpness = Clamp01((variance - 18.0) / 150.0);
            return Math.Clamp(100.0 * (0.58 * sharpness + 0.42 * (geometry.GeometryConfidence / 100.0)), 0, 100);
        }

        private static EdgeDefectMetrics AnalyzeDefects(Mat color, Mat gray, EdgeGeometry geometry)
        {
            int width = color.Width;
            int height = color.Height;
            int margin = Math.Max(18, width / 18);
            List<double> residuals = new();
            List<double> highFrequency = new();
            int whiteningHits = 0;
            int whiteningSamples = 0;
            int inwardNickHits = 0;
            int missingHits = 0;
            int sampledColumns = 0;

            for (int x = margin; x < width - margin; x += 2)
            {
                int y = geometry.TraceY[x];
                double predicted = geometry.Slope * x + geometry.Intercept;
                double residual = y - predicted;
                residuals.Add(residual);
                sampledColumns++;

                if (x >= margin + 4 && x < width - margin - 4)
                {
                    double d1 = geometry.TraceY[x] - geometry.TraceY[x - 2];
                    double d2 = geometry.TraceY[x + 2] - geometry.TraceY[x];
                    highFrequency.Add(Math.Abs(d2 - d1));
                }

                if (residual > 4.0) inwardNickHits++;
                if (residual > 8.0) missingHits++;

                int edgeStart = Math.Clamp(y + 2, 0, height - 1);
                int edgeEnd = Math.Clamp(y + 10, 0, height - 1);
                int innerStart = Math.Clamp(y + 18, 0, height - 1);
                int innerEnd = Math.Clamp(y + 34, 0, height - 1);
                if (innerEnd <= innerStart || edgeEnd <= edgeStart) continue;

                Vec3b edgeColor = AverageBgr(color, x, edgeStart, edgeEnd);
                Vec3b innerColor = AverageBgr(color, x, innerStart, innerEnd);
                double edgeLum = Luma(edgeColor);
                double innerLum = Luma(innerColor);
                double edgeChroma = Math.Max(edgeColor.Item0, Math.Max(edgeColor.Item1, edgeColor.Item2)) -
                                    Math.Min(edgeColor.Item0, Math.Min(edgeColor.Item1, edgeColor.Item2));
                double innerChroma = Math.Max(innerColor.Item0, Math.Max(innerColor.Item1, innerColor.Item2)) -
                                     Math.Min(innerColor.Item0, Math.Min(innerColor.Item1, innerColor.Item2));

                bool substantiallyBrighter = edgeLum - innerLum > 22;
                bool lowColour = edgeChroma < Math.Max(34, innerChroma * 0.78 + 8);
                if (substantiallyBrighter && lowColour)
                    whiteningHits++;
                whiteningSamples++;
            }

            double residualStd = StdDev(residuals);
            double p95Inward = Percentile(residuals.Where(v => v > 0).ToList(), 0.95);
            List<double> outwardMagnitudes = residuals.Where(v => v < 0).Select(v => -v).ToList();
            double p95Outward = Percentile(outwardMagnitudes, 0.95);
            double outwardRatio = sampledColumns == 0 ? 0 : residuals.Count(v => v < -4.0) / (double)sampledColumns;
            double strongOutwardRatio = sampledColumns == 0 ? 0 : residuals.Count(v => v < -7.0) / (double)sampledColumns;
            double roughness = highFrequency.Count == 0 ? 0 : highFrequency.Average();
            double whiteningRatio = whiteningSamples == 0 ? 0 : whiteningHits / (double)whiteningSamples;
            double nickRatio = sampledColumns == 0 ? 0 : inwardNickHits / (double)sampledColumns;
            double missingRatio = sampledColumns == 0 ? 0 : missingHits / (double)sampledColumns;

            // Low-frequency deformation: compare chunks of the traced edge to the fitted line.
            int chunks = 10;
            List<double> chunkMeans = new();
            for (int c = 0; c < chunks; c++)
            {
                int a = margin + (width - 2 * margin) * c / chunks;
                int b = margin + (width - 2 * margin) * (c + 1) / chunks;
                if (b <= a) continue;
                double sum = 0;
                int n = 0;
                for (int x = a; x < b; x += 2)
                {
                    sum += geometry.TraceY[x] - (geometry.Slope * x + geometry.Intercept);
                    n++;
                }
                if (n > 0) chunkMeans.Add(sum / n);
            }

            double deformationSpread = chunkMeans.Count < 2 ? 0 : chunkMeans.Max() - chunkMeans.Min();

            return new EdgeDefectMetrics
            {
                StraightnessSeverity = ScaleSeverity(residualStd, 1.15, 5.5),
                FrayingSeverity = Math.Clamp(
                    ScaleSeverity(roughness, 0.55, 4.2) * 0.30 +
                    Math.Clamp(outwardRatio * 420.0, 0, 100) * 0.35 +
                    Math.Clamp(strongOutwardRatio * 760.0, 0, 100) * 0.20 +
                    ScaleSeverity(p95Outward, 3.5, 11.0) * 0.15,
                    0, 100),
                WhiteningSeverity = Math.Clamp(whiteningRatio * 145.0, 0, 100),
                NickSeverity = Math.Clamp(nickRatio * 240.0 + ScaleSeverity(p95Inward, 3.0, 10.0) * 0.35, 0, 100),
                MissingMaterialSeverity = Math.Clamp(missingRatio * 420.0 + ScaleSeverity(p95Inward, 7.0, 18.0) * 0.45, 0, 100),
                DeformationSeverity = ScaleSeverity(deformationSpread, 2.5, 14.0)
            };
        }

        private static Vec3b AverageBgr(Mat image, int x, int y0, int y1)
        {
            int half = 1;
            long b = 0, g = 0, r = 0;
            int n = 0;
            for (int yy = y0; yy <= y1; yy++)
            for (int xx = Math.Max(0, x - half); xx <= Math.Min(image.Width - 1, x + half); xx++)
            {
                Vec3b p = image.At<Vec3b>(yy, xx);
                b += p.Item0; g += p.Item1; r += p.Item2; n++;
            }
            if (n == 0) return new Vec3b();
            return new Vec3b((byte)(b / n), (byte)(g / n), (byte)(r / n));
        }

        private static double Luma(Vec3b bgr) => 0.114 * bgr.Item0 + 0.587 * bgr.Item1 + 0.299 * bgr.Item2;

        private static void DrawOverlay(Mat image, EdgeGeometry geometry, EdgeDefectMetrics metrics)
        {
            int width = image.Width;
            int yA = Math.Clamp((int)Math.Round(geometry.Intercept), 0, image.Height - 1);
            int yB = Math.Clamp((int)Math.Round(geometry.Slope * (width - 1) + geometry.Intercept), 0, image.Height - 1);
            // Cyan = robust straight reference. Green = actual physical boundary traced by CollectIQ.
            Cv2.Line(image, new OpenCvSharp.Point(0, yA), new OpenCvSharp.Point(width - 1, yB), new Scalar(255, 220, 0), 2, LineTypes.AntiAlias);

            List<OpenCvSharp.Point> traced = new();
            for (int x = 2; x < width - 2; x += 3)
                traced.Add(new OpenCvSharp.Point(x, geometry.TraceY[x]));
            if (traced.Count >= 2)
                Cv2.Polylines(image, new[] { traced.ToArray() }, false, new Scalar(0, 255, 0), 2, LineTypes.AntiAlias);

            for (int x = 12; x < width - 12; x += 5)
            {
                int actualY = geometry.TraceY[x];
                double expectedY = geometry.Slope * x + geometry.Intercept;
                double inward = actualY - expectedY;
                if (inward > 7)
                    Cv2.Circle(image, new OpenCvSharp.Point(x, actualY), 5, new Scalar(0, 0, 255), -1, LineTypes.AntiAlias);
                else if (inward < -7)
                    Cv2.Circle(image, new OpenCvSharp.Point(x, actualY), 5, new Scalar(255, 120, 0), -1, LineTypes.AntiAlias);
                else if (Math.Abs(inward) > 3.5)
                    Cv2.Circle(image, new OpenCvSharp.Point(x, actualY), 3, new Scalar(0, 190, 255), -1, LineTypes.AntiAlias);
            }

            string text = $"EDGE LOCK {geometry.GeometryConfidence:0}/100";
            Cv2.PutText(image, text, new OpenCvSharp.Point(18, 34), HersheyFonts.HersheySimplex, 0.75, new Scalar(0, 255, 0), 2, LineTypes.AntiAlias);
        }

        private static double CalculateOverallCondition(EdgeDefectMetrics m)
        {
            double severity =
                0.18 * m.StraightnessSeverity +
                0.18 * m.WhiteningSeverity +
                0.17 * m.FrayingSeverity +
                0.18 * m.NickSeverity +
                0.17 * m.MissingMaterialSeverity +
                0.12 * m.DeformationSeverity;
            return Math.Clamp(100.0 - severity, 0, 100);
        }

        private static string BuildSummary(EdgeDefectMetrics m, double overall)
        {
            var ranked = new[]
            {
                ("straightness / waviness", m.StraightnessSeverity),
                ("whitening / chipping", m.WhiteningSeverity),
                ("fraying / roughness", m.FrayingSeverity),
                ("dents / nicks", m.NickSeverity),
                ("missing material", m.MissingMaterialSeverity),
                ("localized deformation", m.DeformationSeverity)
            }.OrderByDescending(x => x.Item2).ToArray();

            if (ranked[0].Item2 < 18)
                return $"Condition {overall:0}/100. No strong edge defect signal was detected.";

            string second = ranked[1].Item2 >= 22 ? $" Secondary signal: {ranked[1].Item1}." : string.Empty;
            return $"Condition {overall:0}/100. Strongest signal: {ranked[0].Item1}.{second}";
        }

        private static string Explain(string name, double severity, string detail)
        {
            string level = severity switch
            {
                < 15 => "No meaningful signal",
                < 32 => "Light signal",
                < 55 => "Moderate signal",
                < 75 => "Strong signal",
                _ => "Severe signal"
            };
            return $"{name}: {100 - severity:0}/100 condition • {level}. {detail}";
        }

        private static double ToCondition(double severity) => Math.Clamp(100.0 - severity, 0, 100);

        private static double ScaleSeverity(double value, double good, double bad)
        {
            if (value <= good) return 0;
            if (value >= bad) return 100;
            return 100.0 * (value - good) / (bad - good);
        }

        private static double StdDev(IReadOnlyList<double> values)
        {
            if (values.Count < 2) return 0;
            double mean = values.Average();
            return Math.Sqrt(values.Average(v => (v - mean) * (v - mean)));
        }

        private static double Percentile(List<double> values, double percentile)
        {
            if (values.Count == 0) return 0;
            values.Sort();
            int index = Math.Clamp((int)Math.Round((values.Count - 1) * percentile), 0, values.Count - 1);
            return values[index];
        }

        private static double Clamp01(double v) => Math.Clamp(v, 0.0, 1.0);

        private sealed record BgrStats(Vec3d Mean, double MeanDistance);
        private sealed record EdgePoint(int X, int Y, byte Strength);
        private sealed record LineFit(double Slope, double Intercept, double ResidualStd);
        private sealed record EdgeGeometry(
            double Slope,
            double Intercept,
            double ResidualStd,
            double AngleDegrees,
            double GeometryConfidence,
            int[] TraceY,
            IReadOnlyList<EdgePoint> Inliers);

        private sealed class EdgeDefectMetrics
        {
            public double StraightnessSeverity { get; set; }
            public double WhiteningSeverity { get; set; }
            public double FrayingSeverity { get; set; }
            public double NickSeverity { get; set; }
            public double MissingMaterialSeverity { get; set; }
            public double DeformationSeverity { get; set; }
        }
    }

    public sealed class SingleEdgeInspectionResult
    {
        public EdgePosition Edge { get; init; }
        public double OverallConditionScore { get; init; }
        public double StraightnessConditionScore { get; init; }
        public double WhiteningConditionScore { get; init; }
        public double FrayingConditionScore { get; init; }
        public double NickConditionScore { get; init; }
        public double MissingMaterialConditionScore { get; init; }
        public double DeformationConditionScore { get; init; }
        public double CaptureQuality { get; init; }
        public double GeometryConfidence { get; init; }
        public string CloseupImagePath { get; init; } = string.Empty;
        public string AnalysisOverlayPath { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public string StraightnessExplanation { get; init; } = string.Empty;
        public string WhiteningExplanation { get; init; } = string.Empty;
        public string FrayingExplanation { get; init; } = string.Empty;
        public string NickExplanation { get; init; } = string.Empty;
        public string MissingMaterialExplanation { get; init; } = string.Empty;
        public string DeformationExplanation { get; init; } = string.Empty;
    }
}
