using CollectIQ.Interfaces;
using CollectIQ.Models.Inspection.Geometry;
using OpenCvSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Runtime.InteropServices;
using ImageSharpResizeMode = SixLabors.ImageSharp.Processing.ResizeMode;
using ImageSharpSize = SixLabors.ImageSharp.Size;
using CvPoint = OpenCvSharp.Point;
using CvRect = OpenCvSharp.Rect;
using CvSize = OpenCvSharp.Size;

namespace CollectIQ.Services.Inspection.Geometry
{
    /// <summary>
    /// Finds the physical outside perimeter of a trading card using the same
    /// OpenCV GrabCut -> external contour -> polygon approximation pipeline
    /// that was proven against the Reed Bailey test photograph before this
    /// implementation was added to the MAUI application.
    ///
    /// IMPORTANT:
    /// - This service does not use printed artwork/borders as card geometry.
    /// - GrabCut separates the physical foreground object from its background.
    /// - Only the OUTERMOST contour is considered for the four card sides.
    /// - If a corner is rounded/damaged, four robust side lines are fitted to
    ///   the contour and their mathematical intersections are used.
    /// - Returned corners are ordered for a portrait 2.5 x 3.5 card so a
    ///   landscape camera buffer cannot accidentally produce a sideways warp.
    /// </summary>
    public sealed class CardGeometryService : ICardGeometryService
    {
        private const int DetectionMaximumDimension = 900;
        private const double ExpectedCardRatio = 2.5 / 3.5;
        private const double MinimumForegroundAreaFraction = 0.02;
        private const double MaximumForegroundAreaFraction = 0.80;

        // GrabCut labels are fixed by OpenCV:
        // 0 = background, 1 = foreground, 2 = probable background,
        // 3 = probable foreground.
        private const byte GrabBackground = 0;
        private const byte GrabForeground = 1;
        private const byte GrabProbableBackground = 2;
        private const byte GrabProbableForeground = 3;

        public CardGeometryResult DetectCard(SixLabors.ImageSharp.Image<Rgba32> source)
        {
            return DetectCardInternal(source, null);
        }

        /// <summary>
        /// Uses the exact same real-OpenCV foreground detector as DetectCard.
        /// The neutral-reference corners are only a scoring/seed hint; they are
        /// never treated as the new image's corners because the handheld phone
        /// is allowed to translate, rotate, scale and change perspective.
        /// </summary>
        public CardGeometryResult DetectCardNearPrior(
            SixLabors.ImageSharp.Image<Rgba32> source,
            IReadOnlyList<CardPoint> normalizedPriorCorners)
        {
            return DetectCardInternal(source, normalizedPriorCorners);
        }

        private static CardGeometryResult DetectCardInternal(
            SixLabors.ImageSharp.Image<Rgba32> source,
            IReadOnlyList<CardPoint>? normalizedPriorCorners)
        {
            try
            {
                float scale = Math.Min(1.0f, DetectionMaximumDimension / (float)Math.Max(source.Width, source.Height));
                int detectionWidth = Math.Max(180, (int)Math.Round(source.Width * scale));
                int detectionHeight = Math.Max(180, (int)Math.Round(source.Height * scale));

                using SixLabors.ImageSharp.Image<Rgba32> detectionImage = source.Clone(context =>
                    context.Resize(new ResizeOptions
                    {
                        Size = new ImageSharpSize(detectionWidth, detectionHeight),
                        Mode = ImageSharpResizeMode.Stretch,
                        Sampler = KnownResamplers.Bicubic
                    }));

                using Mat bgr = CreateBgrMat(detectionImage);
                if (!TryDetectRectangleByDirectionalScan(bgr, detectionWidth, detectionHeight, out CardPoint[] corners, out double confidence))
                    return Failed(source);

                return new CardGeometryResult
                {
                    Success = true,
                    Corners = corners.Select(pt => new CardPoint(pt.X / scale, pt.Y / scale)).ToArray(),
                    Confidence = confidence,
                    SourceWidth = source.Width,
                    SourceHeight = source.Height
                };
            }
            catch
            {
                return Failed(source);
            }
        }

        private static bool TryDetectRectangleByDirectionalScan(
            Mat bgr,
            int width,
            int height,
            out CardPoint[] ordered,
            out double confidence)
        {
            ordered = Array.Empty<CardPoint>();
            confidence = 0.0;

            using Mat gray = new();
            using Mat blurred = new();
            using Mat sx16 = new();
            using Mat sy16 = new();
            using Mat sx = new();
            using Mat sy = new();
            using Mat bx = new();
            using Mat by = new();

            Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.GaussianBlur(gray, blurred, new CvSize(5, 5), 1.0);
            Cv2.Sobel(blurred, sx16, MatType.CV_16S, 1, 0, 3);
            Cv2.Sobel(blurred, sy16, MatType.CV_16S, 0, 1, 3);
            Cv2.ConvertScaleAbs(sx16, sx);
            Cv2.ConvertScaleAbs(sy16, sy);

            double tx = Math.Max(38.0, Cv2.Threshold(sx, bx, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu));
            double ty = Math.Max(38.0, Cv2.Threshold(sy, by, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu));
            Cv2.Threshold(sx, bx, tx, 255, ThresholdTypes.Binary);
            Cv2.Threshold(sy, by, ty, 255, ThresholdTypes.Binary);

            using Mat hk = Cv2.GetStructuringElement(MorphShapes.Rect, new CvSize(9, 1));
            using Mat vk = Cv2.GetStructuringElement(MorphShapes.Rect, new CvSize(1, 9));
            Cv2.MorphologyEx(by, by, MorphTypes.Close, hk, iterations: 1);
            Cv2.MorphologyEx(bx, bx, MorphTypes.Close, vk, iterations: 1);

            List<ScanPoint> topRaw = ScanHorizontalSide(by, width, height, true);
            List<ScanPoint> bottomRaw = ScanHorizontalSide(by, width, height, false);
            List<ScanPoint> leftRaw = ScanVerticalSide(bx, width, height, true);
            List<ScanPoint> rightRaw = ScanVerticalSide(bx, width, height, false);

            if (!TrySelectDominantBand(topRaw, true, height, out List<ScanPoint> topPoints) ||
                !TrySelectDominantBand(bottomRaw, true, height, out List<ScanPoint> bottomPoints) ||
                !TrySelectDominantBand(leftRaw, false, width, out List<ScanPoint> leftPoints) ||
                !TrySelectDominantBand(rightRaw, false, width, out List<ScanPoint> rightPoints))
                return false;

            if (!TryFitLine(topPoints, true, out ScanLine top) ||
                !TryFitLine(bottomPoints, true, out ScanLine bottom) ||
                !TryFitLine(leftPoints, false, out ScanLine left) ||
                !TryFitLine(rightPoints, false, out ScanLine right))
                return false;

            double cx = (width - 1) * 0.5;
            double cy = (height - 1) * 0.5;
            if (top.Evaluate(cx) >= bottom.Evaluate(cx) || left.Evaluate(cy) >= right.Evaluate(cy))
                return false;

            CardPoint tl = IntersectHorizontalVertical(top, left);
            CardPoint tr = IntersectHorizontalVertical(top, right);
            CardPoint br = IntersectHorizontalVertical(bottom, right);
            CardPoint bl = IntersectHorizontalVertical(bottom, left);
            CardPoint[] c = { tl, tr, br, bl };
            if (!ValidateScannedCard(c, width, height))
                return false;

            double ratio = ((Distance(tl, tr) + Distance(bl, br)) * 0.5) /
                           Math.Max((Distance(tl, bl) + Distance(tr, br)) * 0.5, 1.0);
            double ratioScore = Math.Clamp(1.0 - (Math.Abs(ratio - ExpectedCardRatio) / 0.12), 0.0, 1.0);
            double support = Math.Clamp((topPoints.Count + bottomPoints.Count + leftPoints.Count + rightPoints.Count) /
                                        Math.Max((width + height) / 3.0, 1.0), 0.0, 1.0);
            ordered = c;
            confidence = Math.Clamp(0.72 + ratioScore * 0.18 + support * 0.10, 0.72, 0.995);
            return true;
        }

