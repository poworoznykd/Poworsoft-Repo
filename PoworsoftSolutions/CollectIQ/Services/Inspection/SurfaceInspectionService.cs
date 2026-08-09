/*
* FILE: SurfaceInspectionService.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* DESCRIPTION:
*     Performs directional surface inspection from four card photographs.
*     Each capture is automatically detected, perspective-corrected to the
*     complete trading-card rectangle, and fine-aligned before any directional
*     calculations are performed. This reduces false surface signals caused by
*     normal hand movement and ensures the result images show the full card.
*/

using CollectIQ.Interfaces;
using CollectIQ.Models.Inspection;
using CollectIQ.Models.Inspection.Registration;
using Microsoft.Maui.Storage;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using ImageSharpSize = SixLabors.ImageSharp.Size;
using ImageSharpResizeMode = SixLabors.ImageSharp.Processing.ResizeMode;

namespace CollectIQ.Services.Inspection
{
    /// <summary>
    /// Performs local directional-image processing for surface inspection.
    /// </summary>
    public sealed class SurfaceInspectionService : ISurfaceInspectionService
    {
        // A standard trading card is 2.5 x 3.5 inches. All accepted captures
        // are perspective-warped to this same pixel coordinate system.
        private const int ProcessingWidth = 750;
        private const int ProcessingHeight = 1050;
        private const int DetectionMaximumDimension = 560;
        private const float Epsilon = 0.001f;

        private const int HoughThetaStepDegrees = 2;
        private const int MaximumHoughPeaks = 36;
        private const int MaximumParallelPairs = 22;

        private readonly ICardRegistrationService cardRegistrationService;

        public SurfaceInspectionService(ICardRegistrationService cardRegistrationService)
        {
            this.cardRegistrationService = cardRegistrationService;
        }

        /// <inheritdoc />
        public async Task<SurfaceInspectionResult> AnalyzeAsync(
            string neutralReferencePath,
            IReadOnlyDictionary<SurfaceLightDirection, string> captures,
            CancellationToken cancellationToken = default)
        {
            ValidateCaptures(captures);
            if (string.IsNullOrWhiteSpace(neutralReferencePath) || !File.Exists(neutralReferencePath))
            {
                throw new InvalidOperationException(
                    "A valid neutral reference image is required before directional surface inspection.");
            }

            string outputDirectory = Path.Combine(
                FileSystem.AppDataDirectory,
                "SurfaceInspections",
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));

            Directory.CreateDirectory(outputDirectory);

            Dictionary<string, string> registrationInputs = captures.ToDictionary(
                item => item.Key.ToString(),
                item => item.Value,
                StringComparer.OrdinalIgnoreCase);
            registrationInputs["Reference"] = neutralReferencePath;

            CardRegistrationResult registration = await cardRegistrationService.RegisterAsync(
                registrationInputs,
                "Reference",
                outputDirectory,
                cancellationToken);

            RegisteredCardFrame topCapture = registration.Frames[SurfaceLightDirection.Top.ToString()];
            RegisteredCardFrame rightCapture = registration.Frames[SurfaceLightDirection.Right.ToString()];
            RegisteredCardFrame bottomCapture = registration.Frames[SurfaceLightDirection.Bottom.ToString()];
            RegisteredCardFrame leftCapture = registration.Frames[SurfaceLightDirection.Left.ToString()];

            float[] top = topCapture.Luminance;
            float[] right = rightCapture.Luminance;
            float[] bottom = bottomCapture.Luminance;
            float[] left = leftCapture.Luminance;

            float targetMean =
                (CalculateMean(top) + CalculateMean(right) +
                 CalculateMean(bottom) + CalculateMean(left)) / 4.0f;

            NormalizeBrightness(top, targetMean);
            NormalizeBrightness(right, targetMean);
            NormalizeBrightness(bottom, targetMean);
            NormalizeBrightness(left, targetMean);

            int pixelCount = ProcessingWidth * ProcessingHeight;
            float[] diffuse = new float[pixelCount];
            float[] relief = new float[pixelCount];

            for (int index = 0; index < pixelCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                diffuse[index] =
                    (top[index] + right[index] + bottom[index] + left[index]) / 4.0f;

                float horizontal =
                    (right[index] - left[index]) /
                    (right[index] + left[index] + Epsilon);

                float vertical =
                    (bottom[index] - top[index]) /
                    (bottom[index] + top[index] + Epsilon);

                relief[index] = MathF.Sqrt(
                    (horizontal * horizontal) +
                    (vertical * vertical));
            }

            float[] localRelief = HighPassRelief(relief);
            float threshold = CalculatePercentile(localRelief, 0.97f);
            double anomalyScore = CalculateAnomalyScore(localRelief, threshold);

            double registrationScore = registration.OverallQuality;

            double exposureConsistency = CalculateExposureConsistency(
                top,
                right,
                bottom,
                left);

            double consistencyScore = Math.Clamp(
                (registrationScore * 0.80) +
                (exposureConsistency * 0.20),
                0.0,
                100.0);

            string diffusePath = Path.Combine(outputDirectory, "diffuse_full_card.png");
            string reliefPath = Path.Combine(outputDirectory, "surface_relief_full_card.png");
            string heatmapPath = Path.Combine(outputDirectory, "surface_heatmap_full_card.png");

            await SaveGrayscaleAsync(diffuse, diffusePath, cancellationToken);
            await SaveGrayscaleAsync(
                localRelief,
                reliefPath,
                cancellationToken,
                normalize: true);
            await SaveHeatmapAsync(
                localRelief,
                threshold,
                heatmapPath,
                cancellationToken);

            return new SurfaceInspectionResult
            {
                DiffuseImagePath = diffusePath,
                ReliefImagePath = reliefPath,
                HeatmapImagePath = heatmapPath,
                Diagnostics = new AlignmentDiagnostics
                {
                    EdgeOverlayPath = registration.EdgeOverlayPath,
                    AlignmentAveragePath = registration.AlignmentAveragePath,
                    Frames = new List<InspectionDebugFrame>
                    {
                        BuildDebugFrame(topCapture, SurfaceLightDirection.Top),
                        BuildDebugFrame(rightCapture, SurfaceLightDirection.Right),
                        BuildDebugFrame(bottomCapture, SurfaceLightDirection.Bottom),
                        BuildDebugFrame(leftCapture, SurfaceLightDirection.Left)
                    }
                },
                AnomalyScore = anomalyScore,
                CaptureConsistencyScore = consistencyScore,
                Summary = BuildSummary(
                    anomalyScore,
                    consistencyScore,
                    registrationScore)
            };
        }

