using CollectIQ.Models.Inspection;
using CollectIQ.Models.Inspection.Registration;
using OpenCvSharp;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CollectIQ.Services.Inspection
{
    /// <summary>
    /// Fast five-image photometric reconstruction used by the primary Surface inspection.
    /// The geometry/registration stage has already mapped every capture to one canonical card.
    /// This class only reconstructs lighting-independent appearance, surface slopes, normals,
    /// curvature, and spatially-supported scratch/dent candidates.
    /// </summary>
    internal static class FastPhotometricStereoProcessor
    {
        private const int SourceWidth = 750;
        private const int SourceHeight = 1050;
        private const int Width = 500;
        private const int Height = 700;
        private const float Epsilon = 0.0005f;

        internal sealed class Output
        {
            public string AlbedoPath { get; set; } = string.Empty;
            public string ReliefPath { get; set; } = string.Empty;
            public string NormalPath { get; set; } = string.Empty;
            public string CurvaturePath { get; set; } = string.Empty;
            public string ResidualPath { get; set; } = string.Empty;
            public string SlopeXPath { get; set; } = string.Empty;
            public string SlopeYPath { get; set; } = string.Empty;
            public string ScratchPath { get; set; } = string.Empty;
            public string DentPath { get; set; } = string.Empty;
            public string HeatmapPath { get; set; } = string.Empty;
            public string OverlayPath { get; set; } = string.Empty;
            public int RegionCount { get; set; }
            public double AnomalyScore { get; set; }
            public double PhotometricQuality { get; set; }
            public string Summary { get; set; } = string.Empty;
        }

        private sealed class Region
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public int Area { get; set; }
            public float Peak { get; set; }
            public float Mean { get; set; }
            public string Kind { get; set; } = string.Empty;
        }

        public static async Task<Output> ProcessAsync(
            CardRegistrationResult registration,
            string outputDirectory,
            IProgress<SurfaceInspectionProgress>? progress,
            CancellationToken cancellationToken)
        {
            Stopwatch total = Stopwatch.StartNew();
            string stage = "INPUT PREP";
            try
            {
                RegisteredCardFrame referenceFrame = registration.Frames["Reference"];
                RegisteredCardFrame topFrame = registration.Frames[SurfaceLightDirection.Top.ToString()];
                RegisteredCardFrame rightFrame = registration.Frames[SurfaceLightDirection.Right.ToString()];
                RegisteredCardFrame bottomFrame = registration.Frames[SurfaceLightDirection.Bottom.ToString()];
                RegisteredCardFrame leftFrame = registration.Frames[SurfaceLightDirection.Left.ToString()];

                Report(progress, 80, "PHOTOMETRIC INPUTS", "All five cards are flat. Downsampling only the analysis copy for fast photometric reconstruction; the full-resolution normalized cards are preserved for display.", registration.AlignmentAveragePath);
                Stopwatch step = Stopwatch.StartNew();

                float[] reference = ResizeLuminance(referenceFrame.Luminance, SourceWidth, SourceHeight, Width, Height);
                float[] top = ResizeLuminance(topFrame.Luminance, SourceWidth, SourceHeight, Width, Height);
                float[] right = ResizeLuminance(rightFrame.Luminance, SourceWidth, SourceHeight, Width, Height);
                float[] bottom = ResizeLuminance(bottomFrame.Luminance, SourceWidth, SourceHeight, Width, Height);
                float[] left = ResizeLuminance(leftFrame.Luminance, SourceWidth, SourceHeight, Width, Height);

                NormalizeDirectionalBrightness(top, right, bottom, left);
                NormalizeOne(reference, (Mean(top) + Mean(right) + Mean(bottom) + Mean(left)) * 0.25f);
                cancellationToken.ThrowIfCancellationRequested();
                await LogStageAsync("INPUT PREP", step, total, $"Analysis={Width}x{Height}");

                // Base photometric reconstruction -------------------------------------------------
                stage = "ALBEDO + SLOPES";
                step.Restart();
                Report(progress, 83, "ALBEDO + SURFACE SLOPES", "Combining opposite light pairs. Stable printed color cancels; lighting-dependent surface shape remains.");

                int count = Width * Height;
                float[] albedo = new float[count];
                float[] gx = new float[count];
                float[] gy = new float[count];
                float[] residual = new float[count];
                float[] directionalEnergy = new float[count];
                float[] validity = new float[count];

                for (int i = 0; i < count; i++)
                {
                    if ((i & 8191) == 0) cancellationToken.ThrowIfCancellationRequested();
                    float t = top[i], r = right[i], b = bottom[i], l = left[i];
                    Sort4(t, r, b, l, out float lo, out float m1, out float m2, out float hi);
                    float a = (m1 + m2) * 0.5f;
                    albedo[i] = a;
                    gx[i] = (r - l) / (r + l + 0.08f);
                    gy[i] = (b - t) / (b + t + 0.08f);
                    residual[i] = MathF.Abs(reference[i] - a);
                    directionalEnergy[i] = (MathF.Abs(r - l) + MathF.Abs(b - t)) * 0.5f;

                    // Strong clipping/shadow in any light makes that pixel less trustworthy.
                    float lowPenalty = lo < 0.025f ? 0.45f : 1.0f;
                    float highPenalty = hi > 0.985f ? 0.45f : 1.0f;
                    validity[i] = lowPenalty * highPenalty;
                }

                string albedoPath = Path.Combine(outputDirectory, "ps_albedo.jpg");
                await SaveGrayNativeAsync(albedo, Width, Height, albedoPath, cancellationToken, normalize: false);
                Report(progress, 85, "ROBUST ALBEDO", "First real photometric image: the middle-two directional estimate suppresses one-frame glare and shadow while preserving the printed card appearance.", derived: albedoPath);
                await LogStageAsync(stage, step, total);

                // Normal / shaded relief -----------------------------------------------------------
                stage = "NORMALS + RELIEF";
                step.Restart();
                float[] relief = new float[count];
                float[] normalChange = new float[count];
                BuildReliefAndNormalChange(gx, gy, relief, normalChange);

                string reliefPath = Path.Combine(outputDirectory, "ps_relief.jpg");
                string normalPath = Path.Combine(outputDirectory, "ps_normals.jpg");
                string slopeXPath = Path.Combine(outputDirectory, "ps_slope_x.jpg");
                string slopeYPath = Path.Combine(outputDirectory, "ps_slope_y.jpg");
                await SaveGrayNativeAsync(relief, Width, Height, reliefPath, cancellationToken, normalize: true);
                await SaveNormalNativeAsync(gx, gy, Width, Height, normalPath, cancellationToken);
                await SaveSignedNativeAsync(gx, Width, Height, slopeXPath, cancellationToken);
                await SaveSignedNativeAsync(gy, Width, Height, slopeYPath, cancellationToken);
                Report(progress, 88, "PHOTOMETRIC RELIEF", "Surface normals are reconstructed from LEFT/RIGHT and TOP/BOTTOM differences. Bright/dark relief now represents shape, not card artwork.", derived: reliefPath);
                await LogStageAsync(stage, step, total);

                // Curvature and material-vs-shape separation ---------------------------------------
                stage = "CURVATURE + ARTWORK SUPPRESSION";
                step.Restart();
                float[] curvature = CalculateCurvature(gx, gy, Width, Height);
                float[] curvatureMedium = BoxBlur(curvature, Width, Height, 5);
                float[] curvatureBroad = BoxBlur(curvature, Width, Height, 14);
                float[] appearanceEdges = GradientMagnitude(reference, Width, Height);
                float[] photometricEdges = GradientMagnitude(relief, Width, Height);

                float[] curvatureN = NormalizeByPercentile(curvature, 0.992f);
                float[] curvatureMediumN = NormalizeByPercentile(curvatureMedium, 0.985f);
                float[] curvatureBroadN = NormalizeByPercentile(curvatureBroad, 0.975f);
                float[] normalChangeN = NormalizeByPercentile(normalChange, 0.992f);
                float[] appearanceN = NormalizeByPercentile(appearanceEdges, 0.995f);
                float[] photometricEdgeN = NormalizeByPercentile(photometricEdges, 0.992f);
                float[] residualN = NormalizeByPercentile(residual, 0.992f);
                float[] energyN = NormalizeByPercentile(directionalEnergy, 0.985f);

                string curvaturePath = Path.Combine(outputDirectory, "ps_curvature_heat.jpg");
                await SaveHeatNativeAsync(MaxMap(curvatureN, curvatureMediumN, curvatureBroadN), Width, Height, curvaturePath, cancellationToken);
                Report(progress, 90, "CURVATURE HEAT MAP", "Blue is quiet/flat. Yellow and red show where the reconstructed surface normal bends across the card. Printed edges are penalized in the next defect stage.", derived: curvaturePath);
                await LogStageAsync(stage, step, total);

                // Scratch / dent evidence -----------------------------------------------------------
                stage = "DEFECT EVIDENCE";
                step.Restart();
                float[] scratch = new float[count];
                float[] dent = new float[count];
                float[] combined = new float[count];
                const int border = 12;

                for (int y = 0; y < Height; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        int i = y * Width + x;
                        if (x < border || y < border || x >= Width - border || y >= Height - border)
                            continue;

                        float artworkPenalty = Math.Clamp(appearanceN[i] * 0.72f, 0.0f, 0.78f);
                        float scratchRaw =
                            (normalChangeN[i] * 0.43f) +
                            (photometricEdgeN[i] * 0.25f) +
                            (energyN[i] * 0.20f) +
                            (residualN[i] * 0.12f);
                        scratchRaw *= 1.0f - artworkPenalty;
                        scratchRaw *= validity[i];

                        float dentRaw = MathF.Max(
                            (curvatureMediumN[i] * 0.58f) + (curvatureN[i] * 0.22f) + (energyN[i] * 0.20f),
                            (curvatureBroadN[i] * 0.70f) + (curvatureMediumN[i] * 0.20f) + (residualN[i] * 0.10f));
                        // Broad physical shape is allowed to cross artwork, so suppress it only gently.
                        dentRaw *= 1.0f - (appearanceN[i] * 0.18f);
                        dentRaw *= validity[i];

                        scratch[i] = Math.Clamp(scratchRaw, 0.0f, 1.0f);
                        dent[i] = Math.Clamp(dentRaw, 0.0f, 1.0f);
                    }
                }

                scratch = BoxBlur(scratch, Width, Height, 1);
                dent = BoxBlur(dent, Width, Height, 3);
                float scratchThreshold = Math.Max(0.36f, Percentile(scratch, 0.991f));
                float dentThreshold = Math.Max(0.30f, Percentile(dent, 0.978f));

                for (int i = 0; i < count; i++)
                {
                    float sr = scratch[i] / Math.Max(scratchThreshold, Epsilon);
                    float dr = dent[i] / Math.Max(dentThreshold, Epsilon);
                    combined[i] = Math.Clamp(MathF.Max(sr, dr) / 1.55f, 0.0f, 1.0f);
                }

                string scratchPath = Path.Combine(outputDirectory, "ps_scratch_evidence.jpg");
                string dentPath = Path.Combine(outputDirectory, "ps_dent_evidence.jpg");
                string residualPath = Path.Combine(outputDirectory, "ps_reference_residual.jpg");
                await SaveHeatNativeAsync(scratch, Width, Height, scratchPath, cancellationToken);
                await SaveHeatNativeAsync(dent, Width, Height, dentPath, cancellationToken);
                await SaveGrayNativeAsync(residualN, Width, Height, residualPath, cancellationToken, normalize: false);
                Report(progress, 93, "SCRATCH / DENT EVIDENCE", "Sharp line-like normal changes feed the scratch map; broader coherent curvature feeds the dent map. Stable printed artwork is actively suppressed.", derived: scratchPath);
                await LogStageAsync(stage, step, total, $"ScratchT={scratchThreshold:0.000}; DentT={dentThreshold:0.000}");

                // Spatial support / final defect map -----------------------------------------------
                stage = "DEFECT REGIONS";
                step.Restart();
                const float finalThreshold = 0.625f;
                List<Region> regions = FindRegions(combined, scratch, dent, Width, Height, finalThreshold, border, 12);
                string heatmapPath = Path.Combine(outputDirectory, "ps_defect_heatmap.jpg");
                string overlayPath = Path.Combine(outputDirectory, "ps_defect_overlay.jpg");
                await SaveHeatNativeAsync(combined, Width, Height, heatmapPath, cancellationToken);
                await SaveOverlayNativeAsync(referenceFrame.RegisteredImagePath, regions, overlayPath, cancellationToken);
                Report(progress, 97, "DEFECT HEAT MAP", $"{regions.Count} spatially-supported candidate region{(regions.Count == 1 ? string.Empty : "s")} survived the photometric checks. Blue is quiet; yellow/red deserves inspection.", derived: heatmapPath);
                Report(progress, 99, "FINAL SURFACE FINDINGS", "CollectIQ has overlaid the strongest scratch/dent candidates on the normalized card. This is the image to compare against the physical card.", derived: overlayPath);
                await LogStageAsync(stage, step, total, $"Regions={regions.Count}");

                double hotFraction = combined.Count(v => v >= finalThreshold) / (double)count;
                double peak = Percentile(combined, 0.995f);
                double anomaly = Math.Clamp((hotFraction * 850.0) + (peak * 30.0), 0.0, 100.0);
                double photometricQuality = CalculatePhotometricQuality(top, right, bottom, left, validity);

                await InspectionDiagnosticLogger.WriteAsync(
                    "Surface/Photometric",
                    "FAST PHOTOMETRIC COMPLETE",
                    $"Elapsed={total.Elapsed.TotalSeconds:0.00}s; Regions={regions.Count}; Anomaly={anomaly:0.0}; PhotoQuality={photometricQuality:0.0}");

                string summary = regions.Count == 0
                    ? $"No spatially-supported surface defect region crossed the final threshold. Photometric quality {photometricQuality:0}%. Review the relief and heat maps for subtle features."
                    : $"CollectIQ found {regions.Count} candidate surface region{(regions.Count == 1 ? string.Empty : "s")}. The final overlay distinguishes broad dent-like curvature from sharper scratch/scuff evidence. Photometric quality {photometricQuality:0}%.";

                return new Output
                {
                    AlbedoPath = albedoPath,
                    ReliefPath = reliefPath,
                    NormalPath = normalPath,
                    CurvaturePath = curvaturePath,
                    ResidualPath = residualPath,
                    SlopeXPath = slopeXPath,
                    SlopeYPath = slopeYPath,
                    ScratchPath = scratchPath,
                    DentPath = dentPath,
                    HeatmapPath = heatmapPath,
                    OverlayPath = overlayPath,
                    RegionCount = regions.Count,
                    AnomalyScore = anomaly,
                    PhotometricQuality = photometricQuality,
                    Summary = summary
                };
            }
            catch (Exception ex)
            {
                await InspectionDiagnosticLogger.WriteAsync(
                    "Surface/Photometric",
                    $"FAILED IN {stage}",
                    $"Elapsed={total.Elapsed.TotalSeconds:0.00}s",
                    ex);
                throw new InvalidOperationException($"Surface photometric reconstruction failed during '{stage}': {ex.Message}", ex);
            }
        }

        private static void Report(
            IProgress<SurfaceInspectionProgress>? progress,
            int percent,
            string stage,
            string message,
            string? combined = null,
            string? derived = null)
        {
            progress?.Report(new SurfaceInspectionProgress
            {
                Percent = percent,
                Stage = stage,
                Message = message,
                CombinedImagePath = combined ?? string.Empty,
                DerivedImagePath = derived ?? string.Empty
            });
        }

        private static async Task LogStageAsync(string stage, Stopwatch step, Stopwatch total, string? extra = null)
        {
            step.Stop();
            await InspectionDiagnosticLogger.WriteAsync(
                "Surface/Photometric",
                stage,
                $"Stage={step.Elapsed.TotalMilliseconds:0}ms; Total={total.Elapsed.TotalMilliseconds:0}ms" +
                (string.IsNullOrWhiteSpace(extra) ? string.Empty : $"; {extra}"));
        }

        private static float[] ResizeLuminance(float[] source, int sourceWidth, int sourceHeight, int width, int height)
        {
            float[] output = new float[width * height];
            float scaleX = sourceWidth / (float)width;
            float scaleY = sourceHeight / (float)height;
            for (int y = 0; y < height; y++)
            {
                float sy = ((y + 0.5f) * scaleY) - 0.5f;
                int y0 = Math.Clamp((int)MathF.Floor(sy), 0, sourceHeight - 1);
                int y1 = Math.Min(y0 + 1, sourceHeight - 1);
                float fy = sy - y0;
                for (int x = 0; x < width; x++)
                {
                    float sx = ((x + 0.5f) * scaleX) - 0.5f;
                    int x0 = Math.Clamp((int)MathF.Floor(sx), 0, sourceWidth - 1);
                    int x1 = Math.Min(x0 + 1, sourceWidth - 1);
                    float fx = sx - x0;
                    float a = source[y0 * sourceWidth + x0];
                    float b = source[y0 * sourceWidth + x1];
                    float c = source[y1 * sourceWidth + x0];
                    float d = source[y1 * sourceWidth + x1];
                    output[y * width + x] =
                        (a * (1 - fx) * (1 - fy)) +
                        (b * fx * (1 - fy)) +
                        (c * (1 - fx) * fy) +
                        (d * fx * fy);
                }
            }
            return output;
        }

        private static void NormalizeDirectionalBrightness(float[] top, float[] right, float[] bottom, float[] left)
        {
            float target = (Mean(top) + Mean(right) + Mean(bottom) + Mean(left)) * 0.25f;
            NormalizeOne(top, target);
            NormalizeOne(right, target);
            NormalizeOne(bottom, target);
            NormalizeOne(left, target);
        }

        private static void NormalizeOne(float[] data, float target)
        {
            float mean = Math.Max(Mean(data), 0.03f);
            float scale = Math.Clamp(target / mean, 0.70f, 1.35f);
            for (int i = 0; i < data.Length; i++) data[i] = Math.Clamp(data[i] * scale, 0.0f, 1.0f);
        }

        private static float Mean(float[] values)
        {
            double sum = 0;
            int stride = values.Length > 150000 ? 3 : 1;
            int count = 0;
            for (int i = 0; i < values.Length; i += stride) { sum += values[i]; count++; }
            return count == 0 ? 0 : (float)(sum / count);
        }

        private static void Sort4(float a, float b, float c, float d, out float lo, out float m1, out float m2, out float hi)
        {
            if (a > b) (a, b) = (b, a);
            if (c > d) (c, d) = (d, c);
            if (a > c) (a, c) = (c, a);
            if (b > d) (b, d) = (d, b);
            if (b > c) (b, c) = (c, b);
            lo = a; m1 = b; m2 = c; hi = d;
        }

        private static void BuildReliefAndNormalChange(float[] gx, float[] gy, float[] relief, float[] normalChange)
        {
            for (int y = 1; y < Height - 1; y++)
            {
                for (int x = 1; x < Width - 1; x++)
                {
                    int i = y * Width + x;
                    float nx = -gx[i], ny = -gy[i], nz = 1.0f;
                    float inv = 1.0f / MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                    nx *= inv; ny *= inv; nz *= inv;
                    // Virtual grazing light makes shallow surface structure visually pop.
                    relief[i] = Math.Clamp((nx * -0.34f) + (ny * -0.42f) + (nz * 0.84f), 0.0f, 1.0f);

                    float dx = MathF.Abs(gx[i + 1] - gx[i - 1]) + MathF.Abs(gy[i + 1] - gy[i - 1]);
                    float dy = MathF.Abs(gx[i + Width] - gx[i - Width]) + MathF.Abs(gy[i + Width] - gy[i - Width]);
                    normalChange[i] = (dx + dy) * 0.5f;
                }
            }
        }

        private static float[] CalculateCurvature(float[] gx, float[] gy, int width, int height)
        {
            float[] result = new float[gx.Length];
            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    int i = y * width + x;
                    float dgxDx = (gx[i + 1] - gx[i - 1]) * 0.5f;
                    float dgyDy = (gy[i + width] - gy[i - width]) * 0.5f;
                    float cross = MathF.Abs(gx[i + width] - gx[i - width]) + MathF.Abs(gy[i + 1] - gy[i - 1]);
                    result[i] = MathF.Abs(dgxDx + dgyDy) + (cross * 0.18f);
                }
            }
            return result;
        }

        private static float[] GradientMagnitude(float[] data, int width, int height)
        {
            float[] result = new float[data.Length];
            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    int i = y * width + x;
                    float dx = (data[i + 1] - data[i - 1]) * 0.5f;
                    float dy = (data[i + width] - data[i - width]) * 0.5f;
                    result[i] = MathF.Sqrt(dx * dx + dy * dy);
                }
            }
            return result;
        }

        private static float[] BoxBlur(float[] source, int width, int height, int radius)
        {
            float[] horizontal = new float[source.Length];
            float[] output = new float[source.Length];
            int diameter = radius * 2 + 1;
            for (int y = 0; y < height; y++)
            {
                double sum = 0;
                for (int x = -radius; x <= radius; x++) sum += source[y * width + Math.Clamp(x, 0, width - 1)];
                for (int x = 0; x < width; x++)
                {
                    horizontal[y * width + x] = (float)(sum / diameter);
                    sum -= source[y * width + Math.Clamp(x - radius, 0, width - 1)];
                    sum += source[y * width + Math.Clamp(x + radius + 1, 0, width - 1)];
                }
            }
            for (int x = 0; x < width; x++)
            {
                double sum = 0;
                for (int y = -radius; y <= radius; y++) sum += horizontal[Math.Clamp(y, 0, height - 1) * width + x];
                for (int y = 0; y < height; y++)
                {
                    output[y * width + x] = (float)(sum / diameter);
                    sum -= horizontal[Math.Clamp(y - radius, 0, height - 1) * width + x];
                    sum += horizontal[Math.Clamp(y + radius + 1, 0, height - 1) * width + x];
                }
            }
            return output;
        }

        private static float[] NormalizeByPercentile(float[] source, float percentile)
        {
            float scale = Math.Max(Percentile(source, percentile), Epsilon);
            float[] result = new float[source.Length];
            for (int i = 0; i < source.Length; i++) result[i] = Math.Clamp(source[i] / scale, 0.0f, 1.0f);
            return result;
        }

        private static float Percentile(float[] values, float percentile)
        {
            if (values.Length == 0) return 0;
            const int maximumSamples = 60000;
            int stride = Math.Max(1, values.Length / maximumSamples);
            int sampleCount = (values.Length + stride - 1) / stride;
            float[] sample = new float[sampleCount];
            int p = 0;
            for (int i = 0; i < values.Length; i += stride) sample[p++] = values[i];
            if (p != sample.Length) Array.Resize(ref sample, p);
            Array.Sort(sample);
            int index = Math.Clamp((int)Math.Round((sample.Length - 1) * percentile), 0, sample.Length - 1);
            return sample[index];
        }


        private static float AbsPercentile(float[] values, float percentile)
        {
            if (values.Length == 0) return 0;
            const int maximumSamples = 60000;
            int stride = Math.Max(1, values.Length / maximumSamples);
            int sampleCount = (values.Length + stride - 1) / stride;
            float[] sample = new float[sampleCount];
            int p = 0;
            for (int i = 0; i < values.Length; i += stride) sample[p++] = MathF.Abs(values[i]);
            if (p != sample.Length) Array.Resize(ref sample, p);
            Array.Sort(sample);
            int index = Math.Clamp((int)Math.Round((sample.Length - 1) * percentile), 0, sample.Length - 1);
            return sample[index];
        }

        private static float[] MaxMap(params float[][] maps)
        {
            float[] output = new float[maps[0].Length];
            for (int i = 0; i < output.Length; i++)
            {
                float m = 0;
                for (int j = 0; j < maps.Length; j++) if (maps[j][i] > m) m = maps[j][i];
                output[i] = m;
            }
            return output;
        }

        private static List<Region> FindRegions(float[] score, float[] scratch, float[] dent, int width, int height, float threshold, int border, int maximum)
        {
            byte[] visited = new byte[score.Length];
            List<Region> regions = new();
            int[] q = new int[score.Length];
            for (int y = border; y < height - border; y++)
            {
                for (int x = border; x < width - border; x++)
                {
                    int start = y * width + x;
                    if (visited[start] != 0 || score[start] < threshold) continue;
                    int head = 0, tail = 0;
                    q[tail++] = start;
                    visited[start] = 1;
                    int minX = x, maxX = x, minY = y, maxY = y, area = 0;
                    float peak = 0, sum = 0, scratchSum = 0, dentSum = 0;
                    while (head < tail)
                    {
                        int i = q[head++];
                        int py = i / width, px = i - py * width;
                        area++; sum += score[i]; peak = Math.Max(peak, score[i]);
                        scratchSum += scratch[i]; dentSum += dent[i];
                        minX = Math.Min(minX, px); maxX = Math.Max(maxX, px); minY = Math.Min(minY, py); maxY = Math.Max(maxY, py);
                        for (int yy = py - 1; yy <= py + 1; yy++)
                        {
                            if (yy < border || yy >= height - border) continue;
                            for (int xx = px - 1; xx <= px + 1; xx++)
                            {
                                if (xx < border || xx >= width - border || (xx == px && yy == py)) continue;
                                int ni = yy * width + xx;
                                if (visited[ni] == 0 && score[ni] >= threshold)
                                {
                                    visited[ni] = 1;
                                    q[tail++] = ni;
                                }
                            }
                        }
                    }
                    if (area < 18 || (maxX - minX) < 2 || (maxY - minY) < 2) continue;
                    regions.Add(new Region
                    {
                        X = minX,
                        Y = minY,
                        Width = maxX - minX + 1,
                        Height = maxY - minY + 1,
                        Area = area,
                        Peak = peak,
                        Mean = sum / area,
                        Kind = scratchSum >= dentSum ? "SCRATCH / SCUFF" : "DENT / IMPRESSION"
                    });
                }
            }
            return regions.OrderByDescending(r => (r.Peak * 0.65f) + (r.Mean * 0.35f)).Take(maximum).ToList();
        }

        private static double CalculatePhotometricQuality(float[] top, float[] right, float[] bottom, float[] left, float[] validity)
        {
            double separation = 0, valid = 0;
            int stride = 5, samples = 0;
            for (int i = 0; i < top.Length; i += stride)
            {
                separation += Math.Min(1.0, (Math.Abs(right[i] - left[i]) + Math.Abs(bottom[i] - top[i])) * 2.4);
                valid += validity[i];
                samples++;
            }
            if (samples == 0) return 0;
            double separationScore = separation / samples * 100.0;
            double validScore = valid / samples * 100.0;
            return Math.Clamp((separationScore * 0.58) + (validScore * 0.42), 0.0, 100.0);
        }

        private static async Task SaveGrayNativeAsync(float[] values, int width, int height, string path, CancellationToken token, bool normalize)
        {
            token.ThrowIfCancellationRequested();
            float scale = normalize ? Math.Max(Percentile(values, 0.995f), Epsilon) : 1.0f;
            byte[] bytes = new byte[values.Length];
            for (int i = 0; i < values.Length; i++) bytes[i] = (byte)Math.Clamp(MathF.Round((normalize ? values[i] / scale : values[i]) * 255.0f), 0, 255);
            using Mat mat = new(height, width, MatType.CV_8UC1);
            Marshal.Copy(bytes, 0, mat.Data, bytes.Length);
            Cv2.ImWrite(path, mat, new[] { (int)ImwriteFlags.JpegQuality, 92 });
            await Task.CompletedTask;
        }

        private static async Task SaveSignedNativeAsync(float[] values, int width, int height, string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            float p = Math.Max(AbsPercentile(values, 0.99f), Epsilon);
            byte[] bytes = new byte[values.Length];
            for (int i = 0; i < values.Length; i++) bytes[i] = (byte)Math.Clamp(MathF.Round((0.5f + (values[i] / (2 * p))) * 255.0f), 0, 255);
            using Mat mat = new(height, width, MatType.CV_8UC1);
            Marshal.Copy(bytes, 0, mat.Data, bytes.Length);
            Cv2.ImWrite(path, mat, new[] { (int)ImwriteFlags.JpegQuality, 92 });
            await Task.CompletedTask;
        }

        private static async Task SaveNormalNativeAsync(float[] gx, float[] gy, int width, int height, string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            byte[] bgr = new byte[width * height * 3];
            for (int i = 0; i < gx.Length; i++)
            {
                float nx = -gx[i], ny = -gy[i], nz = 1.0f;
                float inv = 1.0f / MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                nx *= inv; ny *= inv; nz *= inv;
                int p = i * 3;
                bgr[p] = (byte)Math.Clamp(MathF.Round((nz * 0.5f + 0.5f) * 255), 0, 255);
                bgr[p + 1] = (byte)Math.Clamp(MathF.Round((ny * 0.5f + 0.5f) * 255), 0, 255);
                bgr[p + 2] = (byte)Math.Clamp(MathF.Round((nx * 0.5f + 0.5f) * 255), 0, 255);
            }
            using Mat mat = new(height, width, MatType.CV_8UC3);
            Marshal.Copy(bgr, 0, mat.Data, bgr.Length);
            Cv2.ImWrite(path, mat, new[] { (int)ImwriteFlags.JpegQuality, 93 });
            await Task.CompletedTask;
        }

        private static async Task SaveHeatNativeAsync(float[] values, int width, int height, string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            float scale = Math.Max(Percentile(values, 0.995f), Epsilon);
            byte[] bgr = new byte[width * height * 3];
            for (int i = 0; i < values.Length; i++)
            {
                float v = Math.Clamp(values[i] / scale, 0.0f, 1.0f);
                Heat(v, out byte r, out byte g, out byte b);
                int p = i * 3; bgr[p] = b; bgr[p + 1] = g; bgr[p + 2] = r;
            }
            using Mat mat = new(height, width, MatType.CV_8UC3);
            Marshal.Copy(bgr, 0, mat.Data, bgr.Length);
            Cv2.ImWrite(path, mat, new[] { (int)ImwriteFlags.JpegQuality, 94 });
            await Task.CompletedTask;
        }

        private static void Heat(float v, out byte r, out byte g, out byte b)
        {
            // Dark navy -> blue -> cyan -> yellow -> red. Designed to make small changes pop on phone screens.
            v = Math.Clamp(v, 0, 1);
            float rf, gf, bf;
            if (v < 0.25f)
            {
                float t = v / 0.25f; rf = 0.02f; gf = 0.08f + 0.18f * t; bf = 0.18f + 0.82f * t;
            }
            else if (v < 0.50f)
            {
                float t = (v - 0.25f) / 0.25f; rf = 0.02f; gf = 0.26f + 0.74f * t; bf = 1.0f;
            }
            else if (v < 0.75f)
            {
                float t = (v - 0.50f) / 0.25f; rf = t; gf = 1.0f; bf = 1.0f - t;
            }
            else
            {
                float t = (v - 0.75f) / 0.25f; rf = 1.0f; gf = 1.0f - t; bf = 0.0f;
            }
            r = (byte)(rf * 255); g = (byte)(gf * 255); b = (byte)(bf * 255);
        }

        private static async Task SaveOverlayNativeAsync(string referencePath, List<Region> regions, string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using Mat source = Cv2.ImRead(referencePath, ImreadModes.Color);
            if (source.Empty()) throw new InvalidOperationException("Could not open the normalized reference for the final defect overlay.");
            using Mat display = new();
            Cv2.Resize(source, display, new OpenCvSharp.Size(Width, Height), 0, 0, InterpolationFlags.Area);
            int number = 1;
            foreach (Region region in regions)
            {
                Scalar color = region.Kind.StartsWith("SCRATCH", StringComparison.Ordinal) ? new Scalar(0, 215, 255) : new Scalar(0, 70, 255);
                Cv2.Rectangle(display, new OpenCvSharp.Rect(region.X, region.Y, region.Width, region.Height), color, 2);
                Cv2.PutText(display, $"{number} {region.Kind}", new OpenCvSharp.Point(region.X, Math.Max(16, region.Y - 5)), HersheyFonts.HersheySimplex, 0.42, color, 1, LineTypes.AntiAlias);
                number++;
            }
            Cv2.ImWrite(path, display, new[] { (int)ImwriteFlags.JpegQuality, 94 });
            await Task.CompletedTask;
        }
    }
}
