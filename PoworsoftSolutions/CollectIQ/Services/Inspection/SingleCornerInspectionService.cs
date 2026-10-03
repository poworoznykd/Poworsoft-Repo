using OpenCvSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CollectIQ.Services.Inspection
{
    public enum CornerPosition
    {
        TopLeft,
        TopRight,
        BottomRight,
        BottomLeft
    }

    public sealed class SingleCornerInspectionService
    {
        private const int MaxAnalysisSide = 800;

        public async Task<SingleCornerInspectionResult> AnalyzeAsync(
            string imagePath,
            CornerPosition corner,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                throw new InvalidOperationException("Capture or load a close-up corner image first.");

            Stopwatch total = Stopwatch.StartNew();

            await InspectionDiagnosticLogger.WriteAsync("SingleCorner", "STAGE 1 - OpenCV load START");
            using Mat reduced = Cv2.ImRead(imagePath, ImreadModes.ReducedColor4);
            if (reduced.Empty())
                throw new InvalidOperationException("CollectIQ could not open the captured corner image.");

            Mat? fullFallback = null;
            Mat original = reduced;
            if (Math.Max(reduced.Width, reduced.Height) < 360)
            {
                fullFallback = Cv2.ImRead(imagePath, ImreadModes.Color);
                if (!fullFallback.Empty())
                    original = fullFallback;
            }
            cancellationToken.ThrowIfCancellationRequested();
            await InspectionDiagnosticLogger.WriteAsync("SingleCorner", "STAGE 1 - OpenCV load COMPLETE", $"Elapsed={total.Elapsed.TotalSeconds:0.00}s; Size={original.Width}x{original.Height}");

            double scale = Math.Min(1.0, MaxAnalysisSide / (double)Math.Max(original.Width, original.Height));
            int targetWidth = Math.Max(1, (int)Math.Round(original.Width * scale));
            int targetHeight = Math.Max(1, (int)Math.Round(original.Height * scale));

            using Mat analysis = new();
            if (scale < 0.999)
                Cv2.Resize(original, analysis, new OpenCvSharp.Size(targetWidth, targetHeight), 0, 0, InterpolationFlags.Area);
            else
                original.CopyTo(analysis);

            NormalizeOrientation(analysis, corner);
            cancellationToken.ThrowIfCancellationRequested();
            await InspectionDiagnosticLogger.WriteAsync("SingleCorner", "STAGE 2 - resize/orient COMPLETE", $"Elapsed={total.Elapsed.TotalSeconds:0.00}s; Analysis={analysis.Width}x{analysis.Height}");

            int widthPx = analysis.Width;
            int heightPx = analysis.Height;
            Rgba32[] pixels = CopyBgrToRgba(analysis);

            BackgroundModel background = EstimateBackground(pixels, widthPx, heightPx);

            // Build two independent segmentation hypotheses.  The legacy colour-distance
            // mask is very fast and works well on plain matte surfaces.  GrabCut uses the
            // fact that, after orientation normalization, background is expected toward
            // the top/left while card material extends toward the lower/right.  This is
            // much less sensitive to whether the surface is black, grey, glossy or matte.
            bool[] colorMask = BuildCardMask(pixels, widthPx, heightPx, background);
            colorMask = MajorityFilter(colorMask, widthPx, heightPx, 1);
            colorMask = KeepLargestCardComponent(colorMask, widthPx, heightPx);

            bool[] adaptiveMask = BuildAdaptiveCardMask(analysis, widthPx, heightPx);
            adaptiveMask = MajorityFilter(adaptiveMask, widthPx, heightPx, 1);
            adaptiveMask = KeepLargestCardComponent(adaptiveMask, widthPx, heightPx);

            bool colorFound = TryFindCornerGeometry(colorMask, widthPx, heightPx, out CornerGeometry colorGeometry);
            bool adaptiveFound = TryFindCornerGeometry(adaptiveMask, widthPx, heightPx, out CornerGeometry adaptiveGeometry);

            bool[] mask;
            CornerGeometry geometry;
            string segmentationMethod;

            if (adaptiveFound && (!colorFound || adaptiveGeometry.GeometryConfidence >= colorGeometry.GeometryConfidence))
            {
                mask = adaptiveMask;
                geometry = adaptiveGeometry;
                segmentationMethod = "adaptive";
            }
            else if (colorFound)
            {
                mask = colorMask;
                geometry = colorGeometry;
                segmentationMethod = "color";
            }
            else
            {
                throw new InvalidOperationException(
                    "CollectIQ could not lock onto both physical card edges. Keep the selected corner near the yellow L, show both card edges clearly, and leave some background visible outside them.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            await InspectionDiagnosticLogger.WriteAsync(
                "SingleCorner",
                "STAGE 3 - segmentation COMPLETE",
                $"Elapsed={total.Elapsed.TotalSeconds:0.00}s; Method={segmentationMethod}; Adaptive={(adaptiveFound ? adaptiveGeometry.GeometryConfidence : 0):0}; Color={(colorFound ? colorGeometry.GeometryConfidence : 0):0}");

            // Keep a confidence gate, but do not demand a studio-style background.  The
            // winning segmentation already had to satisfy line fit, orthogonality and
            // inside/outside geometry checks.
            if (geometry.GeometryConfidence < 50)
                throw new InvalidOperationException(
                    $"Corner geometry confidence was only {geometry.GeometryConfidence:0}/100. CollectIQ could not verify the physical corner well enough to grade it. Keep both edges visible and reduce glare directly on the card edge.");

            cancellationToken.ThrowIfCancellationRequested();
            double captureQuality = CalculateCaptureQuality(pixels, mask, widthPx, heightPx, geometry);
            if (captureQuality < 28)
                throw new InvalidOperationException(
                    "The corner image is too soft or poorly separated from the background. Retake it closer, flatter, and in better focus.");

            CornerDefectMetrics metrics = AnalyzeDefects(pixels, mask, widthPx, heightPx, geometry);
            double overallCondition = CalculateOverallCondition(metrics);
            cancellationToken.ThrowIfCancellationRequested();
            await InspectionDiagnosticLogger.WriteAsync("SingleCorner", "STAGE 4 - geometry/scoring COMPLETE", $"Elapsed={total.Elapsed.TotalSeconds:0.00}s; GeometryConfidence={geometry.GeometryConfidence:0}");

            string outputDirectory = Path.Combine(
                FileSystem.AppDataDirectory,
                "CornerInspections",
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(outputDirectory);

            string normalizedPath = Path.Combine(outputDirectory, "corner_closeup.jpg");
            Cv2.ImWrite(normalizedPath, analysis, new[] { (int)ImwriteFlags.JpegQuality, 90 });

            using Mat overlay = analysis.Clone();
            DrawAnalysisOverlay(overlay, geometry, metrics);
            string overlayPath = Path.Combine(outputDirectory, "corner_analysis.jpg");
            Cv2.ImWrite(overlayPath, overlay, new[] { (int)ImwriteFlags.JpegQuality, 90 });
            await InspectionDiagnosticLogger.WriteAsync("SingleCorner", "STAGE 5 - output COMPLETE", $"Elapsed={total.Elapsed.TotalSeconds:0.00}s");

            fullFallback?.Dispose();

            return new SingleCornerInspectionResult
            {
                Corner = corner,
                OverallConditionScore = overallCondition,
                ShapeConditionScore = ToCondition(metrics.ShapeLossSeverity),
                FrayingConditionScore = ToCondition(metrics.FrayingSeverity),
                WhiteningConditionScore = ToCondition(metrics.WhiteningSeverity),
                CrushBendConditionScore = ToCondition(metrics.CrushBendSeverity),
                MissingMaterialConditionScore = ToCondition(metrics.MissingMaterialSeverity),
                DeformationConditionScore = ToCondition(metrics.DeformationSeverity),
                CaptureQuality = captureQuality,
                GeometryConfidence = geometry.GeometryConfidence,
                NormalizedImagePath = normalizedPath,
                AnalysisOverlayPath = overlayPath,
                Summary = BuildSummary(metrics, overallCondition),
                ShapeExplanation = BuildMetricExplanation("Shape / rounding", metrics.ShapeLossSeverity,
                    "The detected physical contour is compared with the sharp intersection predicted by the two straight edge segments."),
                FrayingExplanation = BuildMetricExplanation("Fraying / fuzzing", metrics.FrayingSeverity,
                    "CollectIQ measures small high-frequency contour excursions and fiber-like irregularity along the two edge segments nearest the tip."),
                WhiteningExplanation = BuildMetricExplanation("Whitening / chipping", metrics.WhiteningSeverity,
                    "Pixels immediately inside the physical edge are compared with nearby interior card material for bright, low-chroma exposed areas."),
                CrushBendExplanation = BuildMetricExplanation("Crush / bend", metrics.CrushBendSeverity,
                    "The corner is checked for abrupt local edge-angle changes and paired light/dark structure consistent with compression or bending."),
                MissingMaterialExplanation = BuildMetricExplanation("Missing material", metrics.MissingMaterialSeverity,
                    "CollectIQ measures how much expected card area is absent near the theoretical sharp corner."),
                DeformationExplanation = BuildMetricExplanation("Localized deformation", metrics.DeformationSeverity,
                    "Local contour curvature and texture distortion are measured around the corner rather than across the printed artwork."),
            };
        }

        private static Rgba32[] CopyBgrToRgba(Mat image)
        {
            using Mat? continuousCopy = image.IsContinuous() ? null : image.Clone();
            Mat source = continuousCopy ?? image;

            int count = source.Width * source.Height;
            byte[] bgr = new byte[count * 3];
            Marshal.Copy(source.Data, bgr, 0, bgr.Length);
            Rgba32[] pixels = new Rgba32[count];
            for (int i = 0, j = 0; i < count; i++, j += 3)
                pixels[i] = new Rgba32(bgr[j + 2], bgr[j + 1], bgr[j], 255);
            return pixels;
        }

        private static void NormalizeOrientation(Mat image, CornerPosition corner)
        {
            switch (corner)
            {
                case CornerPosition.TopRight:
                    Cv2.Flip(image, image, FlipMode.Y);
                    break;
                case CornerPosition.BottomRight:
                    Cv2.Flip(image, image, FlipMode.XY);
                    break;
                case CornerPosition.BottomLeft:
                    Cv2.Flip(image, image, FlipMode.X);
                    break;
            }
        }

        private static BackgroundModel EstimateBackground(Rgba32[] pixels, int width, int height)
        {
            int sampleWidth = Math.Max(12, width / 7);
            int sampleHeight = Math.Max(12, height / 7);
            List<(double R, double G, double B)> values = new(sampleWidth * sampleHeight);

            for (int y = 0; y < sampleHeight; y++)
            for (int x = 0; x < sampleWidth; x++)
            {
                Rgba32 p = pixels[(y * width) + x];
                values.Add((p.R / 255.0, p.G / 255.0, p.B / 255.0));
            }

            double r = values.Average(v => v.R);
            double g = values.Average(v => v.G);
            double b = values.Average(v => v.B);
            double spread = Math.Sqrt(values.Average(v =>
                ((v.R - r) * (v.R - r)) +
                ((v.G - g) * (v.G - g)) +
                ((v.B - b) * (v.B - b))));

            return new BackgroundModel(r, g, b, spread);
        }


        private static bool[] BuildAdaptiveCardMask(Mat image, int width, int height)
        {
            // The capture guide places the selected corner near the centre.  After
            // NormalizeOrientation(), the card always extends inward toward the
            // lower-right.  Seed GrabCut from those geometric facts rather than from a
            // particular background colour.
            using Mat gcMask = new(height, width, MatType.CV_8UC1, new Scalar((byte)GrabCutClasses.PR_BGD));

            int bgBandX = Math.Max(8, (int)Math.Round(width * 0.10));
            int bgBandY = Math.Max(8, (int)Math.Round(height * 0.10));
            int probableStartX = Math.Clamp((int)Math.Round(width * 0.50), 0, width - 1);
            int probableStartY = Math.Clamp((int)Math.Round(height * 0.50), 0, height - 1);
            int foregroundStartX = Math.Clamp((int)Math.Round(width * 0.66), 0, width - 1);
            int foregroundStartY = Math.Clamp((int)Math.Round(height * 0.66), 0, height - 1);

            // Definite background well outside the two expected physical edges.
            gcMask[new OpenCvSharp.Rect(0, 0, width, bgBandY)].SetTo((byte)GrabCutClasses.BGD);
            gcMask[new OpenCvSharp.Rect(0, 0, bgBandX, height)].SetTo((byte)GrabCutClasses.BGD);

            // Broad probable-card seed, then a smaller definite-card seed.  This lets
            // GrabCut model colourful/white/black card artwork instead of assuming the
            // card has one representative colour.
            gcMask[new OpenCvSharp.Rect(
                probableStartX, probableStartY,
                Math.Max(1, width - probableStartX),
                Math.Max(1, height - probableStartY))].SetTo((byte)GrabCutClasses.PR_FGD);

            gcMask[new OpenCvSharp.Rect(
                foregroundStartX, foregroundStartY,
                Math.Max(1, width - foregroundStartX),
                Math.Max(1, height - foregroundStartY))].SetTo((byte)GrabCutClasses.FGD);

            using Mat bgModel = new();
            using Mat fgModel = new();
            try
            {
                Cv2.GrabCut(
                    image,
                    gcMask,
                    new OpenCvSharp.Rect(),
                    bgModel,
                    fgModel,
                    2,
                    GrabCutModes.InitWithMask);
            }
            catch
            {
                // Returning an empty mask simply causes the colour-mask hypothesis to be
                // used.  A segmentation helper should never make the whole inspection fail.
                return new bool[width * height];
            }

            byte[] labels = new byte[width * height];
            Marshal.Copy(gcMask.Data, labels, 0, labels.Length);
            bool[] result = new bool[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                byte label = labels[i];
                result[i] = label == (byte)GrabCutClasses.FGD || label == (byte)GrabCutClasses.PR_FGD;
            }

            return result;
        }

        private static bool[] BuildCardMask(Rgba32[] pixels, int width, int height, BackgroundModel bg)
        {
            bool[] mask = new bool[width * height];
            double threshold = Math.Clamp(0.105 + (bg.Spread * 2.8), 0.10, 0.30);

            for (int i = 0; i < pixels.Length; i++)
            {
                Rgba32 p = pixels[i];
                double r = p.R / 255.0;
                double g = p.G / 255.0;
                double b = p.B / 255.0;
                double distance = Math.Sqrt(
                    ((r - bg.R) * (r - bg.R)) +
                    ((g - bg.G) * (g - bg.G)) +
                    ((b - bg.B) * (b - bg.B)));
                mask[i] = distance >= threshold;
            }

            return mask;
        }

        private static bool[] MajorityFilter(bool[] input, int width, int height, int passes)
        {
            bool[] current = input;
            for (int pass = 0; pass < passes; pass++)
            {
                bool[] output = (bool[])current.Clone();
                for (int y = 1; y < height - 1; y++)
                for (int x = 1; x < width - 1; x++)
                {
                    int row0 = (y - 1) * width;
                    int row1 = y * width;
                    int row2 = (y + 1) * width;
                    int on =
                        (current[row0 + x - 1] ? 1 : 0) +
                        (current[row0 + x] ? 1 : 0) +
                        (current[row0 + x + 1] ? 1 : 0) +
                        (current[row1 + x - 1] ? 1 : 0) +
                        (current[row1 + x] ? 1 : 0) +
                        (current[row1 + x + 1] ? 1 : 0) +
                        (current[row2 + x - 1] ? 1 : 0) +
                        (current[row2 + x] ? 1 : 0) +
                        (current[row2 + x + 1] ? 1 : 0);
                    output[row1 + x] = on >= 5;
                }
                current = output;
            }
            return current;
        }


        private static bool[] KeepLargestCardComponent(bool[] input, int width, int height)
        {
            int total = width * height;
            bool[] visited = new bool[total];
            int[] queue = new int[total];
            List<int> best = new();
            double bestScore = 0;

            int minArea = Math.Max(1200, (int)(total * 0.035));
            for (int start = 0; start < total; start++)
            {
                if (!input[start] || visited[start])
                    continue;

                int head = 0, tail = 0;
                queue[tail++] = start;
                visited[start] = true;
                List<int> component = new();
                int minX = width, minY = height, maxX = 0, maxY = 0;

                while (head < tail)
                {
                    int index = queue[head++];
                    component.Add(index);
                    int y = index / width;
                    int x = index - (y * width);
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;

                    if (x > 0) Add(index - 1);
                    if (x + 1 < width) Add(index + 1);
                    if (y > 0) Add(index - width);
                    if (y + 1 < height) Add(index + width);
                }

                if (component.Count < minArea)
                    continue;

                double spanX = (maxX - minX + 1) / (double)width;
                double spanY = (maxY - minY + 1) / (double)height;
                double areaFraction = component.Count / (double)total;
                double score = component.Count * (0.35 + Math.Min(1.0, spanX + spanY));

                // After orientation normalization the card interior should extend toward
                // the lower-right from the selected corner. Reward components that do so.
                if (maxX >= width * 0.68) score *= 1.20;
                if (maxY >= height * 0.68) score *= 1.20;
                if (areaFraction > 0.92) score *= 0.25;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = component;
                }

                void Add(int neighbor)
                {
                    if (!visited[neighbor] && input[neighbor])
                    {
                        visited[neighbor] = true;
                        queue[tail++] = neighbor;
                    }
                }
            }

            if (best.Count == 0)
                return input;

            bool[] output = new bool[total];
            foreach (int index in best) output[index] = true;
            return output;
        }

        private static bool TryFindCornerGeometry(bool[] mask, int width, int height, out CornerGeometry geometry)
        {
            geometry = new CornerGeometry();
            int minX = Math.Max(4, width / 20);
            int minY = Math.Max(4, height / 20);
            int maxX = Math.Min(width - 5, (int)(width * 0.92));
            int maxY = Math.Min(height - 5, (int)(height * 0.92));

            List<PointD> topBoundary = new();
            for (int x = minX; x <= maxX; x++)
            {
                int first = -1;
                for (int y = minY; y <= maxY; y++)
                {
                    if (mask[(y * width) + x]) { first = y; break; }
                }
                if (first >= 0) topBoundary.Add(new PointD(x, first));
            }

            List<PointD> leftBoundary = new();
            for (int y = minY; y <= maxY; y++)
            {
                int first = -1;
                for (int x = minX; x <= maxX; x++)
                {
                    if (mask[(y * width) + x]) { first = x; break; }
                }
                if (first >= 0) leftBoundary.Add(new PointD(first, y));
            }

            if (topBoundary.Count < width * 0.22 || leftBoundary.Count < height * 0.22)
                return false;

            double roughCornerX = Percentile(topBoundary.Select(p => p.X).ToList(), 0.12);
            double roughCornerY = Percentile(leftBoundary.Select(p => p.Y).ToList(), 0.12);

            double edgeStartX = Math.Clamp(roughCornerX + (width * 0.08), 0, width - 1);
            double edgeStartY = Math.Clamp(roughCornerY + (height * 0.08), 0, height - 1);

            List<PointD> topFitPoints = topBoundary
                .Where(p => p.X >= edgeStartX && p.X <= width * 0.90)
                .ToList();
            List<PointD> leftFitPoints = leftBoundary
                .Where(p => p.Y >= edgeStartY && p.Y <= height * 0.90)
                .ToList();

            if (topFitPoints.Count < 30 || leftFitPoints.Count < 30)
                return false;

            LineModel topLine = RobustFitYFromX(topFitPoints);
            LineModel leftLineAsXFromY = RobustFitYFromX(leftFitPoints.Select(p => new PointD(p.Y, p.X)).ToList());

            // The user has already told us which corner this is and the image is normalized
            // so that it behaves like a top-left corner. Reject artwork/diagonal lines that
            // cannot plausibly be the two physical edges of that corner.
            double topAngle = Math.Abs(Math.Atan(topLine.Slope) * 180.0 / Math.PI);
            double leftAngle = Math.Abs(Math.Atan(leftLineAsXFromY.Slope) * 180.0 / Math.PI);
            if (topAngle > 32 || leftAngle > 32)
                return false;

            double denominator = 1.0 - (leftLineAsXFromY.Slope * topLine.Slope);
            if (Math.Abs(denominator) < 0.05)
                return false;

            double idealX = (leftLineAsXFromY.Slope * topLine.Intercept + leftLineAsXFromY.Intercept) / denominator;
            double idealY = (topLine.Slope * idealX) + topLine.Intercept;

            if (idealX < -width * 0.10 || idealY < -height * 0.10 || idealX > width * 0.75 || idealY > height * 0.75)
                return false;

            double analysisRadius = Math.Clamp(Math.Min(width, height) * 0.20, 55, 220);

            double topResidual = FitResidual(topFitPoints, topLine);
            LineModel leftForResidual = leftLineAsXFromY;
            double leftResidual = FitResidual(
                leftFitPoints.Select(p => new PointD(p.Y, p.X)).ToList(),
                leftForResidual);

            double directionDot = Math.Abs(
                (1.0 * leftLineAsXFromY.Slope) +
                (topLine.Slope * 1.0)) /
                (Math.Sqrt(1.0 + (topLine.Slope * topLine.Slope)) *
                 Math.Sqrt(1.0 + (leftLineAsXFromY.Slope * leftLineAsXFromY.Slope)));

            double orthogonalityScore = Clamp01(1.0 - (directionDot / 0.42));
            double residualScale = Math.Max(3.0, Math.Min(width, height) * 0.012);
            double residualScore = Clamp01(1.0 - (((topResidual + leftResidual) * 0.5) / residualScale));
            double supportScore = Math.Min(1.0, Math.Min(
                topFitPoints.Count / Math.Max(1.0, width * 0.48),
                leftFitPoints.Count / Math.Max(1.0, height * 0.48)));
            double regionScore = ValidateCornerRegions(mask, width, height, idealX, idealY, analysisRadius);

            double geometryConfidence = 100.0 * (
                (orthogonalityScore * 0.25) +
                (residualScore * 0.30) +
                (supportScore * 0.15) +
                (regionScore * 0.30));

            geometry = new CornerGeometry
            {
                IdealX = idealX,
                IdealY = idealY,
                TopSlope = topLine.Slope,
                TopIntercept = topLine.Intercept,
                LeftSlopeXFromY = leftLineAsXFromY.Slope,
                LeftInterceptXFromY = leftLineAsXFromY.Intercept,
                Radius = analysisRadius,
                GeometryConfidence = geometryConfidence,
                TopBoundary = topBoundary,
                LeftBoundary = leftBoundary
            };
            return true;
        }


        private static double FitResidual(List<PointD> points, LineModel line)
        {
            if (points.Count == 0) return double.MaxValue;
            List<double> residuals = points
                .Select(p => Math.Abs(p.Y - ((line.Slope * p.X) + line.Intercept)))
                .ToList();
            return Percentile(residuals, 0.70);
        }

        private static double ValidateCornerRegions(
            bool[] mask, int width, int height, double cornerX, double cornerY, double radius)
        {
            int step = Math.Max(2, Math.Min(width, height) / 180);
            double inside = SampleMaskFraction(mask, width, height,
                cornerX + radius * 0.18, cornerY + radius * 0.18,
                cornerX + radius * 0.80, cornerY + radius * 0.80, step);
            double above = SampleMaskFraction(mask, width, height,
                cornerX + radius * 0.18, cornerY - radius * 0.70,
                cornerX + radius * 0.80, cornerY - radius * 0.10, step);
            double left = SampleMaskFraction(mask, width, height,
                cornerX - radius * 0.70, cornerY + radius * 0.18,
                cornerX - radius * 0.10, cornerY + radius * 0.80, step);

            // The true card corner has card material down/right and background above/left.
            double insideScore = Clamp01((inside - 0.55) / 0.35);
            double outsideScore = Clamp01(1.0 - ((above + left) * 0.5) / 0.35);
            return (insideScore * 0.60) + (outsideScore * 0.40);
        }

        private static double SampleMaskFraction(
            bool[] mask, int width, int height,
            double x0d, double y0d, double x1d, double y1d, int step)
        {
            int x0 = Math.Clamp((int)Math.Round(Math.Min(x0d, x1d)), 0, width - 1);
            int x1 = Math.Clamp((int)Math.Round(Math.Max(x0d, x1d)), 0, width - 1);
            int y0 = Math.Clamp((int)Math.Round(Math.Min(y0d, y1d)), 0, height - 1);
            int y1 = Math.Clamp((int)Math.Round(Math.Max(y0d, y1d)), 0, height - 1);
            if (x1 <= x0 || y1 <= y0) return 0;

            int on = 0, total = 0;
            for (int y = y0; y <= y1; y += step)
            for (int x = x0; x <= x1; x += step)
            {
                total++;
                if (mask[(y * width) + x]) on++;
            }
            return total == 0 ? 0 : on / (double)total;
        }

        private static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);

        private static LineModel RobustFitYFromX(List<PointD> points)
        {
            LineModel first = FitYFromX(points);
            List<double> residuals = points.Select(p => Math.Abs(p.Y - ((first.Slope * p.X) + first.Intercept))).ToList();
            double cutoff = Math.Max(2.5, Percentile(residuals, 0.75) * 1.7);
            List<PointD> filtered = points
                .Where(p => Math.Abs(p.Y - ((first.Slope * p.X) + first.Intercept)) <= cutoff)
                .ToList();
            return filtered.Count >= 12 ? FitYFromX(filtered) : first;
        }

        private static LineModel FitYFromX(List<PointD> points)
        {
            double meanX = points.Average(p => p.X);
            double meanY = points.Average(p => p.Y);
            double numerator = 0;
            double denominator = 0;
            foreach (PointD p in points)
            {
                double dx = p.X - meanX;
                numerator += dx * (p.Y - meanY);
                denominator += dx * dx;
            }
            double slope = Math.Abs(denominator) < 1e-8 ? 0 : numerator / denominator;
            return new LineModel(slope, meanY - (slope * meanX));
        }

        private static double CalculateCaptureQuality(Rgba32[] pixels, bool[] mask, int width, int height, CornerGeometry geometry)
        {
            List<double> gradients = new();
            int x0 = Math.Clamp((int)(geometry.IdealX - geometry.Radius * 0.2), 1, width - 2);
            int y0 = Math.Clamp((int)(geometry.IdealY - geometry.Radius * 0.2), 1, height - 2);
            int x1 = Math.Clamp((int)(geometry.IdealX + geometry.Radius * 1.4), 1, width - 2);
            int y1 = Math.Clamp((int)(geometry.IdealY + geometry.Radius * 1.4), 1, height - 2);

            for (int y = y0; y <= y1; y += 2)
            for (int x = x0; x <= x1; x += 2)
            {
                if (!mask[(y * width) + x]) continue;
                double gx = Math.Abs(Luma(pixels[(y * width) + x + 1]) - Luma(pixels[(y * width) + x - 1]));
                double gy = Math.Abs(Luma(pixels[((y + 1) * width) + x]) - Luma(pixels[((y - 1) * width) + x]));
                gradients.Add(Math.Sqrt((gx * gx) + (gy * gy)));
            }

            double sharpness = gradients.Count == 0 ? 0 : Percentile(gradients, 0.85);
            return Math.Clamp(sharpness * 420.0, 0, 100);
        }

        private static CornerDefectMetrics AnalyzeDefects(
            Rgba32[] pixels,
            bool[] mask,
            int width,
            int height,
            CornerGeometry geometry)
        {
            double radius = geometry.Radius;
            int x0 = Math.Clamp((int)Math.Floor(geometry.IdealX - radius * 0.20), 1, width - 2);
            int y0 = Math.Clamp((int)Math.Floor(geometry.IdealY - radius * 0.20), 1, height - 2);
            int x1 = Math.Clamp((int)Math.Ceiling(geometry.IdealX + radius), 1, width - 2);
            int y1 = Math.Clamp((int)Math.Ceiling(geometry.IdealY + radius), 1, height - 2);

            double nearestDistance = double.MaxValue;
            int expectedCount = 0;
            int missingCount = 0;
            List<double> edgeWhitening = new();
            List<double> localTexture = new();

            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                double topY = (geometry.TopSlope * x) + geometry.TopIntercept;
                double leftX = (geometry.LeftSlopeXFromY * y) + geometry.LeftInterceptXFromY;
                bool expectedInside = y >= topY && x >= leftX;
                if (!expectedInside) continue;

                double fromCorner = Math.Sqrt(((x - geometry.IdealX) * (x - geometry.IdealX)) + ((y - geometry.IdealY) * (y - geometry.IdealY)));
                if (fromCorner > radius) continue;

                expectedCount++;
                bool isCard = mask[(y * width) + x];
                if (!isCard) missingCount++;
                else nearestDistance = Math.Min(nearestDistance, fromCorner);

                if (!isCard) continue;

                bool boundary = !mask[(y * width) + x - 1] || !mask[(y * width) + x + 1] ||
                                !mask[((y - 1) * width) + x] || !mask[((y + 1) * width) + x];
                if (!boundary) continue;

                int inwardX = Math.Clamp(x + 14, 0, width - 1);
                int inwardY = Math.Clamp(y + 14, 0, height - 1);
                Rgba32 edge = pixels[(y * width) + x];
                Rgba32 inner = pixels[(inwardY * width) + inwardX];
                double bright = Math.Max(0, Luma(edge) - Luma(inner));
                double lowChroma = Math.Max(0, Chroma(inner) - Chroma(edge));
                edgeWhitening.Add(Math.Clamp((bright * 1.8) + (lowChroma * 0.8), 0, 1));

                double gx = Math.Abs(Luma(pixels[(y * width) + x + 1]) - Luma(pixels[(y * width) + x - 1]));
                double gy = Math.Abs(Luma(pixels[((y + 1) * width) + x]) - Luma(pixels[((y - 1) * width) + x]));
                localTexture.Add(Math.Sqrt((gx * gx) + (gy * gy)));
            }

            double missingFraction = expectedCount == 0 ? 0 : missingCount / (double)expectedCount;
            double cornerGap = nearestDistance == double.MaxValue ? radius : nearestDistance;
            double normalizedGap = Math.Clamp(cornerGap / Math.Max(1, radius * 0.42), 0, 1);

            List<double> topResidual = geometry.TopBoundary
                .Where(p => p.X >= geometry.IdealX && p.X <= geometry.IdealX + radius)
                .Select(p => Math.Abs(p.Y - ((geometry.TopSlope * p.X) + geometry.TopIntercept)))
                .ToList();
            List<double> leftResidual = geometry.LeftBoundary
                .Where(p => p.Y >= geometry.IdealY && p.Y <= geometry.IdealY + radius)
                .Select(p => Math.Abs(p.X - ((geometry.LeftSlopeXFromY * p.Y) + geometry.LeftInterceptXFromY)))
                .ToList();

            double roughTop = RobustHighFrequency(topResidual);
            double roughLeft = RobustHighFrequency(leftResidual);
            double rough = Math.Max(roughTop, roughLeft);
            double roughNorm = Math.Clamp(rough / Math.Max(2.0, radius * 0.055), 0, 1);

            double whitening = edgeWhitening.Count == 0 ? 0 : Percentile(edgeWhitening, 0.88);
            double texture = localTexture.Count == 0 ? 0 : Percentile(localTexture, 0.90);
            double textureNorm = Math.Clamp((texture - 0.035) / 0.18, 0, 1);

            double lineAngleChange = Math.Clamp((Math.Abs(geometry.TopSlope) + Math.Abs(geometry.LeftSlopeXFromY)) / 0.55, 0, 1);

            return new CornerDefectMetrics
            {
                ShapeLossSeverity = Math.Clamp((normalizedGap * 0.72) + (missingFraction * 1.3), 0, 1),
                FrayingSeverity = Math.Clamp((roughNorm * 0.78) + (textureNorm * 0.22), 0, 1),
                WhiteningSeverity = Math.Clamp(whitening * 1.25, 0, 1),
                CrushBendSeverity = Math.Clamp((textureNorm * 0.52) + (roughNorm * 0.30) + (lineAngleChange * 0.18), 0, 1),
                MissingMaterialSeverity = Math.Clamp((missingFraction * 1.65) + (normalizedGap * 0.28), 0, 1),
                DeformationSeverity = Math.Clamp((roughNorm * 0.50) + (textureNorm * 0.35) + (lineAngleChange * 0.15), 0, 1)
            };
        }

        private static double RobustHighFrequency(List<double> residuals)
        {
            if (residuals.Count < 4) return 0;
            residuals.Sort();
            double median = residuals[residuals.Count / 2];
            List<double> deviations = residuals.Select(v => Math.Abs(v - median)).ToList();
            return Percentile(deviations, 0.88);
        }

        private static double CalculateOverallCondition(CornerDefectMetrics m)
        {
            double penalty =
                (m.ShapeLossSeverity * 24) +
                (m.FrayingSeverity * 14) +
                (m.WhiteningSeverity * 18) +
                (m.CrushBendSeverity * 16) +
                (m.MissingMaterialSeverity * 20) +
                (m.DeformationSeverity * 8);
            return Math.Clamp(100.0 - penalty, 0, 100);
        }

        private static double ToCondition(double severity) => Math.Clamp(100.0 - (severity * 100.0), 0, 100);

        private static string BuildSummary(CornerDefectMetrics m, double condition)
        {
            (string Name, double Value)[] values =
            {
                ("shape loss / rounding", m.ShapeLossSeverity),
                ("fraying / fuzzing", m.FrayingSeverity),
                ("whitening / chipping", m.WhiteningSeverity),
                ("crush / bend", m.CrushBendSeverity),
                ("missing material", m.MissingMaterialSeverity),
                ("localized deformation", m.DeformationSeverity)
            };

            (string Name, double Value) strongest = values.OrderByDescending(v => v.Value).First();
            string conditionLabel = condition >= 90 ? "Very clean visual corner" :
                                    condition >= 78 ? "Light wear candidate" :
                                    condition >= 62 ? "Moderate wear candidate" :
                                    "Strong damage candidate";

            return strongest.Value < 0.18
                ? $"{conditionLabel}. No strong defect family dominates this close-up."
                : $"{conditionLabel}. The strongest visual evidence is {strongest.Name}.";
        }

        private static string BuildMetricExplanation(string name, double severity, string method)
        {
            string level = severity < 0.18 ? "Low evidence" :
                           severity < 0.38 ? "Mild evidence" :
                           severity < 0.65 ? "Moderate evidence" :
                           "Strong evidence";
            return $"{level}. {method}";
        }

        private static void DrawAnalysisOverlay(Mat image, CornerGeometry g, CornerDefectMetrics metrics)
        {
            Scalar green = new(94, 197, 34);
            Scalar yellow = new(21, 204, 250);
            Scalar red = new(68, 68, 239);
            Scalar scoreColor = metrics.ShapeLossSeverity < 0.30 ? green : metrics.ShapeLossSeverity < 0.60 ? yellow : red;

            int xStart = Math.Clamp((int)Math.Round(g.IdealX), 0, image.Width - 1);
            int xEnd = Math.Clamp((int)Math.Round(g.IdealX + g.Radius * 1.7), 0, image.Width - 1);
            int yStart = Math.Clamp((int)Math.Round(g.IdealY), 0, image.Height - 1);
            int yEnd = Math.Clamp((int)Math.Round(g.IdealY + g.Radius * 1.7), 0, image.Height - 1);

            Cv2.Line(image,
                new OpenCvSharp.Point(xStart, (int)Math.Round((g.TopSlope * xStart) + g.TopIntercept)),
                new OpenCvSharp.Point(xEnd, (int)Math.Round((g.TopSlope * xEnd) + g.TopIntercept)),
                green, 4, LineTypes.AntiAlias);
            Cv2.Line(image,
                new OpenCvSharp.Point((int)Math.Round((g.LeftSlopeXFromY * yStart) + g.LeftInterceptXFromY), yStart),
                new OpenCvSharp.Point((int)Math.Round((g.LeftSlopeXFromY * yEnd) + g.LeftInterceptXFromY), yEnd),
                green, 4, LineTypes.AntiAlias);
            Cv2.Circle(image,
                new OpenCvSharp.Point((int)Math.Round(g.IdealX), (int)Math.Round(g.IdealY)),
                12, scoreColor, -1, LineTypes.AntiAlias);
        }

        private static double Luma(Rgba32 p) => ((0.2126 * p.R) + (0.7152 * p.G) + (0.0722 * p.B)) / 255.0;

        private static double Chroma(Rgba32 p)
        {
            double max = Math.Max(p.R, Math.Max(p.G, p.B));
            double min = Math.Min(p.R, Math.Min(p.G, p.B));
            return (max - min) / 255.0;
        }

        private static double Percentile(List<double> values, double p)
        {
            if (values.Count == 0) return 0;
            values.Sort();
            int index = Math.Clamp((int)Math.Round((values.Count - 1) * p), 0, values.Count - 1);
            return values[index];
        }

        private readonly record struct BackgroundModel(double R, double G, double B, double Spread);
        private readonly record struct PointD(double X, double Y);
        private readonly record struct LineModel(double Slope, double Intercept);

        private sealed class CornerGeometry
        {
            public double IdealX { get; set; }
            public double IdealY { get; set; }
            public double TopSlope { get; set; }
            public double TopIntercept { get; set; }
            public double LeftSlopeXFromY { get; set; }
            public double LeftInterceptXFromY { get; set; }
            public double Radius { get; set; }
            public double GeometryConfidence { get; set; }
            public List<PointD> TopBoundary { get; set; } = new();
            public List<PointD> LeftBoundary { get; set; } = new();
        }

        private sealed class CornerDefectMetrics
        {
            public double ShapeLossSeverity { get; set; }
            public double FrayingSeverity { get; set; }
            public double WhiteningSeverity { get; set; }
            public double CrushBendSeverity { get; set; }
            public double MissingMaterialSeverity { get; set; }
            public double DeformationSeverity { get; set; }
        }
    }

    public sealed class SingleCornerInspectionResult
    {
        public CornerPosition Corner { get; set; }
        public double OverallConditionScore { get; set; }
        public double ShapeConditionScore { get; set; }
        public double FrayingConditionScore { get; set; }
        public double WhiteningConditionScore { get; set; }
        public double CrushBendConditionScore { get; set; }
        public double MissingMaterialConditionScore { get; set; }
        public double DeformationConditionScore { get; set; }
        public double CaptureQuality { get; set; }
        public double GeometryConfidence { get; set; }
        public string NormalizedImagePath { get; set; } = string.Empty;
        public string AnalysisOverlayPath { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string ShapeExplanation { get; set; } = string.Empty;
        public string FrayingExplanation { get; set; } = string.Empty;
        public string WhiteningExplanation { get; set; } = string.Empty;
        public string CrushBendExplanation { get; set; } = string.Empty;
        public string MissingMaterialExplanation { get; set; } = string.Empty;
        public string DeformationExplanation { get; set; } = string.Empty;
    }
}