        private static void ValidateCaptures(
            IReadOnlyDictionary<SurfaceLightDirection, string> captures)
        {
            foreach (SurfaceLightDirection direction in
                     Enum.GetValues<SurfaceLightDirection>())
            {
                if (!captures.TryGetValue(direction, out string? path) ||
                    string.IsNullOrWhiteSpace(path) ||
                    !File.Exists(path))
                {
                    throw new InvalidOperationException(
                        $"A valid {direction} illumination image is required.");
                }
            }
        }

        /// <summary>
        /// Detects the complete card in a capture and warps it to a fixed
        /// 750 x 1050 coordinate system. No fixed centered crop is used.
        /// </summary>
        private static async Task<RegisteredCapture> LoadAndRegisterCaptureAsync(
            string path,
            SurfaceLightDirection direction,
            string outputDirectory,
            CancellationToken cancellationToken)
        {
            using SixLabors.ImageSharp.Image<Rgba32> source =
                await ImageSharpImage.LoadAsync<Rgba32>(path, cancellationToken);

            source.Mutate(context => context.AutoOrient());

            DetectionResult detection = DetectCardQuadrilateral(source);

            if (!detection.Success)
            {
                throw new InvalidOperationException(
                    $"The complete card could not be detected in the {direction.ToString().ToLowerInvariant()}-light image. " +
                    "Make sure all four card edges are visible against a contrasting background and retake the scan.");
            }

            string detectionOverlayPath = Path.Combine(
                outputDirectory,
                $"detected_{direction.ToString().ToLowerInvariant()}.png");

            await SaveDetectionOverlayAsync(
                source,
                detection.Corners,
                detectionOverlayPath,
                cancellationToken);

            using SixLabors.ImageSharp.Image<Rgba32> registered =
                WarpCardToStandardRectangle(source, detection.Corners);

            string registeredPath = Path.Combine(
                outputDirectory,
                $"registered_{direction.ToString().ToLowerInvariant()}.png");

            await using (FileStream registeredStream = new FileStream(
                registeredPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                await registered.SaveAsync(
                    registeredStream,
                    new PngEncoder(),
                    cancellationToken);
            }

            float[] luminance = ExtractLuminance(registered);

            return new RegisteredCapture
            {
                Direction = direction,
                Luminance = luminance,
                DetectionConfidence = detection.Confidence,
                DetectionOverlayPath = detectionOverlayPath,
                RegisteredImagePath = registeredPath
            };
        }

        /// <summary>
        /// Finds a trading-card-sized quadrilateral using strong long edges in
        /// the photograph. A Hough-line search is used rather than assuming the
        /// card is centered, which allows moderate camera translation, rotation
        /// and perspective changes between captures.
        /// </summary>
        private static DetectionResult DetectCardQuadrilateral(
            SixLabors.ImageSharp.Image<Rgba32> source)
        {
            float scale = Math.Min(
                1.0f,
                DetectionMaximumDimension /
                (float)Math.Max(source.Width, source.Height));

            int width = Math.Max(120, (int)Math.Round(source.Width * scale));
            int height = Math.Max(120, (int)Math.Round(source.Height * scale));

            using SixLabors.ImageSharp.Image<Rgba32> detectionImage = source.Clone(
                context => context.Resize(new ResizeOptions
                {
                    Size = new ImageSharpSize(width, height),
                    Mode = ImageSharpResizeMode.Stretch,
                    Sampler = KnownResamplers.Bicubic
                }));

            float[] gray = ExtractLuminance(detectionImage);
            float[] smoothed = BoxBlur(gray, width, height, 1);

            CalculateSobel(
                smoothed,
                width,
                height,
                out float[] gradientX,
                out float[] gradientY,
                out float[] gradientMagnitude);

            float edgeThreshold = CalculatePercentile(gradientMagnitude, 0.91f);
            List<HoughLine> lines = FindHoughLines(
                gradientMagnitude,
                edgeThreshold,
                width,
                height);

            if (lines.Count < 4)
            {
                return DetectionResult.Failed;
            }

            List<LinePair> parallelPairs = BuildParallelLinePairs(
                lines,
                width,
                height);

            if (parallelPairs.Count < 2)
            {
                return DetectionResult.Failed;
            }

            QuadCandidate? bestCandidate = null;

            foreach (LinePair firstPair in parallelPairs.Take(MaximumParallelPairs))
            {
                foreach (LinePair secondPair in parallelPairs.Take(MaximumParallelPairs))
                {
                    if (ReferenceEquals(firstPair, secondPair))
                    {
                        continue;
                    }

                    float angleDifference = AbsoluteAngleDifferenceDegrees(
                        firstPair.AverageThetaDegrees,
                        secondPair.AverageThetaDegrees);

                    if (angleDifference < 70.0f || angleDifference > 110.0f)
                    {
                        continue;
                    }

                    if (!TryBuildQuadrilateral(
                            firstPair,
                            secondPair,
                            width,
                            height,
                            out FloatPoint[] corners))
                    {
                        continue;
                    }

                    float candidateScore = ScoreQuadrilateral(
                        corners,
                        firstPair,
                        secondPair,
                        width,
                        height);

                    if (candidateScore <= 0.0f)
                    {
                        continue;
                    }

                    if (bestCandidate == null ||
                        candidateScore > bestCandidate.Score)
                    {
                        bestCandidate = new QuadCandidate
                        {
                            Corners = corners,
                            Score = candidateScore
                        };
                    }
                }
            }

            if (bestCandidate == null)
            {
                return DetectionResult.Failed;
            }

            FloatPoint[] sourceScaleCorners = bestCandidate.Corners
                .Select(point => new FloatPoint(
                    point.X / scale,
                    point.Y / scale))
                .ToArray();

            float confidence = Math.Clamp(bestCandidate.Score, 0.0f, 1.0f);

            return new DetectionResult
            {
                Success = true,
                Corners = OrderCorners(sourceScaleCorners),
                Confidence = confidence
            };
        }

        private static List<HoughLine> FindHoughLines(
            float[] gradientMagnitude,
            float threshold,
            int width,
            int height)
        {
            int thetaCount = 180 / HoughThetaStepDegrees;
            float diagonal = MathF.Sqrt((width * width) + (height * height));
            int rhoCount = ((int)Math.Ceiling(diagonal) * 2) + 1;
            int rhoOffset = rhoCount / 2;

            int[,] accumulator = new int[thetaCount, rhoCount];
            float[] cosines = new float[thetaCount];
            float[] sines = new float[thetaCount];

            for (int thetaIndex = 0; thetaIndex < thetaCount; thetaIndex++)
            {
                float radians = DegreesToRadians(thetaIndex * HoughThetaStepDegrees);
                cosines[thetaIndex] = MathF.Cos(radians);
                sines[thetaIndex] = MathF.Sin(radians);
            }

            // Sample strong edges to keep the mobile workload reasonable.
            for (int y = 2; y < height - 2; y += 2)
            {
                for (int x = 2; x < width - 2; x += 2)
                {
                    float magnitude = gradientMagnitude[(y * width) + x];

                    if (magnitude < threshold)
                    {
                        continue;
                    }

                    for (int thetaIndex = 0; thetaIndex < thetaCount; thetaIndex++)
                    {
                        int rho = (int)MathF.Round(
                            (x * cosines[thetaIndex]) +
                            (y * sines[thetaIndex])) + rhoOffset;

                        if ((uint)rho < (uint)rhoCount)
                        {
                            accumulator[thetaIndex, rho]++;
                        }
                    }
                }
            }

            var candidates = new List<HoughLine>();

            for (int thetaIndex = 0; thetaIndex < thetaCount; thetaIndex++)
            {
                for (int rhoIndex = 1; rhoIndex < rhoCount - 1; rhoIndex++)
                {
                    int votes = accumulator[thetaIndex, rhoIndex];

                    if (votes < Math.Max(12, Math.Min(width, height) / 20))
                    {
                        continue;
                    }

                    candidates.Add(new HoughLine
                    {
                        ThetaDegrees = thetaIndex * HoughThetaStepDegrees,
                        ThetaRadians = DegreesToRadians(thetaIndex * HoughThetaStepDegrees),
                        Rho = rhoIndex - rhoOffset,
                        Votes = votes
                    });
                }
            }

            List<HoughLine> selected = new();

            foreach (HoughLine candidate in candidates
                         .OrderByDescending(value => value.Votes))
            {
                bool duplicate = selected.Any(existing =>
                    AbsoluteAngleDifferenceDegrees(
                        existing.ThetaDegrees,
                        candidate.ThetaDegrees) <= 4.0f &&
                    MathF.Abs(existing.Rho - candidate.Rho) <= 12.0f);

                if (duplicate)
                {
                    continue;
                }

                selected.Add(candidate);

                if (selected.Count >= MaximumHoughPeaks)
                {
                    break;
                }
            }

            return selected;
        }

        private static List<LinePair> BuildParallelLinePairs(
            IReadOnlyList<HoughLine> lines,
            int width,
            int height)
        {
            float minimumSeparation = Math.Min(width, height) * 0.28f;
            float maximumSeparation = MathF.Sqrt((width * width) + (height * height)) * 0.95f;
            var pairs = new List<LinePair>();

            for (int first = 0; first < lines.Count; first++)
            {
                for (int second = first + 1; second < lines.Count; second++)
                {
                    HoughLine a = lines[first];
                    HoughLine b = lines[second];

                    float angleDifference = AbsoluteAngleDifferenceDegrees(
                        a.ThetaDegrees,
                        b.ThetaDegrees);

                    if (angleDifference > 8.0f)
                    {
                        continue;
                    }

                    float separation = MathF.Abs(a.Rho - b.Rho);

                    if (separation < minimumSeparation ||
                        separation > maximumSeparation)
                    {
                        continue;
                    }

                    pairs.Add(new LinePair
                    {
                        First = a,
                        Second = b,
                        AverageThetaDegrees = AverageLineAngleDegrees(
                            a.ThetaDegrees,
                            b.ThetaDegrees),
                        Score = a.Votes + b.Votes
                    });
                }
            }

            return pairs
                .OrderByDescending(pair => pair.Score)
                .ToList();
        }

        private static bool TryBuildQuadrilateral(
            LinePair firstPair,
            LinePair secondPair,
            int width,
            int height,
            out FloatPoint[] corners)
        {
            corners = Array.Empty<FloatPoint>();

            if (!TryIntersectLines(
                    firstPair.First,
                    secondPair.First,
                    out FloatPoint p1) ||
                !TryIntersectLines(
                    firstPair.First,
                    secondPair.Second,
                    out FloatPoint p2) ||
                !TryIntersectLines(
                    firstPair.Second,
                    secondPair.First,
                    out FloatPoint p3) ||
                !TryIntersectLines(
                    firstPair.Second,
                    secondPair.Second,
                    out FloatPoint p4))
            {
                return false;
            }

            FloatPoint[] ordered = OrderCorners(new[] { p1, p2, p3, p4 });

            float toleranceX = width * 0.10f;
            float toleranceY = height * 0.10f;

            if (ordered.Any(point =>
                    point.X < -toleranceX ||
                    point.X > width + toleranceX ||
                    point.Y < -toleranceY ||
                    point.Y > height + toleranceY))
            {
                return false;
            }

            corners = ordered;
            return true;
        }

        private static float ScoreQuadrilateral(
            FloatPoint[] corners,
            LinePair firstPair,
            LinePair secondPair,
            int width,
            int height)
        {
            float area = PolygonArea(corners);
            float frameArea = width * height;
            float areaFraction = area / Math.Max(frameArea, 1.0f);

            // The card must occupy a meaningful portion of the image. Allow a
            // wide range because users can hold the camera at different heights.
            if (areaFraction < 0.12f || areaFraction > 0.92f)
            {
                return 0.0f;
            }

            float topLength = Distance(corners[0], corners[1]);
            float rightLength = Distance(corners[1], corners[2]);
            float bottomLength = Distance(corners[2], corners[3]);
            float leftLength = Distance(corners[3], corners[0]);

            float horizontal = (topLength + bottomLength) * 0.5f;
            float vertical = (leftLength + rightLength) * 0.5f;

            if (horizontal < 1.0f || vertical < 1.0f)
            {
                return 0.0f;
            }

            float shortSide = Math.Min(horizontal, vertical);
            float longSide = Math.Max(horizontal, vertical);
            float observedRatio = shortSide / longSide;
            const float expectedRatio = 2.5f / 3.5f;

            // Perspective can distort the apparent ratio, so this is a soft score.
            float aspectScore = 1.0f - Math.Clamp(
                MathF.Abs(observedRatio - expectedRatio) / 0.32f,
                0.0f,
                1.0f);

            float oppositeSimilarity =
                1.0f - Math.Clamp(
                    (MathF.Abs(topLength - bottomLength) /
                     Math.Max(topLength, bottomLength)) +
                    (MathF.Abs(leftLength - rightLength) /
                     Math.Max(leftLength, rightLength)),
                    0.0f,
                    1.0f);

            FloatPoint center = new(
                corners.Average(point => point.X),
                corners.Average(point => point.Y));

            float centerDistance = MathF.Sqrt(
                MathF.Pow((center.X - (width * 0.5f)) / width, 2.0f) +
                MathF.Pow((center.Y - (height * 0.5f)) / height, 2.0f));

            float centerScore = 1.0f - Math.Clamp(centerDistance / 0.55f, 0.0f, 1.0f);

            float voteScore = Math.Clamp(
                (firstPair.Score + secondPair.Score) /
                (float)(Math.Max(width, height) * 1.6f),
                0.0f,
                1.0f);

            float areaScore = Math.Clamp(
                (areaFraction - 0.12f) / 0.48f,
                0.0f,
                1.0f);

            return Math.Clamp(
                (voteScore * 0.30f) +
                (aspectScore * 0.24f) +
                (oppositeSimilarity * 0.16f) +
                (centerScore * 0.14f) +
                (areaScore * 0.16f),
                0.0f,
                1.0f);
        }

        private static SixLabors.ImageSharp.Image<Rgba32> WarpCardToStandardRectangle(
            SixLabors.ImageSharp.Image<Rgba32> source,
            FloatPoint[] sourceCorners)
        {
            FloatPoint[] ordered = OrderCorners(sourceCorners);

            FloatPoint[] destinationCorners =
            {
                new(0.0f, 0.0f),
                new(ProcessingWidth - 1.0f, 0.0f),
                new(ProcessingWidth - 1.0f, ProcessingHeight - 1.0f),
                new(0.0f, ProcessingHeight - 1.0f)
            };

            // Solve destination -> source so every output pixel can sample the
            // original photograph directly without needing a matrix inversion.
            double[] homography = SolveHomography(
                destinationCorners,
                ordered);

            Rgba32[] sourcePixels = new Rgba32[source.Width * source.Height];

            source.ProcessPixelRows(sourceAccessor =>
            {
                for (int y = 0; y < source.Height; y++)
                {
                    sourceAccessor.GetRowSpan(y).CopyTo(
                        sourcePixels.AsSpan(y * source.Width, source.Width));
                }
            });

            var output = new SixLabors.ImageSharp.Image<Rgba32>(
                ProcessingWidth,
                ProcessingHeight);

            output.ProcessPixelRows(outputAccessor =>
            {
                for (int y = 0; y < ProcessingHeight; y++)
                {
                    Span<Rgba32> outputRow = outputAccessor.GetRowSpan(y);

                    for (int x = 0; x < ProcessingWidth; x++)
                    {
                        double denominator =
                            (homography[6] * x) +
                            (homography[7] * y) + 1.0;

                        if (Math.Abs(denominator) < 1e-9)
                        {
                            outputRow[x] = default;
                            continue;
                        }

                        float sourceX = (float)(
                            ((homography[0] * x) +
                             (homography[1] * y) +
                             homography[2]) /
                            denominator);

                        float sourceY = (float)(
                            ((homography[3] * x) +
                             (homography[4] * y) +
                             homography[5]) /
                            denominator);

                        outputRow[x] = SampleBilinear(
                            sourcePixels,
                            source.Width,
                            source.Height,
                            sourceX,
                            sourceY);
                    }
                }
            });

            return output;
        }

        private static Rgba32 SampleBilinear(
            Rgba32[] pixels,
            int width,
            int height,
            float x,
            float y)
        {
            if (x < 0.0f || y < 0.0f || x > width - 1 || y > height - 1)
            {
                return new Rgba32(0, 0, 0, 255);
            }

            int x0 = Math.Clamp((int)MathF.Floor(x), 0, width - 1);
            int y0 = Math.Clamp((int)MathF.Floor(y), 0, height - 1);
            int x1 = Math.Min(x0 + 1, width - 1);
            int y1 = Math.Min(y0 + 1, height - 1);

            float tx = x - x0;
            float ty = y - y0;

            Rgba32 p00 = pixels[(y0 * width) + x0];
            Rgba32 p10 = pixels[(y0 * width) + x1];
            Rgba32 p01 = pixels[(y1 * width) + x0];
            Rgba32 p11 = pixels[(y1 * width) + x1];

            return new Rgba32(
                InterpolateByte(p00.R, p10.R, p01.R, p11.R, tx, ty),
                InterpolateByte(p00.G, p10.G, p01.G, p11.G, tx, ty),
                InterpolateByte(p00.B, p10.B, p01.B, p11.B, tx, ty),
                255);
        }

        private static byte InterpolateByte(
            byte topLeft,
            byte topRight,
            byte bottomLeft,
            byte bottomRight,
            float tx,
            float ty)
        {
            float top = topLeft + ((topRight - topLeft) * tx);
            float bottom = bottomLeft + ((bottomRight - bottomLeft) * tx);
            float value = top + ((bottom - top) * ty);

            return (byte)Math.Clamp(MathF.Round(value), 0.0f, 255.0f);
        }

        private static double[] SolveHomography(
            IReadOnlyList<FloatPoint> sourcePoints,
            IReadOnlyList<FloatPoint> destinationPoints)
        {
            if (sourcePoints.Count != 4 || destinationPoints.Count != 4)
            {
                throw new ArgumentException("Exactly four point pairs are required.");
            }

            double[,] matrix = new double[8, 9];

            for (int index = 0; index < 4; index++)
            {
                double x = sourcePoints[index].X;
                double y = sourcePoints[index].Y;
                double u = destinationPoints[index].X;
                double v = destinationPoints[index].Y;

                int row = index * 2;

                matrix[row, 0] = x;
                matrix[row, 1] = y;
                matrix[row, 2] = 1.0;
                matrix[row, 3] = 0.0;
                matrix[row, 4] = 0.0;
                matrix[row, 5] = 0.0;
                matrix[row, 6] = -u * x;
                matrix[row, 7] = -u * y;
                matrix[row, 8] = u;

                matrix[row + 1, 0] = 0.0;
                matrix[row + 1, 1] = 0.0;
                matrix[row + 1, 2] = 0.0;
                matrix[row + 1, 3] = x;
                matrix[row + 1, 4] = y;
                matrix[row + 1, 5] = 1.0;
                matrix[row + 1, 6] = -v * x;
                matrix[row + 1, 7] = -v * y;
                matrix[row + 1, 8] = v;
            }

            // Gaussian elimination with partial pivoting.
            for (int pivot = 0; pivot < 8; pivot++)
            {
                int bestRow = pivot;
                double bestValue = Math.Abs(matrix[pivot, pivot]);

                for (int row = pivot + 1; row < 8; row++)
                {
                    double candidate = Math.Abs(matrix[row, pivot]);

                    if (candidate > bestValue)
                    {
                        bestValue = candidate;
                        bestRow = row;
                    }
                }

                if (bestValue < 1e-10)
                {
                    throw new InvalidOperationException(
                        "The detected card geometry could not be normalized.");
                }

                if (bestRow != pivot)
                {
                    for (int column = pivot; column < 9; column++)
                    {
                        (matrix[pivot, column], matrix[bestRow, column]) =
                            (matrix[bestRow, column], matrix[pivot, column]);
                    }
                }

                double pivotValue = matrix[pivot, pivot];

                for (int column = pivot; column < 9; column++)
                {
                    matrix[pivot, column] /= pivotValue;
                }

                for (int row = 0; row < 8; row++)
                {
                    if (row == pivot)
                    {
                        continue;
                    }

                    double factor = matrix[row, pivot];

                    for (int column = pivot; column < 9; column++)
                    {
                        matrix[row, column] -= factor * matrix[pivot, column];
                    }
                }
            }

            double[] result = new double[8];

            for (int row = 0; row < 8; row++)
            {
                result[row] = matrix[row, 8];
            }

            return result;
        }

        /// <summary>
        /// Removes residual translation after perspective correction. The
        /// structural image is downsampled for the search, then the winning
        /// offset is applied to the full-resolution luminance array.
        /// </summary>
        private static float FineAlignToReference(
            float[] reference,
            float[] moving)
        {
            const int downsampleFactor = 5;
            int smallWidth = ProcessingWidth / downsampleFactor;
            int smallHeight = ProcessingHeight / downsampleFactor;

            float[] smallReference = Downsample(
                reference,
                ProcessingWidth,
                ProcessingHeight,
                downsampleFactor);
            float[] smallMoving = Downsample(
                moving,
                ProcessingWidth,
                ProcessingHeight,
                downsampleFactor);

            float[] referenceEdges = GradientMagnitude(
                smallReference,
                smallWidth,
                smallHeight);
            float[] movingEdges = GradientMagnitude(
                smallMoving,
                smallWidth,
                smallHeight);

            int bestDx = 0;
            int bestDy = 0;
            float bestScore = float.NegativeInfinity;

            const int searchRadius = 7;

            for (int dy = -searchRadius; dy <= searchRadius; dy++)
            {
                for (int dx = -searchRadius; dx <= searchRadius; dx++)
                {
                    float score = CalculateNormalizedCorrelation(
                        referenceEdges,
                        movingEdges,
                        smallWidth,
                        smallHeight,
                        dx,
                        dy);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestDx = dx;
                        bestDy = dy;
                    }
                }
            }

            int fullDx = bestDx * downsampleFactor;
            int fullDy = bestDy * downsampleFactor;

            if (fullDx != 0 || fullDy != 0)
            {
                ShiftInPlace(
                    moving,
                    ProcessingWidth,
                    ProcessingHeight,
                    fullDx,
                    fullDy);
            }

            return Math.Clamp((bestScore + 1.0f) * 0.5f, 0.0f, 1.0f);
        }