        private static List<ScanPoint> ScanHorizontalSide(Mat map, int width, int height, bool fromTop)
        {
            List<ScanPoint> points = new();
            int step = Math.Max(2, width / 320);
            int x0 = Math.Max(8, (int)(width * 0.06));
            int x1 = Math.Min(width - 9, (int)(width * 0.94));
            int y0 = Math.Max(5, (int)(height * 0.03));
            int y1 = Math.Min(height - 6, (int)(height * 0.97));
            for (int x = x0; x <= x1; x += step)
            {
                if (fromTop)
                {
                    for (int y = y0; y <= (int)(height * 0.58); y++)
                        if (HasHorizontalSupport(map, x, y, width)) { points.Add(new ScanPoint(x, y)); break; }
                }
                else
                {
                    for (int y = y1; y >= (int)(height * 0.42); y--)
                        if (HasHorizontalSupport(map, x, y, width)) { points.Add(new ScanPoint(x, y)); break; }
                }
            }
            return points;
        }

        private static List<ScanPoint> ScanVerticalSide(Mat map, int width, int height, bool fromLeft)
        {
            List<ScanPoint> points = new();
            int step = Math.Max(2, height / 420);
            int y0 = Math.Max(8, (int)(height * 0.06));
            int y1 = Math.Min(height - 9, (int)(height * 0.94));
            int x0 = Math.Max(5, (int)(width * 0.03));
            int x1 = Math.Min(width - 6, (int)(width * 0.97));
            for (int y = y0; y <= y1; y += step)
            {
                if (fromLeft)
                {
                    for (int x = x0; x <= (int)(width * 0.58); x++)
                        if (HasVerticalSupport(map, x, y, height)) { points.Add(new ScanPoint(x, y)); break; }
                }
                else
                {
                    for (int x = x1; x >= (int)(width * 0.42); x--)
                        if (HasVerticalSupport(map, x, y, height)) { points.Add(new ScanPoint(x, y)); break; }
                }
            }
            return points;
        }

        private static bool HasHorizontalSupport(Mat map, int x, int y, int width)
        {
            int hits = 0;
            for (int dx = -5; dx <= 5; dx++)
                if (map.At<byte>(y, Math.Clamp(x + dx, 0, width - 1)) != 0) hits++;
            return hits >= 6;
        }

        private static bool HasVerticalSupport(Mat map, int x, int y, int height)
        {
            int hits = 0;
            for (int dy = -5; dy <= 5; dy++)
                if (map.At<byte>(Math.Clamp(y + dy, 0, height - 1), x) != 0) hits++;
            return hits >= 6;
        }

        private static bool TrySelectDominantBand(List<ScanPoint> raw, bool useY, int axisLength, out List<ScanPoint> selected)
        {
            selected = new();
            if (raw.Count < 18) return false;
            int binSize = Math.Max(4, axisLength / 150);
            Dictionary<int, int> bins = new();
            foreach (ScanPoint p in raw)
            {
                int v = (int)Math.Round(useY ? p.Y : p.X);
                int b = v / binSize;
                bins[b] = bins.TryGetValue(b, out int n) ? n + 1 : 1;
            }
            int best = bins.OrderByDescending(k => k.Value).First().Key;
            double center = (best + 0.5) * binSize;
            double tol = Math.Max(10.0, axisLength * 0.025);
            selected = raw.Where(p => Math.Abs((useY ? p.Y : p.X) - center) <= tol).ToList();
            return selected.Count >= Math.Max(14, raw.Count / 5);
        }

        private static bool TryFitLine(List<ScanPoint> source, bool horizontal, out ScanLine line)
        {
            line = default;
            List<ScanPoint> points = source.ToList();
            if (points.Count < 12) return false;
            for (int iter = 0; iter < 3; iter++)
            {
                double ma = horizontal ? points.Average(p => p.X) : points.Average(p => p.Y);
                double mb = horizontal ? points.Average(p => p.Y) : points.Average(p => p.X);
                double num = 0, den = 0;
                foreach (ScanPoint p in points)
                {
                    double a = horizontal ? p.X : p.Y;
                    double b = horizontal ? p.Y : p.X;
                    num += (a - ma) * (b - mb);
                    den += (a - ma) * (a - ma);
                }
                if (den < 1e-6) return false;
                double m = num / den;
                double q = mb - m * ma;
                double[] r = points.Select(p => Math.Abs((horizontal ? p.Y : p.X) - (m * (horizontal ? p.X : p.Y) + q))).OrderBy(v => v).ToArray();
                double med = r[r.Length / 2];
                double tol = Math.Max(2.5, med * 2.8 + 1.5);
                List<ScanPoint> f = points.Where(p => Math.Abs((horizontal ? p.Y : p.X) - (m * (horizontal ? p.X : p.Y) + q)) <= tol).ToList();
                line = new ScanLine(m, q);
                if (f.Count == points.Count || f.Count < 12) break;
                points = f;
            }
            return points.Count >= 12;
        }

        private static CardPoint IntersectHorizontalVertical(ScanLine h, ScanLine v)
        {
            double d = 1.0 - v.Slope * h.Slope;
            if (Math.Abs(d) < 1e-7) d = d < 0 ? -1e-7 : 1e-7;
            double x = (v.Slope * h.Intercept + v.Intercept) / d;
            double y = h.Slope * x + h.Intercept;
            return new CardPoint((float)x, (float)y);
        }

        private static bool ValidateScannedCard(CardPoint[] c, int width, int height)
        {
            double margin = Math.Max(width, height) * 0.025;
            if (c.Length != 4 || c.Any(p => p.X < margin || p.Y < margin || p.X > width - margin || p.Y > height - margin)) return false;
            double top = Distance(c[0], c[1]), right = Distance(c[1], c[2]), bottom = Distance(c[3], c[2]), left = Distance(c[0], c[3]);
            double mw = (top + bottom) * 0.5, mh = (left + right) * 0.5;
            if (mw < width * 0.20 || mh < height * 0.28) return false;
            double ratio = mw / Math.Max(mh, 1.0);
            if (ratio < 0.60 || ratio > 0.83) return false;
            if (Math.Max(top, bottom) / Math.Max(Math.Min(top, bottom), 1.0) > 1.30) return false;
            if (Math.Max(left, right) / Math.Max(Math.Min(left, right), 1.0) > 1.30) return false;
            double area = 0;
            for (int i = 0; i < 4; i++) { CardPoint a = c[i], b = c[(i + 1) % 4]; area += a.X * b.Y - b.X * a.Y; }
            double fraction = Math.Abs(area) * 0.5 / Math.Max(width * (double)height, 1.0);
            return fraction >= 0.10 && fraction <= 0.78;
        }

        private readonly record struct ScanPoint(double X, double Y);
        private readonly record struct ScanLine(double Slope, double Intercept)
        {
            public double Evaluate(double axis) => Slope * axis + Intercept;
        }

        private static bool TryDetectCardFromBackgroundContrast(
            Mat bgr,
            int width,
            int height,
            IReadOnlyList<CardPoint>? normalizedPriorCorners,
            out CardPoint[] ordered,
            out double confidence)
        {
            ordered = Array.Empty<CardPoint>();
            confidence = 0.0;

            if (width < 80 || height < 120 || !bgr.IsContinuous())
                return false;

            int pixelCount = width * height;
            byte[] pixels = new byte[pixelCount * 3];
            Marshal.Copy(bgr.Data, pixels, 0, pixels.Length);

            int rimX = Math.Max(4, (int)Math.Round(width * 0.055));
            int rimY = Math.Max(4, (int)Math.Round(height * 0.055));

            double sumB = 0, sumG = 0, sumR = 0;
            long sampleCount = 0;

            // Sample the full outer rim. The capture instructions require the card to be
            // completely inside the frame, so this region should overwhelmingly be background.
            for (int y = 0; y < height; y += 2)
            {
                for (int x = 0; x < width; x += 2)
                {
                    if (x >= rimX && x < width - rimX && y >= rimY && y < height - rimY)
                        continue;

                    int i = ((y * width) + x) * 3;
                    sumB += pixels[i];
                    sumG += pixels[i + 1];
                    sumR += pixels[i + 2];
                    sampleCount++;
                }
            }

            if (sampleCount < 20)
                return false;

            double meanB = sumB / sampleCount;
            double meanG = sumG / sampleCount;
            double meanR = sumR / sampleCount;

            // Measure natural background variation so shadows/noise in the mat do not become card.
            double distanceSum = 0.0;
            double distanceSqSum = 0.0;
            long distanceCount = 0;
            for (int y = 0; y < height; y += 3)
            {
                for (int x = 0; x < width; x += 3)
                {
                    if (x >= rimX && x < width - rimX && y >= rimY && y < height - rimY)
                        continue;

                    int i = ((y * width) + x) * 3;
                    double db = pixels[i] - meanB;
                    double dg = pixels[i + 1] - meanG;
                    double dr = pixels[i + 2] - meanR;
                    double d = Math.Sqrt((db * db) + (dg * dg) + (dr * dr));
                    distanceSum += d;
                    distanceSqSum += d * d;
                    distanceCount++;
                }
            }

            double meanDistance = distanceCount > 0 ? distanceSum / distanceCount : 0.0;
            double variance = distanceCount > 0
                ? Math.Max(0.0, (distanceSqSum / distanceCount) - (meanDistance * meanDistance))
                : 0.0;
            double stdDistance = Math.Sqrt(variance);

            // Adaptive threshold, clamped so a very clean mat does not become oversensitive
            // and a slightly textured/shadowed mat does not become impossible.
            double threshold = Math.Clamp(meanDistance + (stdDistance * 2.6) + 14.0, 24.0, 88.0);

            byte[] maskBytes = new byte[pixelCount];
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int p = row + x;
                    int i = p * 3;
                    double db = pixels[i] - meanB;
                    double dg = pixels[i + 1] - meanG;
                    double dr = pixels[i + 2] - meanR;
                    double distance = Math.Sqrt((db * db) + (dg * dg) + (dr * dr));
                    maskBytes[p] = distance >= threshold ? (byte)255 : (byte)0;
                }
            }

