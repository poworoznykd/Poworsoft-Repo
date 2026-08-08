/*
* FILE: SurfaceInspectionService.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* DESCRIPTION:
*     Creates preliminary surface-inspection images from four photographs
*     captured with a fixed phone/card and a light moved around the card.
*     This is the first mobile approximation of the directional-light
*     inspection techniques used in industrial machine vision.
*/

using CollectIQ.Interfaces;
using CollectIQ.Models.Inspection;
using Microsoft.Maui.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using ImageSharpRectangle = SixLabors.ImageSharp.Rectangle;
using ImageSharpResizeMode = SixLabors.ImageSharp.Processing.ResizeMode;
using ImageSharpSize = SixLabors.ImageSharp.Size;

namespace CollectIQ.Services.Inspection
{
    /// <summary>
    /// Performs local directional-image processing for surface inspection.
    /// </summary>
    public sealed class SurfaceInspectionService : ISurfaceInspectionService
    {
        private const int ProcessingWidth = 700;
        private const int ProcessingHeight = 980;
        private const float Epsilon = 0.001f;

        /// <inheritdoc />
        public async Task<SurfaceInspectionResult> AnalyzeAsync(
            IReadOnlyDictionary<SurfaceLightDirection, string> captures,
            CancellationToken cancellationToken = default)
        {
            ValidateCaptures(captures);

            float[] top = await LoadNormalizedLuminanceAsync(
                captures[SurfaceLightDirection.Top], cancellationToken);
            float[] right = await LoadNormalizedLuminanceAsync(
                captures[SurfaceLightDirection.Right], cancellationToken);
            float[] bottom = await LoadNormalizedLuminanceAsync(
                captures[SurfaceLightDirection.Bottom], cancellationToken);
            float[] left = await LoadNormalizedLuminanceAsync(
                captures[SurfaceLightDirection.Left], cancellationToken);

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

            // Remove broad illumination changes so local scratches, dents and
            // print/surface disturbances are emphasized over slow gradients.
            float[] localRelief = HighPassRelief(relief);
            float threshold = CalculatePercentile(localRelief, 0.97f);
            double anomalyScore = CalculateAnomalyScore(localRelief, threshold);
            double consistencyScore = CalculateCaptureConsistency(top, right, bottom, left);

            string outputDirectory = Path.Combine(
                FileSystem.AppDataDirectory,
                "SurfaceInspections",
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));

            Directory.CreateDirectory(outputDirectory);

            string diffusePath = Path.Combine(outputDirectory, "diffuse.png");
            string reliefPath = Path.Combine(outputDirectory, "surface_relief.png");
            string heatmapPath = Path.Combine(outputDirectory, "surface_heatmap.png");

            await SaveGrayscaleAsync(diffuse, diffusePath, cancellationToken);
            await SaveGrayscaleAsync(localRelief, reliefPath, cancellationToken, normalize: true);
            await SaveHeatmapAsync(localRelief, threshold, heatmapPath, cancellationToken);

            return new SurfaceInspectionResult
            {
                DiffuseImagePath = diffusePath,
                ReliefImagePath = reliefPath,
                HeatmapImagePath = heatmapPath,
                AnomalyScore = anomalyScore,
                CaptureConsistencyScore = consistencyScore,
                Summary = BuildSummary(anomalyScore, consistencyScore)
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

        private static async Task<float[]> LoadNormalizedLuminanceAsync(
            string path,
            CancellationToken cancellationToken)
        {
            using SixLabors.ImageSharp.Image<Rgba32> image = await ImageSharpImage.LoadAsync<Rgba32>(path, cancellationToken);

            ImageSharpRectangle cropRectangle = CalculateCenteredCardCrop(image.Width, image.Height);

            image.Mutate(context => context
                .Crop(cropRectangle)
                .Resize(new ResizeOptions
                {
                    Size = new ImageSharpSize(ProcessingWidth, ProcessingHeight),
                    Mode = ImageSharpResizeMode.Stretch,
                    Sampler = KnownResamplers.Bicubic
                }));

            float[] luminance = new float[ProcessingWidth * ProcessingHeight];

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < ProcessingHeight; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);

                    for (int x = 0; x < ProcessingWidth; x++)
                    {
                        Rgba32 pixel = row[x];

                        luminance[(y * ProcessingWidth) + x] =
                            ((0.2126f * pixel.R) +
                             (0.7152f * pixel.G) +
                             (0.0722f * pixel.B)) / 255.0f;
                    }
                }
            });

            return luminance;
        }

        private static ImageSharpRectangle CalculateCenteredCardCrop(int width, int height)
        {
            const float cardAspectRatio = 2.5f / 3.5f;
            int cropHeight = (int)(height * 0.84f);
            int cropWidth = (int)(cropHeight * cardAspectRatio);

            if (cropWidth > (int)(width * 0.90f))
            {
                cropWidth = (int)(width * 0.90f);
                cropHeight = (int)(cropWidth / cardAspectRatio);
            }

            int x = Math.Max(0, (width - cropWidth) / 2);
            int y = Math.Max(0, (height - cropHeight) / 2);

            return new ImageSharpRectangle(x, y, cropWidth, cropHeight);
        }

        private static float CalculateMean(float[] values)
        {
            double total = 0;

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
            // A small box-blur is sufficient for the MVP and avoids another native
            // computer-vision dependency. It removes slow lighting gradients while
            // preserving local changes caused by scratches, dents and print lines.
            const int radius = 9;
            float[] blurred = BoxBlur(relief, radius);
            float[] local = new float[relief.Length];

            for (int index = 0; index < relief.Length; index++)
            {
                local[index] = MathF.Max(0.0f, relief[index] - blurred[index]);
            }

            return local;
        }

        private static float[] BoxBlur(float[] source, int radius)
        {
            int width = ProcessingWidth;
            int height = ProcessingHeight;
            float[] horizontal = new float[source.Length];
            float[] output = new float[source.Length];

            for (int y = 0; y < height; y++)
            {
                double sum = 0;

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
                double sum = 0;

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

            return Math.Clamp((areaRatio * 900.0) + (averageExcess * 12.0), 0.0, 100.0);
        }

        private static double CalculateCaptureConsistency(params float[][] images)
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

            return Math.Clamp(100.0 - (coefficientOfVariation * 180.0), 0.0, 100.0);
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

            using var image = new SixLabors.ImageSharp.Image<Rgba32>(ProcessingWidth, ProcessingHeight);

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

            await image.SaveAsync(
                path,
                new PngEncoder(),
                cancellationToken);
        }

        private static async Task SaveHeatmapAsync(
            float[] values,
            float threshold,
            string path,
            CancellationToken cancellationToken)
        {
            float high = Math.Max(CalculatePercentile(values, 0.995f), threshold + Epsilon);

            using var image = new SixLabors.ImageSharp.Image<Rgba32>(ProcessingWidth, ProcessingHeight);

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

            await image.SaveAsync(
                path,
                new PngEncoder(),
                cancellationToken);
        }

        private static string BuildSummary(
            double anomalyScore,
            double consistencyScore)
        {
            if (consistencyScore < 65.0)
            {
                return "Capture consistency is low. Retake the scan before trusting highlighted surface areas.";
            }

            if (anomalyScore < 20.0)
            {
                return "No strong directional surface anomalies were detected in this scan.";
            }

            if (anomalyScore < 50.0)
            {
                return "Some localized surface anomalies were highlighted. Review the heatmap at full resolution.";
            }

            return "Multiple or strong surface anomalies were highlighted. Review each area before drawing a condition conclusion.";
        }
    }
}
