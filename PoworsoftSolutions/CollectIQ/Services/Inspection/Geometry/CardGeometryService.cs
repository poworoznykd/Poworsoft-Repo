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
                float scale = Math.Min(
                    1.0f,
                    DetectionMaximumDimension / (float)Math.Max(source.Width, source.Height));

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

                // First choice for CollectIQ inspection captures: find the strongest
                // card-shaped closed rectangle directly from edges. The user is told
                // to place the card on a solid matte background, so the physical
                // perimeter should be the dominant large quadrilateral.
                if (TryDetectCardFromEdges(
                    bgr,
                    detectionWidth,
                    detectionHeight,
                    normalizedPriorCorners,
                    out CardPoint[] edgeCorners,
                    out double edgeConfidence))
                {
                    CardPoint[] edgeFullResolution = edgeCorners
                        .Select(point => new CardPoint(point.X / scale, point.Y / scale))
                        .ToArray();

                    return new CardGeometryResult
                    {
                        Success = true,
                        Corners = edgeFullResolution,
                        Confidence = edgeConfidence,
                        SourceWidth = source.Width,
                        SourceHeight = source.Height
                    };
                }

                // Fallback: retain the existing GrabCut implementation for difficult
                // backgrounds or cards whose physical perimeter is unusually weak.
                using Mat grabMask = CreateGrabCutSeed(
                    detectionWidth,
                    detectionHeight,
                    normalizedPriorCorners);
                using Mat backgroundModel = new(1, 65, MatType.CV_64FC1, Scalar.All(0));
                using Mat foregroundModel = new(1, 65, MatType.CV_64FC1, Scalar.All(0));

                Cv2.GrabCut(
                    bgr,
                    grabMask,
                    new CvRect(),
                    backgroundModel,
                    foregroundModel,
                    6,
                    GrabCutModes.InitWithMask);

                using Mat foregroundMask = BuildBinaryForegroundMask(grabMask);
                using Mat cleanedMask = CleanForegroundMask(foregroundMask);

                Cv2.FindContours(
                    cleanedMask,
                    out CvPoint[][] contours,
                    out HierarchyIndex[] _,
                    RetrievalModes.External,
                    ContourApproximationModes.ApproxSimple);

                CvPoint[]? cardContour = SelectCardContour(
                    contours,
                    detectionWidth,
                    detectionHeight,
                    normalizedPriorCorners);

                if (cardContour is null)
                {
                    return Failed(source);
                }

                CvPoint[]? quadrilateral = ApproximateFourCorners(cardContour);
                CardPoint[] corners;

                if (quadrilateral is not null)
                {
                    corners = quadrilateral
                        .Select(point => new CardPoint(point.X, point.Y))
                        .ToArray();
                }
                else if (!TryFitFourSideIntersections(cardContour, out corners))
                {
                    return Failed(source);
                }

                if (!TryOrderCardCornersForPortrait(corners, out CardPoint[] ordered))
                {
                    return Failed(source);
                }

                if (!ValidateQuadrilateral(ordered, detectionWidth, detectionHeight))
                {
                    return Failed(source);
                }

                double contourArea = Math.Abs(Cv2.ContourArea(cardContour));
                double imageArea = detectionWidth * (double)detectionHeight;
                double areaFraction = contourArea / Math.Max(imageArea, 1.0);
                double geometryConfidence = CalculateGeometryConfidence(
                    ordered,
                    areaFraction,
                    normalizedPriorCorners,
                    detectionWidth,
                    detectionHeight);

                CardPoint[] fullResolution = ordered
                    .Select(point => new CardPoint(point.X / scale, point.Y / scale))
                    .ToArray();

                return new CardGeometryResult
                {
                    Success = true,
                    Corners = fullResolution,
                    Confidence = geometryConfidence,
                    SourceWidth = source.Width,
                    SourceHeight = source.Height
                };
            }
            catch (OpenCvSharpException)
            {
                return Failed(source);
            }
            catch (DllNotFoundException)
            {
                return Failed(source);
            }
            catch (TypeInitializationException)
            {
                return Failed(source);
            }
        }


        /// <summary>
        /// Detects the physical outside card perimeter from an edge image.
        /// This deliberately prefers a large, convex, trading-card-shaped
        /// quadrilateral and ignores internal printed borders/artwork.
        /// </summary>
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
            if (input.Count != 4)
            {
                return false;
            }

            CardPoint center = new(
                input.Average(point => point.X),
                input.Average(point => point.Y));
            CardPoint[] clockwise = SortClockwise(input, center);

            double[] lengths = new double[4];
            for (int i = 0; i < 4; i++)
            {
                lengths[i] = Distance(clockwise[i], clockwise[(i + 1) % 4]);
            }

            double pair02 = (lengths[0] + lengths[2]) * 0.5;
            double pair13 = (lengths[1] + lengths[3]) * 0.5;
            int shortEdgeA = pair02 <= pair13 ? 0 : 1;
            int shortEdgeB = (shortEdgeA + 2) % 4;

            CardPoint a0 = clockwise[shortEdgeA];
            CardPoint a1 = clockwise[(shortEdgeA + 1) % 4];
            CardPoint b0 = clockwise[shortEdgeB];
            CardPoint b1 = clockwise[(shortEdgeB + 1) % 4];

            // Pick the short side whose midpoint appears highest in the
            // auto-oriented camera image as canonical top. This is deterministic
            // and, crucially, always produces portrait geometry.
            double midAY = (a0.Y + a1.Y) * 0.5;
            double midBY = (b0.Y + b1.Y) * 0.5;
            int topEdge = midAY <= midBY ? shortEdgeA : shortEdgeB;

            CardPoint top0 = clockwise[topEdge];
            CardPoint top1 = clockwise[(topEdge + 1) % 4];

            int topLeftIndex;
            int topRightIndex;
            if (top0.X <= top1.X)
            {
                topLeftIndex = topEdge;
                topRightIndex = (topEdge + 1) % 4;
            }
            else
            {
                topLeftIndex = (topEdge + 1) % 4;
                topRightIndex = topEdge;
            }

            // The bottom corner adjacent to TR is the non-top neighbour of TR;
            // the bottom corner adjacent to TL is the non-top neighbour of TL.
            int nextFromTr = (topRightIndex + 1) % 4;
            int prevFromTr = (topRightIndex + 3) % 4;
            int bottomRightIndex = nextFromTr == topLeftIndex ? prevFromTr : nextFromTr;

            int nextFromTl = (topLeftIndex + 1) % 4;
            int prevFromTl = (topLeftIndex + 3) % 4;
            int bottomLeftIndex = nextFromTl == topRightIndex ? prevFromTl : nextFromTl;

            ordered = new[]
            {
                clockwise[topLeftIndex],
                clockwise[topRightIndex],
                clockwise[bottomRightIndex],
                clockwise[bottomLeftIndex]
            };

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
            if (ratio < 0.48 || ratio > 0.94)
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

        private readonly record struct FittedLine(
            double X,
            double Y,
            double Vx,
            double Vy);
    }
}
