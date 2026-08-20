using CollectIQ.Interfaces;
using CollectIQ.Models.Inspection.Geometry;
using CollectIQ.Models.Inspection.Registration;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace CollectIQ.Services.Inspection.Registration
{
    /// <summary>
    /// Converts handheld card photographs into one canonical coordinate system.
    /// Stage 1 uses the four physical card corners and a projective homography.
    /// Stage 2 performs a tightly constrained similarity refinement against a
    /// reference card to correct residual corner-detection error.
    /// </summary>
    public sealed class CardRegistrationService : ICardRegistrationService
    {
        public const int CanonicalWidth = 750;
        public const int CanonicalHeight = 1050;
        private const float Epsilon = 0.0001f;

        private readonly ICardGeometryService geometryService;

        public CardRegistrationService(ICardGeometryService geometryService)
        {
            this.geometryService = geometryService;
        }

        public async Task<CardRegistrationResult> RegisterAsync(
            IReadOnlyDictionary<string, string> captures,
            string referenceKey,
            string outputDirectory,
            CancellationToken cancellationToken = default)
        {
            if (!captures.ContainsKey(referenceKey))
            {
                throw new ArgumentException("The requested reference capture does not exist.", nameof(referenceKey));
            }

            Directory.CreateDirectory(outputDirectory);
            Dictionary<string, RegisteredCardFrame> frames = new(StringComparer.OrdinalIgnoreCase);

            // Always solve the neutral/reference image first. The directional
            // captures are then located relative to this known physical card
            // geometry instead of rediscovering the card under moving glare.
            RegisteredCardFrame reference = await RectifyAsync(
                referenceKey,
                captures[referenceKey],
                outputDirectory,
                cancellationToken);
            frames[referenceKey] = reference;
            reference.AlignmentConfidence = 100.0;

            CardPoint[] normalizedReferenceCorners = reference.Geometry.Corners
                .Select(point => new CardPoint(
                    point.X / Math.Max(reference.Geometry.SourceWidth - 1.0f, 1.0f),
                    point.Y / Math.Max(reference.Geometry.SourceHeight - 1.0f, 1.0f)))
                .ToArray();

            foreach (KeyValuePair<string, string> capture in captures)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (capture.Key.Equals(referenceKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                frames[capture.Key] = await RectifyNearReferenceAsync(
                    capture.Key,
                    capture.Value,
                    normalizedReferenceCorners,
                    outputDirectory,
                    cancellationToken);
            }

            foreach (RegisteredCardFrame frame in frames.Values)
            {
                if (ReferenceEquals(frame, reference))
                {
                    continue;
                }

                SimilarityMatch match = FindBestTranslation(reference.Luminance, frame.Luminance);
                frame.AlignmentConfidence = match.Confidence * 100.0;
                frame.RotationDegrees = match.AngleDegrees;
                frame.Scale = match.Scale;
                frame.OffsetX = match.OffsetX;
                frame.OffsetY = match.OffsetY;

                frame.Luminance = ApplySimilarity(
                    frame.Luminance,
                    CanonicalWidth,
                    CanonicalHeight,
                    match);

                await ApplySimilarityToImageAsync(
                    frame.RegisteredImagePath,
                    match,
                    cancellationToken);
            }

            foreach (RegisteredCardFrame frame in frames.Values)
            {
                frame.EdgeImagePath = Path.Combine(outputDirectory, $"edge_{SafeKey(frame.Key)}.png");
                float[] edges = GradientMagnitude(frame.Luminance, CanonicalWidth, CanonicalHeight);
                await SaveGrayscaleAsync(edges, frame.EdgeImagePath, cancellationToken, normalize: true);
            }

            string edgeOverlayPath = Path.Combine(outputDirectory, "registration_edge_overlay.png");
            string averagePath = Path.Combine(outputDirectory, "registration_average.png");

            List<RegisteredCardFrame> directionalFrames = frames.Values
                .Where(frame => !frame.Key.Equals(referenceKey, StringComparison.OrdinalIgnoreCase))
                .ToList();

            await SaveColorOverlayAsync(directionalFrames, edgeOverlayPath, cancellationToken);
            List<float[]> averageFrames = new() { reference.Luminance };
            averageFrames.AddRange(directionalFrames.Select(frame => frame.Luminance));
            await SaveAverageAsync(averageFrames, averagePath, cancellationToken);

            double geometryQuality = directionalFrames.Count == 0
                ? reference.Geometry.Confidence * 100.0
                : directionalFrames.Average(frame => frame.Geometry.Confidence * 100.0);
            double alignmentQuality = directionalFrames.Count == 0
                ? 100.0
                : directionalFrames.Average(frame => frame.AlignmentConfidence);

            return new CardRegistrationResult
            {
                Frames = frames,
                EdgeOverlayPath = edgeOverlayPath,
                AlignmentAveragePath = averagePath,
                OverallQuality = Math.Clamp((geometryQuality * 0.45) + (alignmentQuality * 0.55), 0.0, 100.0)
            };
        }

        private async Task<RegisteredCardFrame> RectifyAsync(
            string key,
            string path,
            string outputDirectory,
            CancellationToken cancellationToken)
        {
            using SixLabors.ImageSharp.Image<Rgba32> source = await ImageSharpImage.LoadAsync<Rgba32>(path, cancellationToken);
            source.Mutate(context => context.AutoOrient());

            CardGeometryResult geometry = geometryService.DetectCard(source);
            if (!geometry.Success || geometry.Corners.Length != 4)
            {
                throw new InvalidOperationException(
                    $"CollectIQ could not find the four outer card corners in the {key} capture. Keep the complete card visible against a contrasting background and retake that image.");
            }

            string safeKey = SafeKey(key);
            string detectionPath = Path.Combine(outputDirectory, $"detected_{safeKey}.png");
            string rectifiedPath = Path.Combine(outputDirectory, $"registered_{safeKey}.png");

            await SaveDetectionOverlayAsync(source, geometry.Corners, detectionPath, cancellationToken);

            using SixLabors.ImageSharp.Image<Rgba32> rectified = WarpToCanonical(source, geometry.Corners);
            await SaveImageAsync(rectified, rectifiedPath, cancellationToken);

            return new RegisteredCardFrame
            {
                Key = key,
                SourcePath = path,
                DetectionOverlayPath = detectionPath,
                RegisteredImagePath = rectifiedPath,
                Geometry = geometry,
                Luminance = ExtractLuminance(rectified)
            };
        }

        /// <summary>
        /// Finds the physical rectangle again in every capture, but uses the neutral
        /// reference as a strong prior. Each image is independently perspective-warped
        /// from its own four physical corners into the exact same canonical matrix.
        /// </summary>
        private async Task<RegisteredCardFrame> RectifyNearReferenceAsync(
            string key,
            string path,
            IReadOnlyList<CardPoint> normalizedReferenceCorners,
            string outputDirectory,
            CancellationToken cancellationToken)
        {
            using SixLabors.ImageSharp.Image<Rgba32> source =
                await ImageSharpImage.LoadAsync<Rgba32>(path, cancellationToken);
            source.Mutate(context => context.AutoOrient());

            CardGeometryResult geometry =
                geometryService.DetectCardNearPrior(source, normalizedReferenceCorners);

            if (!geometry.Success || geometry.Corners.Length != 4)
            {
                throw new InvalidOperationException(
                    $"CollectIQ could not find the physical card rectangle in the {key} capture. " +
                    "Keep all four card edges visible against the solid background and retake it.");
            }

            string safeKey = SafeKey(key);
            string detectionPath = Path.Combine(outputDirectory, $"detected_{safeKey}.png");
            string rectifiedPath = Path.Combine(outputDirectory, $"registered_{safeKey}.png");

            await SaveDetectionOverlayAsync(source, geometry.Corners, detectionPath, cancellationToken);

            using SixLabors.ImageSharp.Image<Rgba32> rectified =
                WarpToCanonical(source, geometry.Corners);
            await SaveImageAsync(rectified, rectifiedPath, cancellationToken);

            return new RegisteredCardFrame
            {
                Key = key,
                SourcePath = path,
                DetectionOverlayPath = detectionPath,
                RegisteredImagePath = rectifiedPath,
                Geometry = geometry,
                Luminance = ExtractLuminance(rectified)
            };
        }

        private static SixLabors.ImageSharp.Image<Rgba32> WarpToCanonical(
            SixLabors.ImageSharp.Image<Rgba32> source,
            IReadOnlyList<CardPoint> sourceCorners)
        {
            // CardGeometryService returns corners in upright image order:
            // top-left, top-right, bottom-right, bottom-left.
            CardPoint[] destination =
            {
                new(0.0f, 0.0f),
                new(CanonicalWidth - 1.0f, 0.0f),
                new(CanonicalWidth - 1.0f, CanonicalHeight - 1.0f),
                new(0.0f, CanonicalHeight - 1.0f)
            };

            // Inverse mapping: destination/canonical coordinates -> source photo.
            double[] matrix = SolveHomography(destination, sourceCorners);
            Rgba32[] sourcePixels = CopyPixels(source);
            SixLabors.ImageSharp.Image<Rgba32> output = new(CanonicalWidth, CanonicalHeight);

            output.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < CanonicalHeight; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < CanonicalWidth; x++)
                    {
                        MapProjective(matrix, x, y, out float sourceX, out float sourceY);
                        row[x] = SampleBilinear(sourcePixels, source.Width, source.Height, sourceX, sourceY);
                    }
                }
            });

            return output;
        }

        /// <summary>
        /// External-light capture requires the card and phone to remain fixed. After
        /// the reference homography has been reused, residual registration should be
        /// limited to small X/Y framing jitter only. Allowing rotation and scale here
        /// caused glare to pull otherwise correct frames by 4-5% and 30-42 pixels.
        /// </summary>
        private static SimilarityMatch FindBestTranslation(float[] reference, float[] moving)
        {
            const int factor = 3;
            const int maximumFullResolutionShift = 10;
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
                    double score = CorrelateTransformed(
                        referenceEdges,
                        movingEdges,
                        width,
                        height,
                        0.0,
                        1.0,
                        dx,
                        dy);

                    // Prefer the smallest correction when scores are effectively tied.
                    double penalty = (Math.Abs(dx) + Math.Abs(dy)) * 0.0005;
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
            double angle = match.AngleDegrees * Math.PI / 180.0;
            double cos = Math.Cos(angle); double sin = Math.Sin(angle);
            double cx = (width - 1) * 0.5; double cy = (height - 1) * 0.5;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double ox = x - match.OffsetX - cx;
                    double oy = y - match.OffsetY - cy;
                    float sx = (float)(((cos * ox) + (sin * oy)) / match.Scale + cx);
                    float sy = (float)(((-sin * ox) + (cos * oy)) / match.Scale + cy);
                    output[(y * width) + x] = SampleBilinear(source, width, height, sx, sy);
                }
            }
            return output;
        }

        private static async Task ApplySimilarityToImageAsync(string path, SimilarityMatch match, CancellationToken cancellationToken)
        {
            using SixLabors.ImageSharp.Image<Rgba32> source = await ImageSharpImage.LoadAsync<Rgba32>(path, cancellationToken);
            Rgba32[] pixels = CopyPixels(source);
            using SixLabors.ImageSharp.Image<Rgba32> output = new(CanonicalWidth, CanonicalHeight);
            double angle = match.AngleDegrees * Math.PI / 180.0;
            double cos = Math.Cos(angle); double sin = Math.Sin(angle);
            double cx = (CanonicalWidth - 1) * 0.5; double cy = (CanonicalHeight - 1) * 0.5;

            output.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < CanonicalHeight; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < CanonicalWidth; x++)
                    {
                        double ox = x - match.OffsetX - cx;
                        double oy = y - match.OffsetY - cy;
                        float sx = (float)(((cos * ox) + (sin * oy)) / match.Scale + cx);
                        float sy = (float)(((-sin * ox) + (cos * oy)) / match.Scale + cy);
                        row[x] = SampleBilinear(pixels, CanonicalWidth, CanonicalHeight, sx, sy);
                    }
                }
            });

            await SaveImageAsync(output, path, cancellationToken);
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