        private static float CalculateNormalizedCorrelation(
            float[] reference,
            float[] moving,
            int width,
            int height,
            int dx,
            int dy)
        {
            int marginX = Math.Max(8, width / 12);
            int marginY = Math.Max(8, height / 12);

            double sumA = 0.0;
            double sumB = 0.0;
            double sumAA = 0.0;
            double sumBB = 0.0;
            double sumAB = 0.0;
            int count = 0;

            for (int y = marginY; y < height - marginY; y += 2)
            {
                int movingY = y + dy;

                if (movingY < marginY || movingY >= height - marginY)
                {
                    continue;
                }

                for (int x = marginX; x < width - marginX; x += 2)
                {
                    int movingX = x + dx;

                    if (movingX < marginX || movingX >= width - marginX)
                    {
                        continue;
                    }

                    float a = reference[(y * width) + x];
                    float b = moving[(movingY * width) + movingX];

                    sumA += a;
                    sumB += b;
                    sumAA += a * a;
                    sumBB += b * b;
                    sumAB += a * b;
                    count++;
                }
            }

            if (count < 100)
            {
                return -1.0f;
            }

            double numerator = sumAB - ((sumA * sumB) / count);
            double denominatorA = sumAA - ((sumA * sumA) / count);
            double denominatorB = sumBB - ((sumB * sumB) / count);
            double denominator = Math.Sqrt(
                Math.Max(denominatorA * denominatorB, 0.0));

            if (denominator <= 1e-9)
            {
                return -1.0f;
            }

            return (float)Math.Clamp(numerator / denominator, -1.0, 1.0);
        }