            using Mat rawMask = new(height, width, MatType.CV_8UC1);
            Marshal.Copy(maskBytes, 0, rawMask.Data, maskBytes.Length);

            using Mat closed = new();
            using Mat cleaned = new();
            using Mat closeKernel = Cv2.GetStructuringElement(
                MorphShapes.Rect,
                new CvSize(Math.Max(5, (width / 180) | 1), Math.Max(5, (height / 240) | 1)));
            using Mat openKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new CvSize(5, 5));

            Cv2.MorphologyEx(rawMask, closed, MorphTypes.Close, closeKernel, iterations: 3);
            Cv2.MorphologyEx(closed, cleaned, MorphTypes.Open, openKernel, iterations: 1);

            Cv2.FindContours(
                cleaned,
                out CvPoint[][] contours,
                out HierarchyIndex[] _,
                RetrievalModes.External,
                ContourApproximationModes.ApproxNone);

            double imageArea = Math.Max(width * (double)height, 1.0);
            double bestScore = double.NegativeInfinity;
            CardPoint[]? best = null;

            foreach (CvPoint[] contour in contours)
            {
                if (contour.Length < 40)
                    continue;

                double area = Math.Abs(Cv2.ContourArea(contour));
                double areaFraction = area / imageArea;
                if (areaFraction < 0.07 || areaFraction > 0.82)
                    continue;

                CvRect bounds = Cv2.BoundingRect(contour);
                if (bounds.Width < width * 0.24 || bounds.Height < height * 0.30)
                    continue;

                // A valid card may approach the rim, but it should not actually be clipped by it.
                const int safeRim = 2;
                if (bounds.Left <= safeRim || bounds.Top <= safeRim ||
                    bounds.Right >= width - safeRim || bounds.Bottom >= height - safeRim)
                    continue;

                CardPoint[]? candidate = null;

                // For rounded corners, side-line fitting is preferred: it ignores the curved
                // corner regions and uses the long straight outside perimeter.
                CvPoint[] hull = Cv2.ConvexHull(contour);
                if (TryFitFourSideIntersections(hull, out CardPoint[] fitted))
                {
                    candidate = fitted;
                }
                else
                {
                    CvPoint[]? quad = ApproximateFourCorners(hull);
                    if (quad is not null)
                        candidate = quad.Select(p => new CardPoint(p.X, p.Y)).ToArray();
                }

                if (candidate is null ||
                    !TryOrderCardCornersForPortrait(candidate, out CardPoint[] candidateOrdered) ||
                    !ValidateQuadrilateral(candidateOrdered, width, height))
                    continue;

                double top = Distance(candidateOrdered[0], candidateOrdered[1]);
                double bottom = Distance(candidateOrdered[3], candidateOrdered[2]);
                double left = Distance(candidateOrdered[0], candidateOrdered[3]);
                double right = Distance(candidateOrdered[1], candidateOrdered[2]);
                double meanWidth = (top + bottom) * 0.5;
                double meanHeight = (left + right) * 0.5;
                double ratio = meanWidth / Math.Max(meanHeight, 1.0);
                double ratioError = Math.Abs(ratio - ExpectedCardRatio);

                double centerX = candidateOrdered.Average(p => p.X) / Math.Max(width - 1.0, 1.0);
                double centerY = candidateOrdered.Average(p => p.Y) / Math.Max(height - 1.0, 1.0);
                double centerDistance = Math.Sqrt(
                    Math.Pow(centerX - 0.5, 2) +
                    Math.Pow(centerY - 0.5, 2));

                double priorScore = 0.5;
                if (normalizedPriorCorners is not null && normalizedPriorCorners.Count == 4)
                {
                    double total = 0.0;
                    for (int c = 0; c < 4; c++)
                    {
                        double nx = candidateOrdered[c].X / Math.Max(width - 1.0, 1.0);
                        double ny = candidateOrdered[c].Y / Math.Max(height - 1.0, 1.0);
                        double dx = nx - normalizedPriorCorners[c].X;
                        double dy = ny - normalizedPriorCorners[c].Y;
                        total += Math.Sqrt((dx * dx) + (dy * dy));
                    }
                    priorScore = Math.Clamp(1.0 - ((total / 4.0) / 0.22), 0.0, 1.0);
                }

                double ratioScore = Math.Clamp(1.0 - (ratioError / 0.22), 0.0, 1.0);
                double areaScore = Math.Clamp(areaFraction / 0.38, 0.0, 1.0);
                double centeredScore = Math.Clamp(1.0 - (centerDistance / 0.36), 0.0, 1.0);

                // Foreground AREA dominates: an internal printed rectangle simply cannot have
                // the same segmented foreground silhouette as the whole physical card.
                double score =
                    (areaScore * 3.0) +
                    (ratioScore * 2.2) +
                    (centeredScore * 0.8) +
                    (priorScore * 1.0);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidateOrdered;
                }
            }

            if (best is null)
                return false;

            ordered = best;
            confidence = Math.Clamp(0.68 + (bestScore / 18.0), 0.68, 0.995);
            return true;
        }

        private static bool TryDetectCardFromOuterLines(
            Mat bgr,
            int width,
            int height,
            IReadOnlyList<CardPoint>? normalizedPriorCorners,
            out CardPoint[] ordered,
            out double confidence)
        {
            ordered = Array.Empty<CardPoint>();
            confidence = 0.0;

            using Mat gray = new();
            using Mat blurred = new();
            using Mat edges = new();
            using Mat closed = new();

            Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.GaussianBlur(gray, blurred, new CvSize(5, 5), 1.1);

            // Two Canny ranges are combined. Dark cards on light mats and bright cards
            // on dark mats can have very different perimeter contrast.
            using Mat edgesLow = new();
            using Mat edgesHigh = new();
            Cv2.Canny(blurred, edgesLow, 28, 92, 3, true);
            Cv2.Canny(blurred, edgesHigh, 62, 175, 3, true);
            Cv2.BitwiseOr(edgesLow, edgesHigh, edges);

            using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new CvSize(5, 5));
            Cv2.MorphologyEx(edges, closed, MorphTypes.Close, kernel, iterations: 2);

            int minDimension = Math.Min(width, height);
            LineSegmentPoint[] segments = Cv2.HoughLinesP(
                closed,
                1.0,
                Math.PI / 360.0,
                Math.Max(28, (int)Math.Round(minDimension * 0.055)),
                Math.Max(55.0, minDimension * 0.22),
                Math.Max(18.0, minDimension * 0.055));

            if (segments.Length < 4) return false;

            List<LineCandidate> top = new();
            List<LineCandidate> bottom = new();
            List<LineCandidate> left = new();
            List<LineCandidate> right = new();

            double rimX = width * 0.025;
            double rimY = height * 0.025;

            foreach (LineSegmentPoint segment in segments)
            {
                double dx = segment.P2.X - segment.P1.X;
                double dy = segment.P2.Y - segment.P1.Y;
                double length = Math.Sqrt((dx * dx) + (dy * dy));
                if (length < 30.0) continue;

                double midX = (segment.P1.X + segment.P2.X) * 0.5;
                double midY = (segment.P1.Y + segment.P2.Y) * 0.5;
                FittedLine line = new(
                    midX,
                    midY,
                    dx / length,
                    dy / length);

                // Keep perspective tolerance generous (~35 degrees), but reject diagonals
                // from artwork and text.
                if (Math.Abs(dx) >= Math.Abs(dy) * 1.35 && length >= width * 0.24)
                {
                    if (midY > rimY && midY < height * 0.58)
                        top.Add(new LineCandidate(line, midY, length));
                    if (midY < height - rimY && midY > height * 0.42)
                        bottom.Add(new LineCandidate(line, midY, length));
                }
                else if (Math.Abs(dy) >= Math.Abs(dx) * 1.35 && length >= height * 0.24)
                {
                    if (midX > rimX && midX < width * 0.58)
                        left.Add(new LineCandidate(line, midX, length));
                    if (midX < width - rimX && midX > width * 0.42)
                        right.Add(new LineCandidate(line, midX, length));
                }
            }

            // Outermost first, but retain several alternatives because a table seam or
            // sleeve edge can occasionally sit outside the card.
            LineCandidate[] tops = top.OrderBy(c => c.Position).ThenByDescending(c => c.Length).Take(10).ToArray();
            LineCandidate[] bottoms = bottom.OrderByDescending(c => c.Position).ThenByDescending(c => c.Length).Take(10).ToArray();
            LineCandidate[] lefts = left.OrderBy(c => c.Position).ThenByDescending(c => c.Length).Take(10).ToArray();
            LineCandidate[] rights = right.OrderByDescending(c => c.Position).ThenByDescending(c => c.Length).Take(10).ToArray();

            if (tops.Length == 0 || bottoms.Length == 0 || lefts.Length == 0 || rights.Length == 0)
                return false;

            double imageArea = Math.Max(width * (double)height, 1.0);
            double bestScore = double.NegativeInfinity;
            CardPoint[]? best = null;

            foreach (LineCandidate t in tops)
            foreach (LineCandidate b in bottoms)
            foreach (LineCandidate l in lefts)
            foreach (LineCandidate r in rights)
            {
                if (b.Position - t.Position < height * 0.30 || r.Position - l.Position < width * 0.25)
                    continue;

                if (!TryIntersect(t.Line, l.Line, out CardPoint tl) ||
                    !TryIntersect(t.Line, r.Line, out CardPoint tr) ||
                    !TryIntersect(b.Line, r.Line, out CardPoint br) ||
                    !TryIntersect(b.Line, l.Line, out CardPoint bl))
                    continue;

                CardPoint[] candidate = { tl, tr, br, bl };
                if (!ValidateQuadrilateral(candidate, width, height)) continue;

                double topLength = Distance(tl, tr);
                double bottomLength = Distance(bl, br);
                double leftLength = Distance(tl, bl);
                double rightLength = Distance(tr, br);
                double meanWidth = (topLength + bottomLength) * 0.5;
                double meanHeight = (leftLength + rightLength) * 0.5;
                if (meanWidth <= 1.0 || meanHeight <= 1.0) continue;

                double ratio = meanWidth / meanHeight;
                double ratioError = Math.Abs(ratio - ExpectedCardRatio);
                if (ratioError > 0.20) continue;

                double areaFraction = PolygonArea(candidate) / imageArea;
                double oppositeAgreement =
                    (1.0 - Math.Min(Math.Abs(topLength - bottomLength) / Math.Max(meanWidth, 1.0), 1.0) +
                     1.0 - Math.Min(Math.Abs(leftLength - rightLength) / Math.Max(meanHeight, 1.0), 1.0)) * 0.5;

                double parallelScore =
                    (Math.Abs((t.Line.Vx * b.Line.Vx) + (t.Line.Vy * b.Line.Vy)) +
                     Math.Abs((l.Line.Vx * r.Line.Vx) + (l.Line.Vy * r.Line.Vy))) * 0.5;

                double perpendicular = Math.Abs((t.Line.Vx * l.Line.Vx) + (t.Line.Vy * l.Line.Vy));
                double perpendicularScore = 1.0 - Math.Clamp(perpendicular, 0.0, 1.0);

                double support = Math.Clamp(
                    (t.Length + b.Length + l.Length + r.Length) /
                    Math.Max((meanWidth * 2.0) + (meanHeight * 2.0), 1.0),
                    0.0,
                    1.0);

                double priorScore = 0.5;
                if (normalizedPriorCorners is not null && normalizedPriorCorners.Count == 4)
                {
                    double total = 0.0;
                    for (int i = 0; i < 4; i++)
                    {
                        double nx = candidate[i].X / Math.Max(width - 1.0, 1.0);
                        double ny = candidate[i].Y / Math.Max(height - 1.0, 1.0);
                        double dxp = nx - normalizedPriorCorners[i].X;
                        double dyp = ny - normalizedPriorCorners[i].Y;
                        total += Math.Sqrt((dxp * dxp) + (dyp * dyp));
                    }
                    priorScore = Math.Clamp(1.0 - ((total / 4.0) / 0.18), 0.0, 1.0);
                }

                double ratioScore = Math.Clamp(1.0 - (ratioError / 0.20), 0.0, 1.0);
                double areaScore = Math.Clamp(areaFraction / 0.34, 0.0, 1.0);
                double score =
                    (areaScore * 2.5) +
                    (ratioScore * 2.2) +
                    (oppositeAgreement * 1.2) +
                    (parallelScore * 0.9) +
                    (perpendicularScore * 0.8) +
                    (support * 0.8) +
                    (priorScore * 1.0);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (best is null) return false;
            ordered = best;
            confidence = Math.Clamp(0.58 + (bestScore / 14.0), 0.58, 0.99);
            return true;
        }

        private static bool TryDetectCardFromEdges(
            Mat bgr,
            int width,
            int height,
            IReadOnlyList<CardPoint>? normalizedPriorCorners,
            out CardPoint[] ordered,
            out double confidence)
        {
            ordered = Array.Empty<CardPoint>();
            confidence = 0.0;

            using Mat gray = new();
            using Mat blurred = new();
            using Mat edges = new();
            using Mat closed = new();

            Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.GaussianBlur(gray, blurred, new CvSize(5, 5), 1.2);
            Cv2.Canny(blurred, edges, 45, 135, 3, true);

            using Mat kernel = Cv2.GetStructuringElement(
                MorphShapes.Rect,
                new CvSize(7, 7));
            Cv2.MorphologyEx(edges, closed, MorphTypes.Close, kernel, iterations: 2);

            Cv2.FindContours(
                closed,
                out CvPoint[][] contours,
                out HierarchyIndex[] _,
                RetrievalModes.List,
                ContourApproximationModes.ApproxSimple);

            double imageArea = Math.Max(width * (double)height, 1.0);
            double bestScore = double.NegativeInfinity;
            CardPoint[]? best = null;

            foreach (CvPoint[] contour in contours)
            {
                double area = Math.Abs(Cv2.ContourArea(contour));
                double areaFraction = area / imageArea;

                // The card should be a substantial object, but never almost the
                // entire camera frame. This removes most artwork and frame-rim candidates.
                if (areaFraction < 0.045 || areaFraction > 0.72)
                {
                    continue;
                }

                double perimeter = Cv2.ArcLength(contour, true);
                if (perimeter < 40)
                {
                    continue;
                }

                CvPoint[]? quad = null;
                for (double epsilon = 0.012; epsilon <= 0.055; epsilon += 0.006)
                {
                    CvPoint[] candidate = Cv2.ApproxPolyDP(
                        contour,
                        perimeter * epsilon,
                        true);

                    if (candidate.Length == 4 && Cv2.IsContourConvex(candidate))
                    {
                        quad = candidate;
                        break;
                    }
                }

                if (quad is null)
                {
                    RotatedRect box = Cv2.MinAreaRect(contour);
                    Point2f[] pts = box.Points();
                    quad = pts.Select(p => new CvPoint((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToArray();
                }

                CardPoint[] raw = quad.Select(p => new CardPoint(p.X, p.Y)).ToArray();
                if (!TryOrderCardCornersForPortrait(raw, out CardPoint[] candidateOrdered) ||
                    !ValidateQuadrilateral(candidateOrdered, width, height))
                {
                    continue;
                }

                double top = Distance(candidateOrdered[0], candidateOrdered[1]);
                double bottom = Distance(candidateOrdered[3], candidateOrdered[2]);
                double left = Distance(candidateOrdered[0], candidateOrdered[3]);
                double right = Distance(candidateOrdered[1], candidateOrdered[2]);
                double shortSide = (top + bottom) * 0.5;
                double longSide = (left + right) * 0.5;

                if (shortSide <= 1 || longSide <= 1)
                {
                    continue;
                }

                double ratio = Math.Min(shortSide, longSide) / Math.Max(shortSide, longSide);
                double ratioError = Math.Abs(ratio - ExpectedCardRatio);
                if (ratioError > 0.22)
                {
                    continue;
                }

                double rectangularity = area / Math.Max(shortSide * longSide, 1.0);
                rectangularity = Math.Clamp(rectangularity, 0.0, 1.0);

                double centerX = candidateOrdered.Average(p => p.X) / Math.Max(width - 1.0, 1.0);
                double centerY = candidateOrdered.Average(p => p.Y) / Math.Max(height - 1.0, 1.0);
                double centerPenalty = Math.Sqrt(
                    Math.Pow(centerX - 0.5, 2) +
                    Math.Pow(centerY - 0.5, 2));

                double priorBonus = 0.0;
                if (normalizedPriorCorners is not null && normalizedPriorCorners.Count == 4)
                {
                    double priorCenterX = normalizedPriorCorners.Average(p => p.X);
                    double priorCenterY = normalizedPriorCorners.Average(p => p.Y);
                    double priorDistance = Math.Sqrt(
                        Math.Pow(centerX - priorCenterX, 2) +
                        Math.Pow(centerY - priorCenterY, 2));
                    priorBonus = Math.Max(0.0, 1.0 - (priorDistance / 0.20)) * 0.40;
                }

                // Large physical perimeter + correct ratio + rectangularity wins.
                // The area term intentionally dominates so an inner printed border
                // does not beat the actual card edge.
                double score =
                    (areaFraction * 3.2) +
                    ((1.0 - Math.Min(ratioError / 0.22, 1.0)) * 1.6) +
                    (rectangularity * 1.1) -
                    (centerPenalty * 0.55) +
                    priorBonus;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidateOrdered;
                }
            }

            if (best is null)
            {
                return false;
            }

            ordered = best;
            confidence = Math.Clamp(0.55 + (bestScore / 8.0), 0.55, 1.0);
            return true;
        }

        /// <summary>
        /// Converts the ImageSharp RGBA image directly into an OpenCV BGR Mat.
        /// No temporary JPEG/PNG is created, so geometry is not affected by an
        /// additional compression pass.
        /// </summary>
        private static Mat CreateBgrMat(SixLabors.ImageSharp.Image<Rgba32> image)
        {
            byte[] rgbaBytes = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(rgbaBytes);

            using Mat rgba = new(image.Height, image.Width, MatType.CV_8UC4);
            Marshal.Copy(rgbaBytes, 0, rgba.Data, rgbaBytes.Length);

            Mat bgr = new();
            Cv2.CvtColor(rgba, bgr, ColorConversionCodes.RGBA2BGR);
            return bgr;
        }

        /// <summary>
        /// Reproduces the successful test setup: the outer image rim is
        /// definite background and a broad central region is probable
        /// foreground. A prior reference, when available, adds probable
        /// foreground support but never fixes the card to the old position.
        /// </summary>
        private static Mat CreateGrabCutSeed(
            int width,
            int height,
            IReadOnlyList<CardPoint>? normalizedPriorCorners)
        {
            byte[] labels = Enumerable.Repeat(
                GrabProbableBackground,
                width * height).ToArray();

            int centralLeft = (int)Math.Round(width * 0.08);
            int centralRight = (int)Math.Round(width * 0.92);
            int centralTop = (int)Math.Round(height * 0.08);
            int centralBottom = (int)Math.Round(height * 0.92);

            for (int y = centralTop; y < centralBottom; y++)
            {
                int row = y * width;
                for (int x = centralLeft; x < centralRight; x++)
                {
                    labels[row + x] = GrabProbableForeground;
                }
            }

            // A neutral-reference prior only adds a generous probable-card
            // region. It is deliberately expanded so normal handheld movement
            // between captures remains legal.
            if (normalizedPriorCorners is not null && normalizedPriorCorners.Count == 4)
            {
                float minX = normalizedPriorCorners.Min(point => point.X);
                float maxX = normalizedPriorCorners.Max(point => point.X);
                float minY = normalizedPriorCorners.Min(point => point.Y);
                float maxY = normalizedPriorCorners.Max(point => point.Y);

                float priorWidth = Math.Max(maxX - minX, 0.05f);
                float priorHeight = Math.Max(maxY - minY, 0.05f);
                minX -= priorWidth * 0.35f;
                maxX += priorWidth * 0.35f;
                minY -= priorHeight * 0.35f;
                maxY += priorHeight * 0.35f;

                int left = Math.Clamp((int)Math.Floor(minX * width), 0, width - 1);
                int right = Math.Clamp((int)Math.Ceiling(maxX * width), 1, width);
                int top = Math.Clamp((int)Math.Floor(minY * height), 0, height - 1);
                int bottom = Math.Clamp((int)Math.Ceiling(maxY * height), 1, height);

                for (int y = top; y < bottom; y++)
                {
                    int row = y * width;
                    for (int x = left; x < right; x++)
                    {
                        labels[row + x] = GrabProbableForeground;
                    }
                }
            }

            int rimX = Math.Max(2, (int)Math.Round(width * 0.04));
            int rimY = Math.Max(2, (int)Math.Round(height * 0.04));

            // The physical card must be completely visible. Therefore only the
            // extreme image rim is asserted as definite background.
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (x < rimX || x >= width - rimX || y < rimY || y >= height - rimY)
                    {
                        labels[row + x] = GrabBackground;
                    }
                }
            }

            Mat mask = new(height, width, MatType.CV_8UC1);
            Marshal.Copy(labels, 0, mask.Data, labels.Length);
            return mask;
        }

        private static Mat BuildBinaryForegroundMask(Mat grabMask)
        {
            int length = grabMask.Rows * grabMask.Cols;
            byte[] grab = new byte[length];
            Marshal.Copy(grabMask.Data, grab, 0, length);

            byte[] binary = new byte[length];
            for (int i = 0; i < length; i++)
            {
                binary[i] = grab[i] == GrabForeground || grab[i] == GrabProbableForeground
                    ? (byte)255
                    : (byte)0;
            }

            Mat result = new(grabMask.Rows, grabMask.Cols, MatType.CV_8UC1);
            Marshal.Copy(binary, 0, result.Data, binary.Length);
            return result;
        }

        private static Mat CleanForegroundMask(Mat foreground)
        {
            using Mat closeKernel = Cv2.GetStructuringElement(
                MorphShapes.Rect,
                new CvSize(11, 11));
            using Mat openKernel = Cv2.GetStructuringElement(
                MorphShapes.Rect,
                new CvSize(5, 5));

            Mat cleaned = new();
            Cv2.MorphologyEx(foreground, cleaned, MorphTypes.Close, closeKernel);
            Cv2.MorphologyEx(cleaned, cleaned, MorphTypes.Close, closeKernel);
            Cv2.MorphologyEx(cleaned, cleaned, MorphTypes.Open, openKernel);
            return cleaned;
        }

        private static CvPoint[]? SelectCardContour(
            IReadOnlyList<CvPoint[]> contours,
            int width,
            int height,
            IReadOnlyList<CardPoint>? normalizedPriorCorners)
        {
            if (contours.Count == 0)
            {
                return null;
            }

            double imageArea = width * (double)height;
            double bestScore = double.NegativeInfinity;
            CvPoint[]? best = null;

            foreach (CvPoint[] contour in contours)
            {
                double area = Math.Abs(Cv2.ContourArea(contour));
                double areaFraction = area / Math.Max(imageArea, 1.0);
                if (areaFraction < MinimumForegroundAreaFraction ||
                    areaFraction > MaximumForegroundAreaFraction)
                {
                    continue;
                }

                RotatedRect rectangle = Cv2.MinAreaRect(contour);
                double shortSide = Math.Min(rectangle.Size.Width, rectangle.Size.Height);
                double longSide = Math.Max(rectangle.Size.Width, rectangle.Size.Height);
                if (shortSide < 20.0 || longSide < 40.0)
                {
                    continue;
                }

                double ratio = shortSide / longSide;
                double ratioScore = Math.Exp(-Math.Pow((ratio - ExpectedCardRatio) / 0.22, 2.0));

                Moments moments = Cv2.Moments(contour);
                double centerX = Math.Abs(moments.M00) > 0.0001
                    ? moments.M10 / moments.M00
                    : rectangle.Center.X;
                double centerY = Math.Abs(moments.M00) > 0.0001
                    ? moments.M01 / moments.M00
                    : rectangle.Center.Y;

                double normalizedCenterDistance = Math.Sqrt(
                    Math.Pow((centerX - (width * 0.5)) / Math.Max(width, 1), 2.0) +
                    Math.Pow((centerY - (height * 0.5)) / Math.Max(height, 1), 2.0));
                double centerScore = Math.Clamp(1.0 - (normalizedCenterDistance * 1.7), 0.0, 1.0);

                double priorScore = 0.5;
                if (normalizedPriorCorners is not null && normalizedPriorCorners.Count == 4)
                {
                    double priorCenterX = normalizedPriorCorners.Average(point => point.X) * width;
                    double priorCenterY = normalizedPriorCorners.Average(point => point.Y) * height;
                    double priorDistance = Math.Sqrt(
                        Math.Pow((centerX - priorCenterX) / Math.Max(width, 1), 2.0) +
                        Math.Pow((centerY - priorCenterY) / Math.Max(height, 1), 2.0));
                    priorScore = Math.Clamp(1.0 - (priorDistance * 2.0), 0.0, 1.0);
                }

                // Area is intentionally important: after GrabCut, the card is
                // normally the largest plausible foreground object. The other
                // terms prevent a large glare/background island from winning.
                double areaScore = Math.Clamp(areaFraction / 0.32, 0.0, 1.0);
                double score =
                    (areaScore * 0.40) +
                    (ratioScore * 0.30) +
                    (centerScore * 0.20) +
                    (priorScore * 0.10);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = contour;
                }
            }

            // Exact-test behavior fallback: if shape scoring rejects every
            // contour, use the largest external foreground contour.
            return best ?? contours
                .OrderByDescending(contour => Math.Abs(Cv2.ContourArea(contour)))
                .FirstOrDefault();
        }

        /// <summary>
        /// First uses approxPolyDP exactly as in the successful desktop test.
        /// Multiple epsilon values are tried because perspective/glare can add
        /// a few contour vertices. A convex hull is tried second.
        /// </summary>
        private static CvPoint[]? ApproximateFourCorners(CvPoint[] contour)
        {
            double perimeter = Cv2.ArcLength(contour, true);

            for (double epsilonFactor = 0.008; epsilonFactor <= 0.052; epsilonFactor += 0.002)
            {
                CvPoint[] approximation = Cv2.ApproxPolyDP(
                    contour,
                    perimeter * epsilonFactor,
                    true);

                if (approximation.Length == 4 && Cv2.IsContourConvex(approximation))
                {
                    return approximation;
                }
            }

            CvPoint[] hull = Cv2.ConvexHull(contour);
            double hullPerimeter = Cv2.ArcLength(hull, true);
            for (double epsilonFactor = 0.008; epsilonFactor <= 0.080; epsilonFactor += 0.003)
            {
                CvPoint[] approximation = Cv2.ApproxPolyDP(
                    hull,
                    hullPerimeter * epsilonFactor,
                    true);

                if (approximation.Length == 4 && Cv2.IsContourConvex(approximation))
                {
                    return approximation;
                }
            }

            return null;
        }

        /// <summary>
        /// Fallback for rounded/chipped/missing corners. The contour is first
        /// aligned to its minimum-area rectangle only to determine four side
        /// bands. Each band then fits a line through the ACTUAL outer contour
        /// points. Adjacent fitted lines are intersected to recover theoretical
        /// physical corners.
        /// </summary>
        private static bool TryFitFourSideIntersections(
            CvPoint[] contour,
            out CardPoint[] corners)
        {
            corners = Array.Empty<CardPoint>();
            if (contour.Length < 20)
            {
                return false;
            }

            RotatedRect box = Cv2.MinAreaRect(contour);
            Point2f[] boxPoints = box.Points();
            CardPoint[] boxCorners = boxPoints
                .Select(point => new CardPoint(point.X, point.Y))
                .ToArray();

            CardPoint center = new(
                boxCorners.Average(point => point.X),
                boxCorners.Average(point => point.Y));
            CardPoint[] clockwise = SortClockwise(boxCorners, center);

            FittedLine[] lines = new FittedLine[4];
            for (int side = 0; side < 4; side++)
            {
                CardPoint a = clockwise[side];
                CardPoint b = clockwise[(side + 1) % 4];
                double sideLength = Distance(a, b);
                double band = Math.Max(4.0, sideLength * 0.055);

                List<CardPoint> sidePoints = new();
                foreach (CvPoint point in contour)
                {
                    CardPoint candidate = new(point.X, point.Y);
                    double t = ProjectionParameter(candidate, a, b);
                    if (t < 0.08 || t > 0.92)
                    {
                        continue; // deliberately ignore corner regions
                    }

                    if (DistanceToInfiniteLine(candidate, a, b) <= band)
                    {
                        sidePoints.Add(candidate);
                    }
                }

                if (sidePoints.Count < 8 || !TryFitLine(sidePoints, out lines[side]))
                {
                    return false;
                }
            }

            CardPoint[] intersections = new CardPoint[4];
            for (int i = 0; i < 4; i++)
            {
                int previous = (i + 3) % 4;
                if (!TryIntersect(lines[previous], lines[i], out intersections[i]))
                {
                    return false;
                }
            }

            corners = intersections;
            return true;
        }

        private static bool TryFitLine(IReadOnlyList<CardPoint> points, out FittedLine line)
        {
            line = default;
            if (points.Count < 2)
            {
                return false;
            }

            double meanX = points.Average(point => point.X);
            double meanY = points.Average(point => point.Y);
            double sxx = 0.0;
            double syy = 0.0;
            double sxy = 0.0;

            foreach (CardPoint point in points)
            {
                double dx = point.X - meanX;
                double dy = point.Y - meanY;
                sxx += dx * dx;
                syy += dy * dy;
                sxy += dx * dy;
            }

            double angle = 0.5 * Math.Atan2(2.0 * sxy, sxx - syy);
            double vx = Math.Cos(angle);
            double vy = Math.Sin(angle);
            if (!double.IsFinite(vx) || !double.IsFinite(vy))
            {
                return false;
            }

            line = new FittedLine(meanX, meanY, vx, vy);
            return true;
        }

        private static bool TryIntersect(FittedLine a, FittedLine b, out CardPoint point)
        {
            double cross = (a.Vx * b.Vy) - (a.Vy * b.Vx);
            if (Math.Abs(cross) < 0.00001)
            {
                point = default;
                return false;
            }

            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double t = ((dx * b.Vy) - (dy * b.Vx)) / cross;
            point = new CardPoint(
                (float)(a.X + (t * a.Vx)),
                (float)(a.Y + (t * a.Vy)));
            return true;
        }

        /// <summary>
        /// Orders an arbitrary four-point convex card so the SHORT physical
        /// edges become canonical top/bottom and the LONG physical edges become
        /// canonical left/right. This is what prevents a landscape camera buffer
        /// from being stretched into a sideways 750 x 1050 result.
        /// </summary>
        private static bool TryOrderCardCornersForPortrait(
            IReadOnlyList<CardPoint> input,
            out CardPoint[] ordered)
        {
            ordered = Array.Empty<CardPoint>();
            if (input.Count != 4) return false;

            // Do NOT infer orientation from which detected edge happens to be shorter.
            // That old behavior could rotate one capture 90 degrees when glare changed
            // a contour. Inspection captures are required to be upright, so preserve
            // screen/image orientation and order corners by their visual positions.
            CardPoint? tl = null, tr = null, br = null, bl = null;
            double minSum = double.PositiveInfinity, maxSum = double.NegativeInfinity;
            double maxDiff = double.NegativeInfinity, minDiff = double.PositiveInfinity;

            foreach (CardPoint point in input)
            {
                double sum = point.X + point.Y;
                double diff = point.X - point.Y;
                if (sum < minSum) { minSum = sum; tl = point; }
                if (sum > maxSum) { maxSum = sum; br = point; }
                if (diff > maxDiff) { maxDiff = diff; tr = point; }
                if (diff < minDiff) { minDiff = diff; bl = point; }
            }

            if (tl.HasValue && tr.HasValue && br.HasValue && bl.HasValue)
            {
                CardPoint[] candidate = { tl.Value, tr.Value, br.Value, bl.Value };
                if (candidate.Distinct().Count() == 4 && PolygonArea(candidate) > 1.0)
                {
                    ordered = candidate;
                    return true;
                }
            }

            // Conservative fallback for near-axis-aligned perspective views.
            CardPoint[] byY = input.OrderBy(point => point.Y).ToArray();
            CardPoint[] topPair = byY.Take(2).OrderBy(point => point.X).ToArray();
            CardPoint[] bottomPair = byY.Skip(2).OrderBy(point => point.X).ToArray();
            ordered = new[] { topPair[0], topPair[1], bottomPair[1], bottomPair[0] };
            return PolygonArea(ordered) > 1.0;
        }

        private static CardPoint[] SortClockwise(
            IEnumerable<CardPoint> points,
            CardPoint center)
        {
            return points
                .OrderBy(point => Math.Atan2(point.Y - center.Y, point.X - center.X))
                .ToArray();
        }


        /// <summary>
        /// Refines an already plausible card quadrilateral onto the actual physical
        /// card/background transition. The initial detector tells us approximately
        /// where the card is; this stage prevents a foreground shadow, printed border,
        /// or mask halo from becoming one of the final four sides.
        ///
        /// For each side independently we search only a narrow band around the proposed
        /// side. A true physical edge should satisfy all three conditions along most of
        /// its length:
        /// 1) a strong image gradient,
        /// 2) pixels OUTSIDE the edge resemble the image-rim background,
        /// 3) pixels INSIDE the edge differ from that background.
        /// Rounded corners are excluded from scoring. The four refined infinite lines
        /// are intersected to produce the final physical corners.
        /// </summary>
        private static bool TryRefineToPhysicalOuterEdges(
            Mat bgr,
            IReadOnlyList<CardPoint> initial,
            int width,
            int height,
            out CardPoint[] refined)
        {
            refined = Array.Empty<CardPoint>();
            if (initial.Count != 4 || width < 80 || height < 120)
                return false;

            if (!TryOrderCardCornersForPortrait(initial, out CardPoint[] ordered) ||
                !ValidateQuadrilateral(ordered, width, height))
                return false;

            using Mat gray = new();
            using Mat gradX = new();
            using Mat gradY = new();
            using Mat magnitude = new();
            Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.GaussianBlur(gray, gray, new CvSize(3, 3), 0.7);
            Cv2.Sobel(gray, gradX, MatType.CV_32F, 1, 0, 3);
            Cv2.Sobel(gray, gradY, MatType.CV_32F, 0, 1, 3);
            Cv2.Magnitude(gradX, gradY, magnitude);

            Scalar backgroundMean = EstimateRimMeanBgr(bgr, width, height);
            CardPoint centroid = new(
                (float)ordered.Average(p => p.X),
                (float)ordered.Average(p => p.Y));

            FittedLine[] sides = new FittedLine[4];
            for (int i = 0; i < 4; i++)
            {
                CardPoint a = ordered[i];
                CardPoint b = ordered[(i + 1) % 4];
                if (!TryRefineOnePhysicalSide(
                    bgr,
                    magnitude,
                    backgroundMean,
                    a,
                    b,
                    centroid,
                    width,
                    height,
                    out sides[i]))
                {
                    return false;
                }
            }

            if (!TryIntersect(sides[0], sides[3], out CardPoint tl) ||
                !TryIntersect(sides[0], sides[1], out CardPoint tr) ||
                !TryIntersect(sides[2], sides[1], out CardPoint br) ||
                !TryIntersect(sides[2], sides[3], out CardPoint bl))
                return false;

            CardPoint[] candidate = { tl, tr, br, bl };
            if (!ValidateQuadrilateral(candidate, width, height) ||
                !HasStrongPhysicalEdgeSupport(bgr, magnitude, backgroundMean, candidate, width, height))
                return false;

            // The refinement is deliberately local. Reject any solution that moved a
            // corner implausibly far from the already plausible initial card candidate.
            double diagonal = Math.Sqrt((width * (double)width) + (height * (double)height));
            double maximumCornerMove = diagonal * 0.075;
            for (int i = 0; i < 4; i++)
            {
                if (Distance(candidate[i], ordered[i]) > maximumCornerMove)
                    return false;
            }

            refined = candidate;
            return true;
        }

        private static bool TryRefineOnePhysicalSide(
            Mat bgr,
            Mat gradient,
            Scalar backgroundMean,
            CardPoint a,
            CardPoint b,
            CardPoint centroid,
            int width,
            int height,
            out FittedLine refinedLine)
        {
            refinedLine = default;
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length < 30.0)
                return false;

            double tx = dx / length;
            double ty = dy / length;
            double nx = -ty;
            double ny = tx;
            double midX = (a.X + b.X) * 0.5;
            double midY = (a.Y + b.Y) * 0.5;

            // Make normal point OUTWARD, away from polygon centroid.
            double toCenterX = centroid.X - midX;
            double toCenterY = centroid.Y - midY;
            if ((nx * toCenterX) + (ny * toCenterY) > 0)
            {
                nx = -nx;
                ny = -ny;
            }

            int searchRadius = (int)Math.Clamp(Math.Round(length * 0.075), 7.0, 42.0);
            double bestScore = double.NegativeInfinity;
            int bestOffset = 0;
            int sampleCount = Math.Clamp((int)Math.Round(length / 6.0), 42, 110);

            for (int offset = -searchRadius; offset <= searchRadius; offset++)
            {
                double score = 0.0;
                int valid = 0;
                for (int i = 0; i < sampleCount; i++)
                {
                    double t = 0.13 + (0.74 * i / Math.Max(sampleCount - 1.0, 1.0));
                    double x = a.X + (dx * t) + (nx * offset);
                    double y = a.Y + (dy * t) + (ny * offset);

                    int px = (int)Math.Round(x);
                    int py = (int)Math.Round(y);
                    if (px < 7 || py < 7 || px >= width - 7 || py >= height - 7)
                        continue;

                    float g = gradient.At<float>(py, px);
                    double outsideDistance = ColorDistanceToBackground(
                        bgr,
                        x + (nx * 5.0),
                        y + (ny * 5.0),
                        backgroundMean,
                        width,
                        height);
                    double insideDistance = ColorDistanceToBackground(
                        bgr,
                        x - (nx * 5.0),
                        y - (ny * 5.0),
                        backgroundMean,
                        width,
                        height);

                    // Physical boundary: high gradient, outside looks background-like,
                    // inside looks card-like. Clamp contrast contribution so one shiny
                    // sample cannot dominate the whole side.
                    double transition = Math.Clamp(insideDistance - outsideDistance, -80.0, 140.0);
                    double outsideBonus = Math.Clamp(65.0 - outsideDistance, -35.0, 65.0);
                    score += g + (transition * 0.75) + (outsideBonus * 0.30);
                    valid++;
                }

                if (valid < sampleCount * 0.72)
                    continue;

                score /= valid;
                score -= Math.Abs(offset) * 0.10; // conservative local preference
                if (score > bestScore)
                {
                    bestScore = score;
                    bestOffset = offset;
                }
            }

            if (!double.IsFinite(bestScore))
                return false;

            refinedLine = new FittedLine(
                midX + (nx * bestOffset),
                midY + (ny * bestOffset),
                tx,
                ty);
            return true;
        }

        private static Scalar EstimateRimMeanBgr(Mat bgr, int width, int height)
        {
            int rimX = Math.Max(4, (int)Math.Round(width * 0.045));
            int rimY = Math.Max(4, (int)Math.Round(height * 0.045));
            double b = 0, g = 0, r = 0;
            long count = 0;

            for (int y = 0; y < height; y += 3)
            {
                for (int x = 0; x < width; x += 3)
                {
                    if (x >= rimX && x < width - rimX && y >= rimY && y < height - rimY)
                        continue;
                    Vec3b p = bgr.At<Vec3b>(y, x);
                    b += p.Item0;
                    g += p.Item1;
                    r += p.Item2;
                    count++;
                }
            }

            if (count == 0)
                return new Scalar(0, 0, 0);
            return new Scalar(b / count, g / count, r / count);
        }

        private static double ColorDistanceToBackground(
            Mat bgr,
            double x,
            double y,
            Scalar backgroundMean,
            int width,
            int height)
        {
            int px = Math.Clamp((int)Math.Round(x), 0, width - 1);
            int py = Math.Clamp((int)Math.Round(y), 0, height - 1);
            Vec3b p = bgr.At<Vec3b>(py, px);
            double db = p.Item0 - backgroundMean.Val0;
            double dg = p.Item1 - backgroundMean.Val1;
            double dr = p.Item2 - backgroundMean.Val2;
            return Math.Sqrt((db * db) + (dg * dg) + (dr * dr));
        }

        private static bool HasStrongPhysicalEdgeSupport(
            Mat bgr,
            Mat gradient,
            Scalar backgroundMean,
            IReadOnlyList<CardPoint> corners,
            int width,
            int height)
        {
            CardPoint centroid = new(
                (float)corners.Average(p => p.X),
                (float)corners.Average(p => p.Y));

            int supportedSides = 0;
            for (int side = 0; side < 4; side++)
            {
                CardPoint a = corners[side];
                CardPoint b = corners[(side + 1) % 4];
                double dx = b.X - a.X;
                double dy = b.Y - a.Y;
                double len = Math.Sqrt((dx * dx) + (dy * dy));
                if (len < 20) continue;
                double nx = -(dy / len);
                double ny = dx / len;
                double mx = (a.X + b.X) * 0.5;
                double my = (a.Y + b.Y) * 0.5;
                if ((nx * (centroid.X - mx)) + (ny * (centroid.Y - my)) > 0)
                {
                    nx = -nx;
                    ny = -ny;
                }

                double gradSum = 0;
                double transitionSum = 0;
                int valid = 0;
                const int samples = 48;
                for (int i = 0; i < samples; i++)
                {
                    double t = 0.14 + (0.72 * i / (samples - 1.0));
                    double x = a.X + dx * t;
                    double y = a.Y + dy * t;
                    int px = (int)Math.Round(x);
                    int py = (int)Math.Round(y);
                    if (px < 6 || py < 6 || px >= width - 6 || py >= height - 6)
                        continue;
                    gradSum += gradient.At<float>(py, px);
                    double outside = ColorDistanceToBackground(bgr, x + nx * 5.0, y + ny * 5.0, backgroundMean, width, height);
                    double inside = ColorDistanceToBackground(bgr, x - nx * 5.0, y - ny * 5.0, backgroundMean, width, height);
                    transitionSum += inside - outside;
                    valid++;
                }

                if (valid >= 32)
                {
                    double avgGradient = gradSum / valid;
                    double avgTransition = transitionSum / valid;
                    if (avgGradient >= 24.0 && avgTransition >= 4.0)
                        supportedSides++;
                }
            }

            return supportedSides >= 3;
        }

        private static bool ValidateQuadrilateral(
            IReadOnlyList<CardPoint> corners,
            int width,
            int height)
        {
            if (corners.Count != 4)
            {
                return false;
            }

            const double boundaryTolerance = 0.04;
            foreach (CardPoint point in corners)
            {
                if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
                {
                    return false;
                }

                if (point.X < -width * boundaryTolerance ||
                    point.X > width * (1.0 + boundaryTolerance) ||
                    point.Y < -height * boundaryTolerance ||
                    point.Y > height * (1.0 + boundaryTolerance))
                {
                    return false;
                }
            }

            double top = Distance(corners[0], corners[1]);
            double right = Distance(corners[1], corners[2]);
            double bottom = Distance(corners[2], corners[3]);
            double left = Distance(corners[3], corners[0]);

            double meanWidth = (top + bottom) * 0.5;
            double meanHeight = (left + right) * 0.5;
            if (meanWidth < 20.0 || meanHeight < 30.0 || meanHeight <= meanWidth)
            {
                return false;
            }

            double ratio = meanWidth / meanHeight;
            // Centering capture is approximately overhead. A standard 2.5 x 3.5 card
            // has ratio ~0.714. Keep enough perspective tolerance, but reject the very
            // broad range that previously allowed obvious false rectangles to pass.
            if (ratio < 0.56 || ratio > 0.86)
            {
                return false;
            }

            double widthAgreement = Math.Min(top, bottom) / Math.Max(Math.Max(top, bottom), 1.0);
            double heightAgreement = Math.Min(left, right) / Math.Max(Math.Max(left, right), 1.0);
            if (widthAgreement < 0.72 || heightAgreement < 0.72)
            {
                return false;
            }

            double area = PolygonArea(corners);
            double imageArea = width * (double)height;
            return area >= imageArea * MinimumForegroundAreaFraction &&
                   area <= imageArea * MaximumForegroundAreaFraction;
        }

        private static double CalculateGeometryConfidence(
            IReadOnlyList<CardPoint> corners,
            double areaFraction,
            IReadOnlyList<CardPoint>? normalizedPriorCorners,
            int width,
            int height)
        {
            double top = Distance(corners[0], corners[1]);
            double right = Distance(corners[1], corners[2]);
            double bottom = Distance(corners[2], corners[3]);
            double left = Distance(corners[3], corners[0]);
            double ratio = ((top + bottom) * 0.5) / Math.Max((left + right) * 0.5, 0.001);

            double ratioScore = Math.Exp(-Math.Pow((ratio - ExpectedCardRatio) / 0.20, 2.0));
            double oppositeScore = 1.0 - Math.Min(
                1.0,
                (Math.Abs(top - bottom) / Math.Max(top + bottom, 1.0)) +
                (Math.Abs(left - right) / Math.Max(left + right, 1.0)));
            double areaScore = Math.Clamp(areaFraction / 0.30, 0.0, 1.0);

            double priorScore = 1.0;
            if (normalizedPriorCorners is not null && normalizedPriorCorners.Count == 4)
            {
                double centerX = corners.Average(point => point.X) / Math.Max(width, 1);
                double centerY = corners.Average(point => point.Y) / Math.Max(height, 1);
                double priorX = normalizedPriorCorners.Average(point => point.X);
                double priorY = normalizedPriorCorners.Average(point => point.Y);
                double d = Math.Sqrt(Math.Pow(centerX - priorX, 2.0) + Math.Pow(centerY - priorY, 2.0));
                priorScore = Math.Clamp(1.0 - d, 0.35, 1.0);
            }

            return Math.Clamp(
                (ratioScore * 0.35) +
                (oppositeScore * 0.25) +
                (areaScore * 0.25) +
                (priorScore * 0.15),
                0.0,
                1.0);
        }

        private static double ProjectionParameter(CardPoint point, CardPoint a, CardPoint b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double denominator = (dx * dx) + (dy * dy);
            if (denominator < 0.000001)
            {
                return 0.0;
            }

            return (((point.X - a.X) * dx) + ((point.Y - a.Y) * dy)) / denominator;
        }

        private static double DistanceToInfiniteLine(CardPoint point, CardPoint a, CardPoint b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length < 0.000001)
            {
                return double.MaxValue;
            }

            return Math.Abs(
                (dy * point.X) -
                (dx * point.Y) +
                (b.X * a.Y) -
                (b.Y * a.X)) / length;
        }

        private static double Distance(CardPoint a, CardPoint b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static double PolygonArea(IReadOnlyList<CardPoint> points)
        {
            double area = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                CardPoint current = points[i];
                CardPoint next = points[(i + 1) % points.Count];
                area += (current.X * next.Y) - (next.X * current.Y);
            }

            return Math.Abs(area) * 0.5;
        }

        private static CardGeometryResult Failed(SixLabors.ImageSharp.Image<Rgba32> source)
        {
            return new CardGeometryResult
            {
                Success = false,
                Corners = Array.Empty<CardPoint>(),
                Confidence = 0.0,
                SourceWidth = source.Width,
                SourceHeight = source.Height
            };
        }

        private readonly record struct LineCandidate(FittedLine Line, double Position, double Length);

        private readonly record struct FittedLine(
            double X,
            double Y,
            double Vx,
            double Vy);
    }
}