        private static void ShiftInPlace(
            float[] values,
            int width,
            int height,
            int dx,
            int dy)
        {
            float[] original = values.ToArray();

            for (int y = 0; y < height; y++)
            {
                int sourceY = Math.Clamp(y + dy, 0, height - 1);

                for (int x = 0; x < width; x++)
                {
                    int sourceX = Math.Clamp(x + dx, 0, width - 1);
                    values[(y * width) + x] =
                        original[(sourceY * width) + sourceX];
                }
            }
        }

        private static float[] ExtractLuminance(
            SixLabors.ImageSharp.Image<Rgba32> image)
        {
            float[] luminance = new float[image.Width * image.Height];

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < image.Height; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);

                    for (int x = 0; x < image.Width; x++)
                    {
                        Rgba32 pixel = row[x];

                        luminance[(y * image.Width) + x] =
                            ((0.2126f * pixel.R) +
                             (0.7152f * pixel.G) +
                             (0.0722f * pixel.B)) / 255.0f;
                    }
                }
            });

            return luminance;
        }

        private static float[] Downsample(
            float[] source,
            int width,
            int height,
            int factor)
        {
            int outputWidth = width / factor;
            int outputHeight = height / factor;
            float[] output = new float[outputWidth * outputHeight];

            for (int y = 0; y < outputHeight; y++)
            {
                for (int x = 0; x < outputWidth; x++)
                {
                    double sum = 0.0;
                    int count = 0;

                    for (int yy = 0; yy < factor; yy++)
                    {
                        int sourceY = (y * factor) + yy;

                        for (int xx = 0; xx < factor; xx++)
                        {
                            int sourceX = (x * factor) + xx;
                            sum += source[(sourceY * width) + sourceX];
                            count++;
                        }
                    }

                    output[(y * outputWidth) + x] =
                        count == 0 ? 0.0f : (float)(sum / count);
                }
            }

            return output;
        }

        private static float[] GradientMagnitude(
            float[] source,
            int width,
            int height)
        {
            CalculateSobel(
                source,
                width,
                height,
                out _,
                out _,
                out float[] magnitude);

            return magnitude;
        }

        private static void CalculateSobel(
            float[] source,
            int width,
            int height,
            out float[] gradientX,
            out float[] gradientY,
            out float[] magnitude)
        {
            gradientX = new float[source.Length];
            gradientY = new float[source.Length];
            magnitude = new float[source.Length];

            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    int top = (y - 1) * width;
                    int middle = y * width;
                    int bottom = (y + 1) * width;

                    float gx =
                        -source[top + x - 1] + source[top + x + 1] +
                        (-2.0f * source[middle + x - 1]) +
                        (2.0f * source[middle + x + 1]) +
                        -source[bottom + x - 1] + source[bottom + x + 1];

                    float gy =
                        -source[top + x - 1] +
                        (-2.0f * source[top + x]) +
                        -source[top + x + 1] +
                        source[bottom + x - 1] +
                        (2.0f * source[bottom + x]) +
                        source[bottom + x + 1];

                    int index = middle + x;
                    gradientX[index] = gx;
                    gradientY[index] = gy;
                    magnitude[index] = MathF.Sqrt((gx * gx) + (gy * gy));
                }
            }
        }

        private static float CalculateMean(float[] values)
        {
            double total = 0.0;

            foreach (float value in values)
            {
                total += value;
            }

            return values.Length == 0
                ? 0.0f
                : (float)(total / values.Length);
        }

        private static void NormalizeBrightness(float[] values, float targetMean)
        {
            float currentMean = CalculateMean(values);

            if (currentMean <= Epsilon)
            {
                return;
            }

            float scale = targetMean / currentMean;

            for (int index = 0; index < values.Length; index++)
            {
                values[index] = Math.Clamp(values[index] * scale, 0.0f, 1.0f);
            }
        }

        private static float[] HighPassRelief(float[] relief)
        {
            const int radius = 9;
            float[] blurred = BoxBlur(
                relief,
                ProcessingWidth,
                ProcessingHeight,
                radius);
            float[] local = new float[relief.Length];

            for (int index = 0; index < relief.Length; index++)
            {
                local[index] = MathF.Max(0.0f, relief[index] - blurred[index]);
            }

            return local;
        }

        private static float[] BoxBlur(
            float[] source,
            int width,
            int height,
            int radius)
        {
            float[] horizontal = new float[source.Length];
            float[] output = new float[source.Length];

            for (int y = 0; y < height; y++)
            {
                double sum = 0.0;

                for (int x = -radius; x <= radius; x++)
                {
                    int sampleX = Math.Clamp(x, 0, width - 1);
                    sum += source[(y * width) + sampleX];
                }

                for (int x = 0; x < width; x++)
                {
                    horizontal[(y * width) + x] =
                        (float)(sum / ((radius * 2) + 1));

                    int removeX = Math.Clamp(x - radius, 0, width - 1);
                    int addX = Math.Clamp(x + radius + 1, 0, width - 1);

                    sum -= source[(y * width) + removeX];
                    sum += source[(y * width) + addX];
                }
            }

            for (int x = 0; x < width; x++)
            {
                double sum = 0.0;

                for (int y = -radius; y <= radius; y++)
                {
                    int sampleY = Math.Clamp(y, 0, height - 1);
                    sum += horizontal[(sampleY * width) + x];
                }

                for (int y = 0; y < height; y++)
                {
                    output[(y * width) + x] =
                        (float)(sum / ((radius * 2) + 1));

                    int removeY = Math.Clamp(y - radius, 0, height - 1);
                    int addY = Math.Clamp(y + radius + 1, 0, height - 1);

                    sum -= horizontal[(removeY * width) + x];
                    sum += horizontal[(addY * width) + x];
                }
            }

            return output;
        }

        private static float CalculatePercentile(float[] values, float percentile)
        {
            if (values.Length == 0)
            {
                return 0.0f;
            }

            float[] ordered = values.ToArray();
            Array.Sort(ordered);

            int index = (int)Math.Clamp(
                Math.Round((ordered.Length - 1) * percentile),
                0,
                ordered.Length - 1);

            return ordered[index];
        }

        private static double CalculateAnomalyScore(float[] values, float threshold)
        {
            if (threshold <= Epsilon)
            {
                return 0.0;
            }

            int strongPixels = 0;
            double excess = 0.0;

            foreach (float value in values)
            {
                if (value <= threshold)
                {
                    continue;
                }

                strongPixels++;
                excess += (value - threshold) / threshold;
            }

            if (strongPixels == 0)
            {
                return 0.0;
            }

            double areaRatio = (double)strongPixels / values.Length;
            double averageExcess = excess / strongPixels;

            return Math.Clamp(
                (areaRatio * 900.0) +
                (averageExcess * 12.0),
                0.0,
                100.0);
        }

        private static double CalculateExposureConsistency(params float[][] images)
        {
            float[] means = images.Select(CalculateMean).ToArray();
            double average = means.Average(value => (double)value);

            if (average <= Epsilon)
            {
                return 0.0;
            }

            double variance = means
                .Select(value => Math.Pow(value - average, 2.0))
                .Average();

            double coefficientOfVariation = Math.Sqrt(variance) / average;

            return Math.Clamp(
                100.0 - (coefficientOfVariation * 180.0),
                0.0,
                100.0);
        }

        private static double CalculateRegistrationScore(
            IReadOnlyList<float> detectionConfidence,
            IReadOnlyList<float> alignmentConfidence)
        {
            double detection = detectionConfidence.Average(value => (double)value);
            double alignment = alignmentConfidence.Average(value => (double)value);

            // Detection is intentionally weighted more heavily because a wrong
            // quadrilateral creates a larger photometric error than a 1-2 pixel
            // residual translation.
            return Math.Clamp(
                ((detection * 0.65) + (alignment * 0.35)) * 100.0,
                0.0,
                100.0);
        }

        private static InspectionDebugFrame BuildDebugFrame(
            RegisteredCardFrame capture,
            SurfaceLightDirection direction)
        {
            return new InspectionDebugFrame
            {
                Direction = direction,
                DetectionOverlayPath = capture.DetectionOverlayPath,
                RegisteredImagePath = capture.RegisteredImagePath,
                EdgeImagePath = capture.EdgeImagePath,
                DetectionConfidence = Math.Round(capture.Geometry.Confidence * 100.0, 0),
                AlignmentConfidence = Math.Round(capture.AlignmentConfidence, 0),
                RotationDegrees = capture.RotationDegrees,
                Scale = capture.Scale,
                OffsetX = capture.OffsetX,
                OffsetY = capture.OffsetY
            };
        }

        private static async Task SaveAverageVisualizationAsync(
            IReadOnlyList<float[]> images,
            string path,
            CancellationToken cancellationToken)
        {
            int pixelCount = ProcessingWidth * ProcessingHeight;
            float[] average = new float[pixelCount];

            foreach (float[] image in images)
            {
                for (int index = 0; index < pixelCount; index++)
                {
                    average[index] += image[index];
                }
            }

            float scale = images.Count == 0 ? 1.0f : 1.0f / images.Count;

            for (int index = 0; index < pixelCount; index++)
            {
                average[index] *= scale;
            }

            await SaveGrayscaleAsync(average, path, cancellationToken);
        }

        private static async Task SaveColorEdgeOverlayAsync(
            float[] top,
            float[] right,
            float[] bottom,
            float[] left,
            string path,
            CancellationToken cancellationToken)
        {
            float topMax = Math.Max(top.Max(), Epsilon);
            float rightMax = Math.Max(right.Max(), Epsilon);
            float bottomMax = Math.Max(bottom.Max(), Epsilon);
            float leftMax = Math.Max(left.Max(), Epsilon);

            using var image = new SixLabors.ImageSharp.Image<Rgba32>(ProcessingWidth, ProcessingHeight);

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < ProcessingHeight; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);

                    for (int x = 0; x < ProcessingWidth; x++)
                    {
                        int index = (y * ProcessingWidth) + x;

                        float topValue = Math.Clamp(top[index] / topMax, 0.0f, 1.0f);
                        float rightValue = Math.Clamp(right[index] / rightMax, 0.0f, 1.0f);
                        float bottomValue = Math.Clamp(bottom[index] / bottomMax, 0.0f, 1.0f);
                        float leftValue = Math.Clamp(left[index] / leftMax, 0.0f, 1.0f);

                        byte red = (byte)Math.Clamp((topValue + leftValue) * 255.0f, 0.0f, 255.0f);
                        byte green = (byte)Math.Clamp((rightValue + leftValue) * 255.0f, 0.0f, 255.0f);
                        byte blue = (byte)Math.Clamp(bottomValue * 255.0f, 0.0f, 255.0f);

                        row[x] = new Rgba32(red, green, blue, 255);
                    }
                }
            });

            await using FileStream outputStream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);

            await image.SaveAsync(outputStream, new PngEncoder(), cancellationToken);
        }

        private static async Task SaveDetectionOverlayAsync(
            SixLabors.ImageSharp.Image<Rgba32> source,
            IReadOnlyList<FloatPoint> corners,
            string path,
            CancellationToken cancellationToken)
        {
            using SixLabors.ImageSharp.Image<Rgba32> overlay = source.Clone();
            Rgba32 lineColor = new Rgba32(57, 255, 20, 255);
            Rgba32 pointColor = new Rgba32(255, 64, 64, 255);

            for (int index = 0; index < corners.Count; index++)
            {
                FloatPoint start = corners[index];
                FloatPoint end = corners[(index + 1) % corners.Count];
                DrawLine(overlay, start, end, lineColor, thickness: 3);
                DrawPoint(overlay, start, pointColor, radius: 7);
            }

            await using FileStream outputStream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);

            await overlay.SaveAsync(outputStream, new PngEncoder(), cancellationToken);
        }

        private static void DrawPoint(
            SixLabors.ImageSharp.Image<Rgba32> image,
            FloatPoint point,
            Rgba32 color,
            int radius)
        {
            int centerX = (int)Math.Round(point.X);
            int centerY = (int)Math.Round(point.Y);

            for (int y = centerY - radius; y <= centerY + radius; y++)
            {
                for (int x = centerX - radius; x <= centerX + radius; x++)
                {
                    if ((uint)x >= (uint)image.Width || (uint)y >= (uint)image.Height)
                    {
                        continue;
                    }

                    int dx = x - centerX;
                    int dy = y - centerY;

                    if ((dx * dx) + (dy * dy) <= radius * radius)
                    {
                        image[x, y] = color;
                    }
                }
            }
        }

        private static void DrawLine(
            SixLabors.ImageSharp.Image<Rgba32> image,
            FloatPoint start,
            FloatPoint end,
            Rgba32 color,
            int thickness)
        {
            int x0 = (int)Math.Round(start.X);
            int y0 = (int)Math.Round(start.Y);
            int x1 = (int)Math.Round(end.X);
            int y1 = (int)Math.Round(end.Y);

            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                DrawPoint(image, new FloatPoint(x0, y0), color, thickness);

                if (x0 == x1 && y0 == y1)
                {
                    break;
                }

                int twiceError = err * 2;

                if (twiceError > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }

                if (twiceError < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        private static async Task SaveGrayscaleAsync(
            float[] values,
            string path,
            CancellationToken cancellationToken,
            bool normalize = false)
        {
            float maximum = normalize
                ? Math.Max(values.Max(), Epsilon)
                : 1.0f;

            using var image = new SixLabors.ImageSharp.Image<Rgba32>(
                ProcessingWidth,
                ProcessingHeight);

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < ProcessingHeight; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);

                    for (int x = 0; x < ProcessingWidth; x++)
                    {
                        float normalized = Math.Clamp(
                            values[(y * ProcessingWidth) + x] / maximum,
                            0.0f,
                            1.0f);

                        byte intensity = (byte)(normalized * 255.0f);
                        row[x] = new Rgba32(intensity, intensity, intensity, 255);
                    }
                }
            });

            await using FileStream outputStream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);

            await image.SaveAsync(outputStream, new PngEncoder(), cancellationToken);
        }

        private static async Task SaveHeatmapAsync(
            float[] values,
            float threshold,
            string path,
            CancellationToken cancellationToken)
        {
            float high = Math.Max(
                CalculatePercentile(values, 0.995f),
                threshold + Epsilon);

            using var image = new SixLabors.ImageSharp.Image<Rgba32>(
                ProcessingWidth,
                ProcessingHeight);

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < ProcessingHeight; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);

                    for (int x = 0; x < ProcessingWidth; x++)
                    {
                        float value = values[(y * ProcessingWidth) + x];

                        if (value <= threshold)
                        {
                            byte baseLevel = (byte)Math.Clamp(
                                (value / Math.Max(threshold, Epsilon)) * 55.0f,
                                0.0f,
                                55.0f);

                            row[x] = new Rgba32(0, baseLevel, 48, 255);
                            continue;
                        }

                        float normalized = Math.Clamp(
                            (value - threshold) / (high - threshold),
                            0.0f,
                            1.0f);

                        byte red = 255;
                        byte green = (byte)(210.0f * (1.0f - normalized));
                        byte blue = (byte)(20.0f * (1.0f - normalized));
                        row[x] = new Rgba32(red, green, blue, 255);
                    }
                }
            });

            await using FileStream outputStream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);

            await image.SaveAsync(outputStream, new PngEncoder(), cancellationToken);
        }

        private static string BuildSummary(
            double anomalyScore,
            double consistencyScore,
            double registrationScore)
        {
            if (registrationScore < 60.0)
            {
                return "The card was found, but automatic alignment confidence was low. Retake the scan with all four card edges visible before trusting the surface map.";
            }

            if (consistencyScore < 65.0)
            {
                return "Capture quality is low. Retake the scan before trusting highlighted surface areas.";
            }

            if (anomalyScore < 20.0)
            {
                return "No strong directional surface anomalies were detected in this scan.";
            }

            if (anomalyScore < 50.0)
            {
                return "Some localized surface anomalies were highlighted. Review the full-card heatmap at full resolution.";
            }

            return "Multiple or strong surface anomalies were highlighted. Review each area before drawing a condition conclusion.";
        }

        private static bool TryIntersectLines(
            HoughLine first,
            HoughLine second,
            out FloatPoint point)
        {
            float c1 = MathF.Cos(first.ThetaRadians);
            float s1 = MathF.Sin(first.ThetaRadians);
            float c2 = MathF.Cos(second.ThetaRadians);
            float s2 = MathF.Sin(second.ThetaRadians);

            float determinant = (c1 * s2) - (s1 * c2);

            if (MathF.Abs(determinant) < 0.0001f)
            {
                point = default;
                return false;
            }

            float x = ((first.Rho * s2) - (s1 * second.Rho)) / determinant;
            float y = ((c1 * second.Rho) - (first.Rho * c2)) / determinant;

            point = new FloatPoint(x, y);
            return true;
        }

        private static FloatPoint[] OrderCorners(IEnumerable<FloatPoint> points)
        {
            FloatPoint[] array = points.ToArray();

            if (array.Length != 4)
            {
                throw new ArgumentException("Exactly four corners are required.");
            }

            FloatPoint topLeft = array.MinBy(point => point.X + point.Y);
            FloatPoint bottomRight = array.MaxBy(point => point.X + point.Y);
            FloatPoint topRight = array.MaxBy(point => point.X - point.Y);
            FloatPoint bottomLeft = array.MinBy(point => point.X - point.Y);

            return new[]
            {
                topLeft,
                topRight,
                bottomRight,
                bottomLeft
            };
        }

        private static float PolygonArea(IReadOnlyList<FloatPoint> points)
        {
            double area = 0.0;

            for (int index = 0; index < points.Count; index++)
            {
                FloatPoint current = points[index];
                FloatPoint next = points[(index + 1) % points.Count];
                area += (current.X * next.Y) - (next.X * current.Y);
            }

            return (float)Math.Abs(area * 0.5);
        }

        private static float Distance(FloatPoint a, FloatPoint b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return MathF.Sqrt((dx * dx) + (dy * dy));
        }

        private static float AverageLineAngleDegrees(float a, float b)
        {
            float radiansA = DegreesToRadians(a * 2.0f);
            float radiansB = DegreesToRadians(b * 2.0f);
            float x = MathF.Cos(radiansA) + MathF.Cos(radiansB);
            float y = MathF.Sin(radiansA) + MathF.Sin(radiansB);

            float average = RadiansToDegrees(MathF.Atan2(y, x)) * 0.5f;

            if (average < 0.0f)
            {
                average += 180.0f;
            }

            return average;
        }

        private static float AbsoluteAngleDifferenceDegrees(float a, float b)
        {
            float difference = MathF.Abs(a - b) % 180.0f;
            return difference > 90.0f ? 180.0f - difference : difference;
        }

        private static float DegreesToRadians(float degrees)
        {
            return degrees * (MathF.PI / 180.0f);
        }

        private static float RadiansToDegrees(float radians)
        {
            return radians * (180.0f / MathF.PI);
        }

        private sealed class RegisteredCapture
        {
            public SurfaceLightDirection Direction { get; set; }
            public float[] Luminance { get; set; } = Array.Empty<float>();
            public float DetectionConfidence { get; set; }
            public float AlignmentConfidence { get; set; }
            public string DetectionOverlayPath { get; set; } = string.Empty;
            public string RegisteredImagePath { get; set; } = string.Empty;
            public string EdgeImagePath { get; set; } = string.Empty;
        }

        private sealed class DetectionResult
        {
            public static DetectionResult Failed => new()
            {
                Success = false,
                Corners = Array.Empty<FloatPoint>(),
                Confidence = 0.0f
            };

            public bool Success { get; set; }
            public FloatPoint[] Corners { get; set; } = Array.Empty<FloatPoint>();
            public float Confidence { get; set; }
        }

        private sealed class QuadCandidate
        {
            public FloatPoint[] Corners { get; set; } = Array.Empty<FloatPoint>();
            public float Score { get; set; }
        }

        private sealed class HoughLine
        {
            public float ThetaDegrees { get; set; }
            public float ThetaRadians { get; set; }
            public float Rho { get; set; }
            public int Votes { get; set; }
        }

        private sealed class LinePair
        {
            public HoughLine First { get; set; } = null!;
            public HoughLine Second { get; set; } = null!;
            public float AverageThetaDegrees { get; set; }
            public int Score { get; set; }
        }

        private readonly record struct FloatPoint(float X, float Y);
    }
}
