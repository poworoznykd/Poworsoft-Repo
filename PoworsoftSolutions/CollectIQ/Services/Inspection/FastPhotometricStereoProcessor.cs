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
            public float Confidence { get; set; }
            public string Evidence { get; set; } = string.Empty;
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

                // Use the robust albedo rather than the neutral photo for artwork suppression.
                // Printed colour/ink survives in albedo; true surface shape survives in the
                // reconstructed slopes/normals. This separation is the core of the defect logic.
                float[] appearanceEdges = GradientMagnitude(albedo, Width, Height);
                float[] photometricEdges = GradientMagnitude(relief, Width, Height);
                float[] signedCurvature = CalculateSignedMeanCurvature(gx, gy, Width, Height);
                float[] dentCoherence = CalculateSignedCurvatureCoherence(signedCurvature, Width, Height, 5, 15);
                float[] lineResponse = CalculateLineResponse(relief, Width, Height);
                float[] lineLocalContrast = PositiveHighPass(lineResponse, Width, Height, 7);

                // Dent-specific 3D cues. A true localized dent/bump creates a compact
                // radial slope field around a common centre and survives several spatial
                // scales. Printed artwork can have strong edges, but it normally does not
                // create this radial normal-field pattern in opposing-light photometry.
                float[] radialDent = CalculateRadialSlopeCoherence(gx, gy, Width, Height);
                float[] multiScaleBump = CalculateMultiScaleBumpResponse(relief, Width, Height);
                float[] blobRing = CalculateMultiScaleBlobRingResponse(relief, curvature, Width, Height);

                float[] curvatureN = NormalizeByPercentile(curvature, 0.992f);
                float[] curvatureMediumN = NormalizeByPercentile(curvatureMedium, 0.985f);
                float[] curvatureBroadN = NormalizeByPercentile(curvatureBroad, 0.975f);
                float[] normalChangeN = NormalizeByPercentile(normalChange, 0.992f);
                float[] appearanceN = NormalizeByPercentile(appearanceEdges, 0.995f);
                float[] photometricEdgeN = NormalizeByPercentile(photometricEdges, 0.992f);
                float[] residualN = NormalizeByPercentile(residual, 0.992f);
                float[] energyN = NormalizeByPercentile(directionalEnergy, 0.985f);
                float[] lineN = NormalizeByPercentile(lineResponse, 0.992f);
                float[] lineLocalN = NormalizeByPercentile(lineLocalContrast, 0.990f);
                float[] dentCoherenceN = NormalizeByPercentile(dentCoherence, 0.985f);
                float[] radialDentN = NormalizeByPercentile(radialDent, 0.990f);
                float[] multiScaleBumpN = NormalizeByPercentile(multiScaleBump, 0.988f);
                float[] blobRingN = NormalizeByPercentile(blobRing, 0.988f);

                string curvaturePath = Path.Combine(outputDirectory, "ps_curvature_heat.jpg");
                await SaveHeatNativeAsync(MaxMap(curvatureN, curvatureMediumN, curvatureBroadN, dentCoherenceN), Width, Height, curvaturePath, cancellationToken);
                Report(progress, 90, "CURVATURE HEAT MAP", "Blue is quiet/flat. Yellow/red now emphasizes coherent bending of the reconstructed surface while isolated printed artwork is suppressed later by the defect classifier.", derived: curvaturePath);
                await LogStageAsync(stage, step, total);

                // Scratch / dent evidence -----------------------------------------------------------
                stage = "DEFECT EVIDENCE";
                step.Restart();
                float[] scratch = new float[count];
                float[] dent = new float[count];
                float[] dentConsensus = new float[count];
                float[] combined = new float[count];

                // Phase 5: opposing-light polarity + multi-scale local candidate detection.
                // A real dimple should reverse its signed slope across the centre when the
                // illumination direction is reversed. Printed artwork can be high contrast,
                // but normally does not create this compact 3D polarity reversal.
                float[] opposingPolarity = CalculateOpposingLightPolarity(gx, gy, Width, Height);
                float[] opposingPolarityN = NormalizeByPercentile(opposingPolarity, 0.990f);
                float[] blobLocalN = LocalContrastNormalize(blobRingN, Width, Height, 24);
                float[] bumpLocalN = LocalContrastNormalize(multiScaleBumpN, Width, Height, 24);
                float[] curvatureLocalN = LocalContrastNormalize(curvatureMediumN, Width, Height, 24);
                float[] normalLocalN = LocalContrastNormalize(normalChangeN, Width, Height, 24);

                // Phase 6: independent dimple seeding.  The final classifier was still
                // missing obvious dents that were plainly visible in relief/normal views
                // because those locations never survived the composite dent-map threshold.
                // This center-surround response promotes compact circular/elliptical 3D
                // disturbances directly from the reconstructed shape maps before artwork
                // penalties or opposite-light scoring are applied.
                float[] dimpleSeed = CalculateDimpleSeedResponse(relief, normalChangeN, curvatureLocalN, Width, Height);
                float[] dimpleSeedN = NormalizeByPercentile(dimpleSeed, 0.988f);

                // Phase 7: direct visible-dimple response.  This path intentionally does NOT
                // depend on the composite dent score.  If a compact centre-surround feature is
                // visibly reconstructed in relief/normal/curvature, it receives an independent
                // candidate score before artwork suppression is allowed to veto it.
                float[] directDimple = CalculateDirectVisibleDimpleResponse(
                    relief,
                    normalChangeN,
                    curvatureMediumN,
                    Width,
                    Height);
                float[] directDimpleN = NormalizeByPercentile(directDimple, 0.975f);

                // Phase 9: residual-ring dimple response.  The neutral-reference vs robust
                // albedo subtraction is one of the cleanest views of shallow circular dents
                // on real cards.  Use it directly instead of only as a tiny supporting weight.
                float[] residualDimple = CalculateResidualRingDimpleResponse(residualN, Width, Height);
                float[] residualDimpleN = NormalizeByPercentile(residualDimple, 0.965f);

                // Edge/corner damage has its own inspection. Surface dents are deliberately
                // evaluated inside the card face so the strong printed/card-border geometry
                // cannot dominate the candidate list.
                const int border = 34;

                for (int y = 0; y < Height; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        int i = y * Width + x;
                        if (x < border || y < border || x >= Width - border || y >= Height - border)
                            continue;

                        // SCRATCH MODEL: thin/line-like 3D perturbation only.
                        float shapeLine = MathF.Max(lineN[i], lineLocalN[i]);
                        float artworkPenalty = appearanceN[i] * (1.0f - Math.Clamp(shapeLine * 0.85f, 0.0f, 0.85f));
                        artworkPenalty = Math.Clamp(artworkPenalty * 0.88f, 0.0f, 0.82f);

                        float scratchRaw =
                            (shapeLine * 0.50f) +
                            (normalChangeN[i] * 0.23f) +
                            (photometricEdgeN[i] * 0.12f) +
                            (energyN[i] * 0.09f) +
                            (residualN[i] * 0.06f);
                        scratchRaw *= 1.0f - artworkPenalty;
                        scratchRaw *= validity[i];

                        // DENT MODEL: local, candidate-first 3D evidence. The weights intentionally
                        // favour opposing-light reversal and compact multi-scale blob structure.
                        int votes = 0;
                        if (opposingPolarityN[i] >= 0.26f) votes++;
                        if (blobLocalN[i] >= 0.30f) votes++;
                        if (bumpLocalN[i] >= 0.30f) votes++;
                        if (curvatureLocalN[i] >= 0.27f) votes++;
                        if (normalLocalN[i] >= 0.27f) votes++;
                        if (radialDentN[i] >= 0.20f) votes++;
                        if (dimpleSeedN[i] >= 0.30f) votes++;

                        float consensus = votes / 7.0f;
                        dentConsensus[i] = consensus;

                        float dentRaw =
                            (opposingPolarityN[i] * 0.25f) +
                            (blobLocalN[i] * 0.20f) +
                            (bumpLocalN[i] * 0.18f) +
                            (normalLocalN[i] * 0.14f) +
                            (curvatureLocalN[i] * 0.11f) +
                            (dentCoherenceN[i] * 0.05f) +
                            (radialDentN[i] * 0.03f) +
                            (dimpleSeedN[i] * 0.10f) +
                            (energyN[i] * 0.02f);

                        // Albedo/artwork is a veto, not a primary signal. Strong 3D evidence can
                        // override a printed edge because a real dent may sit directly on artwork.
                        float shapeStrength = Math.Clamp(
                            (opposingPolarityN[i] * 0.36f) +
                            (blobLocalN[i] * 0.24f) +
                            (bumpLocalN[i] * 0.16f) +
                            (normalLocalN[i] * 0.14f) +
                            (dimpleSeedN[i] * 0.10f), 0.0f, 1.0f);
                        float dentArtworkPenalty = appearanceN[i] * (1.0f - (shapeStrength * 0.86f));
                        dentArtworkPenalty = Math.Clamp(dentArtworkPenalty * 0.76f, 0.0f, 0.68f);

                        // Localized dents are compact. Long line-like responses get a soft penalty.
                        float linePenalty = 1.0f - Math.Clamp(lineN[i] * 0.30f, 0.0f, 0.26f);
                        dentRaw *= 1.0f - dentArtworkPenalty;
                        dentRaw *= linePenalty;
                        dentRaw *= validity[i];

                        // Do not let one weak cue erase an otherwise obvious dimple. We only need
                        // a compact local signal and two supporting 3D cues here; the candidate
                        // evaluator below performs the stricter neighbourhood-level checks.
                        if ((opposingPolarityN[i] < 0.14f && blobLocalN[i] < 0.22f && dimpleSeedN[i] < 0.34f) || votes < 2)
                            dentRaw = 0.0f;
                        else
                            dentRaw *= 0.82f + (consensus * 0.30f);

                        scratch[i] = Math.Clamp(scratchRaw, 0.0f, 1.0f);
                        dent[i] = Math.Clamp(dentRaw, 0.0f, 1.0f);
                    }
                }

                scratch = BoxBlur(scratch, Width, Height, 1);
                dent = BoxBlur(dent, Width, Height, 1);
                dentConsensus = BoxBlur(dentConsensus, Width, Height, 2);

                float scratchThreshold = Math.Max(0.48f, Percentile(scratch, 0.996f));
                float dentThreshold = Math.Max(0.20f, Percentile(dent, 0.982f));

                for (int i = 0; i < count; i++)
                {
                    float sr = scratch[i] / Math.Max(scratchThreshold, Epsilon);
                    float dr = dent[i] / Math.Max(dentThreshold, Epsilon);
                    combined[i] = Math.Clamp(MathF.Max(sr, dr), 0.0f, 1.0f);
                }

                string scratchPath = Path.Combine(outputDirectory, "ps_scratch_evidence.jpg");
                string dentPath = Path.Combine(outputDirectory, "ps_dent_evidence.jpg");
                string residualPath = Path.Combine(outputDirectory, "ps_reference_residual.jpg");
                await SaveHeatNativeAsync(scratch, Width, Height, scratchPath, cancellationToken);
                float[] visibleDimpleEvidence = MaxMap(dent, dimpleSeedN, directDimpleN, residualDimpleN);
                await SaveHeatNativeAsync(visibleDimpleEvidence, Width, Height, dentPath, cancellationToken);
                await SaveGrayNativeAsync(residualN, Width, Height, residualPath, cancellationToken, normalize: false);
                Report(progress, 93, "SCRATCH / DENT EVIDENCE", "The dent view now includes direct relief/normal dimples and compact ring responses from the neutral-reference vs albedo subtraction. Obvious circular dents can be promoted even when artwork weakens the composite score.", derived: dentPath);
                await LogStageAsync(stage, step, total, $"ScratchT={scratchThreshold:0.000}; DentT={dentThreshold:0.000}");

                // Candidate-first final defect map -------------------------------------------------
                stage = "DEFECT REGIONS";
                step.Restart();

                // Instead of thresholding the entire card and trusting whatever connected regions
                // survive, generate a short candidate list first, thoroughly test each candidate,
                // then perform non-maximum suppression. This keeps artwork from flooding the result.
                List<Region> strictDentRegions = FindDentCandidates(
                    dent,
                    opposingPolarityN,
                    blobLocalN,
                    bumpLocalN,
                    curvatureLocalN,
                    normalLocalN,
                    dentConsensus,
                    dimpleSeedN,
                    directDimpleN,
                    relief,
                    appearanceN,
                    lineN,
                    validity,
                    Width,
                    Height,
                    border,
                    maximum: 5);

                // Phase 8: high-visibility dimple rescue path.
                // The photometric reconstruction is already making some real dimples obvious
                // to the human eye, but the strict consensus classifier can still reject them.
                // Give compact, directly visible dimple peaks an independent route into the
                // final candidate set. Other cues tune confidence; they do not veto existence.
                List<Region> visibleDimpleRegions = FindHighVisibilityDimpleCandidates(
                    directDimpleN,
                    dimpleSeedN,
                    residualDimpleN,
                    residualN,
                    relief,
                    normalLocalN,
                    curvatureLocalN,
                    opposingPolarityN,
                    appearanceN,
                    lineN,
                    validity,
                    Width,
                    Height,
                    border,
                    maximum: 5);

                List<Region> dentRegions = NonMaximumSuppress(
                    strictDentRegions.Concat(visibleDimpleRegions).ToList(),
                    maximum: 5,
                    overlapThreshold: 0.20f);

                List<Region> scratchRegions = FindScratchRegions(
                    scratch,
                    appearanceN,
                    lineN,
                    Width,
                    Height,
                    Math.Max(scratchThreshold, 0.50f),
                    border,
                    maximum: 3);

                List<Region> regions = dentRegions
                    .Concat(scratchRegions)
                    .OrderByDescending(r => r.Confidence)
                    .Take(8)
                    .ToList();

                const float finalThreshold = 0.72f;
                string heatmapPath = Path.Combine(outputDirectory, "ps_defect_heatmap.jpg");
                string overlayPath = Path.Combine(outputDirectory, "ps_defect_overlay.jpg");
                await SaveHeatNativeAsync(combined, Width, Height, heatmapPath, cancellationToken);
                await SaveOverlayNativeAsync(referenceFrame.RegisteredImagePath, regions, overlayPath, cancellationToken);
                Report(progress, 97, "POTENTIAL PHYSICAL SURFACE DEFECTS", $"{regions.Count} high-value candidate region{(regions.Count == 1 ? string.Empty : "s")} survived local 3D, opposing-light, artwork and shape checks. Fewer strong candidates are preferred over a noisy card full of boxes.", derived: heatmapPath);
                Report(progress, 99, "FINAL SURFACE FINDINGS", "CollectIQ has overlaid the strongest potential physical surface defects. Dent confidence can now originate directly from an isolated dimple visible in the reconstructed 3D maps, then gains confidence from opposing-light, relief, curvature and normal agreement.", derived: overlayPath);
                await LogStageAsync(stage, step, total, $"Dents={dentRegions.Count}; Scratches={scratchRegions.Count}; Regions={regions.Count}");

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

        private static float[] CalculateSignedMeanCurvature(float[] gx, float[] gy, int width, int height)
        {
            float[] result = new float[gx.Length];
            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    int i = y * width + x;
                    float dgxDx = (gx[i + 1] - gx[i - 1]) * 0.5f;
                    float dgyDy = (gy[i + width] - gy[i - width]) * 0.5f;
                    result[i] = dgxDx + dgyDy;
                }
            }
            return result;
        }

        private static float[] CalculateSignedCurvatureCoherence(float[] signedCurvature, int width, int height, int smallRadius, int largeRadius)
        {
            float[] smallSigned = BoxBlur(signedCurvature, width, height, smallRadius);
            float[] absolute = new float[signedCurvature.Length];
            for (int i = 0; i < signedCurvature.Length; i++) absolute[i] = MathF.Abs(signedCurvature[i]);
            float[] smallAbsolute = BoxBlur(absolute, width, height, smallRadius);
            float[] broadSigned = BoxBlur(signedCurvature, width, height, largeRadius);
            float[] result = new float[signedCurvature.Length];

            for (int i = 0; i < result.Length; i++)
            {
                float coherence = MathF.Abs(smallSigned[i]) / (smallAbsolute[i] + Epsilon);
                float localShape = MathF.Abs(smallSigned[i] - broadSigned[i]);
                result[i] = localShape * Math.Clamp(coherence, 0.0f, 1.0f);
            }
            return result;
        }

        private static float[] CalculateRadialSlopeCoherence(float[] gx, float[] gy, int width, int height)
        {
            // Eight samples around each candidate centre. A localized dent/bump should
            // produce slopes that point consistently toward or away from that centre.
            // We use absolute radial alignment plus sign consistency and angular coverage.
            int[] dx = { 8, 6, 0, -6, -8, -6, 0, 6 };
            int[] dy = { 0, 6, 8, 6, 0, -6, -8, -6 };
            float[] invRadius = { 0.125f, 0.11785113f, 0.125f, 0.11785113f, 0.125f, 0.11785113f, 0.125f, 0.11785113f };
            float[] result = new float[gx.Length];
            const int margin = 10;

            for (int y = margin; y < height - margin; y++)
            {
                for (int x = margin; x < width - margin; x++)
                {
                    float signed = 0.0f;
                    float aligned = 0.0f;
                    float weightSum = 0.0f;
                    float maxWeight = 0.0f;

                    for (int s = 0; s < 8; s++)
                    {
                        int sx = x + dx[s];
                        int sy = y + dy[s];
                        int si = sy * width + sx;
                        float vx = gx[si];
                        float vy = gy[si];
                        float mag = MathF.Sqrt((vx * vx) + (vy * vy));
                        if (mag > maxWeight) maxWeight = mag;
                        if (mag < Epsilon) continue;

                        float rx = dx[s] * invRadius[s];
                        float ry = dy[s] * invRadius[s];
                        float radialDot = ((vx * rx) + (vy * ry)) / mag;
                        float weighted = radialDot * mag;
                        signed += weighted;
                        aligned += MathF.Abs(weighted);
                        weightSum += mag;
                    }

                    if (weightSum < Epsilon || aligned < Epsilon || maxWeight < Epsilon)
                        continue;

                    int active = 0;
                    float activeThreshold = maxWeight * 0.22f;
                    for (int s = 0; s < 8; s++)
                    {
                        int sx = x + dx[s];
                        int sy = y + dy[s];
                        int si = sy * width + sx;
                        float vx = gx[si];
                        float vy = gy[si];
                        float mag = MathF.Sqrt((vx * vx) + (vy * vy));
                        if (mag >= activeThreshold) active++;
                    }

                    float radialAlignment = aligned / weightSum;
                    float signConsistency = MathF.Abs(signed) / (aligned + Epsilon);
                    float coverage = Math.Clamp(active / 6.0f, 0.0f, 1.0f);
                    float meanStrength = weightSum / 8.0f;
                    result[y * width + x] = radialAlignment * signConsistency * coverage * meanStrength;
                }
            }

            return result;
        }

        private static float[] CalculateMultiScaleBlobRingResponse(float[] relief, float[] curvature, int width, int height)
        {
            // Difference-of-scale response.  A localized dent produces a compact centre/ring
            // disturbance that survives several radii, whereas long artwork edges tend to stay
            // directional and are weakened when the centre and surrounding annulus are compared.
            float[] relief2 = BoxBlur(relief, width, height, 2);
            float[] relief6 = BoxBlur(relief, width, height, 6);
            float[] relief12 = BoxBlur(relief, width, height, 12);
            float[] relief22 = BoxBlur(relief, width, height, 22);
            float[] curve2 = BoxBlur(curvature, width, height, 2);
            float[] curve7 = BoxBlur(curvature, width, height, 7);
            float[] curve15 = BoxBlur(curvature, width, height, 15);
            float[] output = new float[relief.Length];

            for (int i = 0; i < output.Length; i++)
            {
                float small = MathF.Abs(relief2[i] - relief6[i]);
                float medium = MathF.Abs(relief6[i] - relief12[i]);
                float broad = MathF.Abs(relief12[i] - relief22[i]);

                // Curvature should be concentrated near the same compact structure.
                float curveLocal = MathF.Max(0.0f, curve2[i] - curve15[i]);
                float curveRing = MathF.Max(0.0f, curve7[i] - curve15[i]);

                // Reward persistence at adjacent scales instead of a single noisy response.
                float persistence = MathF.Min(MathF.Max(small, medium), MathF.Max(medium, broad));
                output[i] =
                    (MathF.Max(small, MathF.Max(medium, broad)) * 0.34f) +
                    (persistence * 0.26f) +
                    (curveLocal * 0.24f) +
                    (curveRing * 0.16f);
            }

            return BoxBlur(output, width, height, 2);
        }

        private static float[] CalculateMultiScaleBumpResponse(float[] relief, int width, int height)
        {
            float[] blur4 = BoxBlur(relief, width, height, 4);
            float[] blur8 = BoxBlur(relief, width, height, 8);
            float[] blur14 = BoxBlur(relief, width, height, 14);
            float[] response = new float[relief.Length];

            for (int i = 0; i < relief.Length; i++)
            {
                float r4 = MathF.Abs(relief[i] - blur4[i]);
                float r8 = MathF.Abs(relief[i] - blur8[i]);
                float r14 = MathF.Abs(relief[i] - blur14[i]);
                float persistent = MathF.Min(MathF.Max(r4, r8), MathF.Max(r8, r14));
                response[i] = (MathF.Max(r4, MathF.Max(r8, r14)) * 0.62f) + (persistent * 0.38f);
            }

            return BoxBlur(response, width, height, 1);
        }

        private static float[] CalculateLineResponse(float[] data, int width, int height)
        {
            float[] result = new float[data.Length];
            for (int y = 2; y < height - 2; y++)
            {
                for (int x = 2; x < width - 2; x++)
                {
                    int i = y * width + x;
                    float c = data[i];
                    float dxx = data[i - 1] - (2.0f * c) + data[i + 1];
                    float dyy = data[i - width] - (2.0f * c) + data[i + width];
                    float dxy = (data[i + width + 1] - data[i + width - 1] - data[i - width + 1] + data[i - width - 1]) * 0.25f;

                    float trace = dxx + dyy;
                    float disc = MathF.Sqrt(MathF.Max(0.0f, ((dxx - dyy) * (dxx - dyy)) + (4.0f * dxy * dxy)));
                    float lambda1 = 0.5f * (trace + disc);
                    float lambda2 = 0.5f * (trace - disc);
                    float a1 = MathF.Abs(lambda1);
                    float a2 = MathF.Abs(lambda2);
                    float strong = MathF.Max(a1, a2);
                    float weak = MathF.Min(a1, a2);
                    float anisotropy = (strong - weak) / (strong + Epsilon);
                    result[i] = strong * anisotropy;
                }
            }
            return result;
        }

        private static float[] PositiveHighPass(float[] source, int width, int height, int radius)
        {
            float[] background = BoxBlur(source, width, height, radius);
            float[] result = new float[source.Length];
            for (int i = 0; i < source.Length; i++) result[i] = MathF.Max(0.0f, source[i] - background[i]);
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

        private static float[] CalculateDimpleSeedResponse(
            float[] relief,
            float[] normalChange,
            float[] curvatureLocal,
            int width,
            int height)
        {
            // Center-surround detector tuned for the small round/elliptical dimples that
            // are plainly visible in the reconstructed relief and normal maps.  We use
            // several radii so a defect does not have to match one exact physical size.
            float[] reliefSmall = BoxBlur(relief, width, height, 2);
            float[] reliefMid = BoxBlur(relief, width, height, 7);
            float[] reliefLarge = BoxBlur(relief, width, height, 15);
            float[] normalSmall = BoxBlur(normalChange, width, height, 2);
            float[] normalMid = BoxBlur(normalChange, width, height, 8);
            float[] response = new float[relief.Length];

            for (int i = 0; i < response.Length; i++)
            {
                float reliefFine = MathF.Abs(reliefSmall[i] - reliefMid[i]);
                float reliefBroad = MathF.Abs(reliefMid[i] - reliefLarge[i]);
                float normalBlob = MathF.Max(0.0f, normalSmall[i] - (normalMid[i] * 0.70f));
                float curvatureSupport = curvatureLocal[i];

                response[i] =
                    (MathF.Max(reliefFine, reliefBroad) * 0.48f) +
                    (normalBlob * 0.34f) +
                    (curvatureSupport * 0.18f);
            }

            return BoxBlur(response, width, height, 2);
        }

        private static float[] CalculateOpposingLightPolarity(float[] gx, float[] gy, int width, int height)
        {
            int[] radii = { 6, 10, 16, 24 };
            float[] result = new float[gx.Length];
            int margin = radii[^1] + 2;

            for (int y = margin; y < height - margin; y++)
            {
                for (int x = margin; x < width - margin; x++)
                {
                    float best = 0.0f;
                    foreach (int r in radii)
                    {
                        int left = y * width + (x - r);
                        int right = y * width + (x + r);
                        int top = (y - r) * width + x;
                        int bottom = (y + r) * width + x;

                        float horizontal = OppositeSignedPair(gx[left], gx[right]);
                        float vertical = OppositeSignedPair(gy[top], gy[bottom]);

                        // One axis may be weak because of lighting or artwork. Reward both axes,
                        // but allow a strong single-axis reversal to remain a useful cue.
                        float score = (MathF.Min(horizontal, vertical) * 0.55f) +
                                      (MathF.Max(horizontal, vertical) * 0.45f);
                        if (score > best) best = score;
                    }
                    result[y * width + x] = best;
                }
            }

            return BoxBlur(result, width, height, 2);
        }

        private static float OppositeSignedPair(float a, float b)
        {
            if (a == 0.0f || b == 0.0f || MathF.Sign(a) == MathF.Sign(b))
                return 0.0f;

            float aa = MathF.Abs(a);
            float bb = MathF.Abs(b);
            float balance = MathF.Min(aa, bb) / (MathF.Max(aa, bb) + Epsilon);
            return MathF.Sqrt(aa * bb) * (0.55f + (0.45f * balance));
        }

        private static float[] LocalContrastNormalize(float[] source, int width, int height, int radius)
        {
            float[] local = BoxBlur(source, width, height, radius);
            float[] response = new float[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                float excess = MathF.Max(0.0f, source[i] - (local[i] * 0.72f));
                response[i] = excess / (local[i] + 0.08f);
            }
            return NormalizeByPercentile(response, 0.992f);
        }

        private static float[] CalculateDirectVisibleDimpleResponse(
            float[] relief,
            float[] normalChange,
            float[] curvature,
            int width,
            int height)
        {
            float[] blur2 = BoxBlur(relief, width, height, 2);
            float[] blur6 = BoxBlur(relief, width, height, 6);
            float[] blur12 = BoxBlur(relief, width, height, 12);
            float[] blur20 = BoxBlur(relief, width, height, 20);
            float[] output = new float[relief.Length];

            for (int i = 0; i < output.Length; i++)
            {
                float fine = MathF.Abs(blur2[i] - blur6[i]);
                float medium = MathF.Abs(blur6[i] - blur12[i]);
                float broad = MathF.Abs(blur12[i] - blur20[i]);
                float compact = MathF.Max(fine, MathF.Max(medium, broad));
                output[i] =
                    (compact * 0.52f) +
                    (normalChange[i] * 0.28f) +
                    (curvature[i] * 0.20f);
            }

            return output;
        }

        private static float CenterSurroundContrast(
            float[] source,
            int width,
            int height,
            int cx,
            int cy,
            int radius)
        {
            int inner = Math.Max(2, radius / 3);
            float innerMean = PatchMean(source, width, height, cx, cy, inner);
            float outerMean = PatchMean(source, width, height, cx, cy, radius);
            return MathF.Abs(innerMean - outerMean);
        }

        private static List<Region> FindDentCandidates(
            float[] dent,
            float[] polarity,
            float[] blob,
            float[] bump,
            float[] curvature,
            float[] normal,
            float[] consensus,
            float[] dimpleSeed,
            float[] directDimple,
            float[] relief,
            float[] appearance,
            float[] line,
            float[] validity,
            int width,
            int height,
            int border,
            int maximum)
        {
            float dentSeedThreshold = Math.Max(0.22f, Percentile(dent, 0.978f));
            float dimpleSeedThreshold = Math.Max(0.40f, Percentile(dimpleSeed, 0.985f));
            float directDimpleThreshold = Math.Max(0.30f, Percentile(directDimple, 0.965f));

            // Phase 6 performance fix:
            // Direct dimple seeding can produce hundreds/thousands of local maxima on busy
            // card artwork. Do NOT run the expensive multi-radius / multi-map neighbourhood
            // validation on every one of them. First collect cheap local maxima, rank them,
            // and validate only the strongest compact seed locations.
            List<(int X, int Y, float Priority)> rawSeeds = new();

            for (int y = border + 5; y < height - border - 5; y += 3)
            {
                for (int x = border + 5; x < width - border - 5; x += 3)
                {
                    int i = y * width + x;
                    bool dentSeed = dent[i] >= dentSeedThreshold && IsLocalMaximum(dent, width, height, x, y, 4);
                    bool directDimpleSeed = dimpleSeed[i] >= dimpleSeedThreshold && IsLocalMaximum(dimpleSeed, width, height, x, y, 5);
                    bool visibleDimpleSeed = directDimple[i] >= directDimpleThreshold && IsLocalMaximum(directDimple, width, height, x, y, 6);
                    if (!dentSeed && !directDimpleSeed && !visibleDimpleSeed)
                        continue;

                    float priority = MathF.Max(dent[i], MathF.Max(dimpleSeed[i], directDimple[i]));
                    priority += polarity[i] * 0.08f;
                    priority += consensus[i] * 0.06f;
                    if (visibleDimpleSeed) priority += 0.18f;
                    rawSeeds.Add((x, y, priority));
                }
            }

            // Keep enough seeds to catch subtle dimples, but bound worst-case runtime.
            // 96 seeds × 4 radii × 10 patch means is small and predictable on-device.
            List<(int X, int Y, float Priority)> selectedSeeds = rawSeeds
                .OrderByDescending(seed => seed.Priority)
                .Take(72)
                .ToList();

            List<Region> seeds = new(selectedSeeds.Count);
            int[] radii = { 8, 12, 18, 24 };

            foreach ((int x, int y, float _) in selectedSeeds)
            {
                int i = y * width + x;
                float bestScore = 0.0f;
                int bestRadius = 10;
                float bestPolarity = 0.0f;
                float bestShape = 0.0f;
                float bestArtwork = 0.0f;
                float bestLine = 0.0f;
                float bestConsensus = 0.0f;
                float bestDimpleSeed = 0.0f;
                float bestDirectDimple = 0.0f;
                float bestCenterSurround = 0.0f;

                foreach (int radius in radii)
                {
                    float p = PatchMean(polarity, width, height, x, y, radius);
                    float b = PatchMean(blob, width, height, x, y, radius);
                    float m = PatchMean(bump, width, height, x, y, radius);
                    float c = PatchMean(curvature, width, height, x, y, radius);
                    float n = PatchMean(normal, width, height, x, y, radius);
                    float cs = PatchMean(consensus, width, height, x, y, radius);
                    float ds = PatchMean(dimpleSeed, width, height, x, y, Math.Max(5, radius / 2));
                    float dd = PatchMean(directDimple, width, height, x, y, Math.Max(5, radius / 2));
                    float centerSurround = CenterSurroundContrast(relief, width, height, x, y, radius);
                    float a = PatchMean(appearance, width, height, x, y, radius);
                    float l = PatchMean(line, width, height, x, y, radius);
                    float v = PatchMean(validity, width, height, x, y, radius);

                    float shape = (p * 0.16f) + (b * 0.13f) + (m * 0.13f) + (n * 0.12f) + (c * 0.08f) + (cs * 0.05f) + (ds * 0.14f) + (dd * 0.13f) + (Math.Clamp(centerSurround * 8.0f, 0.0f, 1.0f) * 0.06f);
                    float artworkPenalty = Math.Clamp(a * (1.0f - (shape * 0.80f)) * 0.62f, 0.0f, 0.50f);
                    float linePenalty = Math.Clamp(l * 0.18f, 0.0f, 0.14f);
                    float score = Math.Clamp((shape * (0.86f + (dent[i] * 0.22f))) * v * (1.0f - artworkPenalty) * (1.0f - linePenalty), 0.0f, 1.0f);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestRadius = radius;
                        bestPolarity = p;
                        bestShape = shape;
                        bestArtwork = a;
                        bestLine = l;
                        bestConsensus = cs;
                        bestDimpleSeed = ds;
                        bestDirectDimple = dd;
                        bestCenterSurround = centerSurround;
                    }
                }

                bool strong3D = bestShape >= 0.38f && bestConsensus >= 0.18f;
                bool physicalReversal = bestPolarity >= 0.14f;
                bool obviousDimple = bestDimpleSeed >= 0.42f && bestShape >= 0.32f;
                bool directlyVisibleDimple = bestDirectDimple >= 0.40f &&
                                             bestCenterSurround >= 0.010f &&
                                             bestLine <= 0.72f;
                if ((!physicalReversal && !strong3D && !obviousDimple && !directlyVisibleDimple) ||
                    (bestScore < 0.27f && !directlyVisibleDimple))
                    continue;

                int boxRadius = Math.Clamp((int)Math.Round(bestRadius * 1.35), 8, 32);
                int x0 = Math.Max(border, x - boxRadius);
                int y0 = Math.Max(border, y - boxRadius);
                int x1 = Math.Min(width - border - 1, x + boxRadius);
                int y1 = Math.Min(height - border - 1, y + boxRadius);
                float confidence = Math.Clamp(
                    (bestScore * 0.62f) +
                    (bestPolarity * 0.13f) +
                    (bestConsensus * 0.10f) +
                    (bestDimpleSeed * 0.10f) +
                    (bestDirectDimple * 0.14f) +
                    (Math.Clamp(bestCenterSurround * 10.0f, 0.0f, 1.0f) * 0.06f), 0.0f, 1.0f);

                seeds.Add(new Region
                {
                    X = x0,
                    Y = y0,
                    Width = x1 - x0 + 1,
                    Height = y1 - y0 + 1,
                    Area = (x1 - x0 + 1) * (y1 - y0 + 1),
                    Peak = dent[i],
                    Mean = bestScore,
                    Confidence = confidence,
                    Evidence = $"Opposing light {bestPolarity:0.00} • dimple seed {bestDimpleSeed:0.00} • direct dimple {bestDirectDimple:0.00} • center-surround {bestCenterSurround:0.000} • 3D shape {bestShape:0.00} • consensus {bestConsensus:0.00} • artwork {bestArtwork:0.00} • line {bestLine:0.00}",
                    Kind = "POTENTIAL DENT"
                });
            }

            return NonMaximumSuppress(seeds, maximum, overlapThreshold: 0.22f);
        }

        private static float[] CalculateResidualRingDimpleResponse(float[] residual, int width, int height)
        {
            // Difference-of-Gaussians style response over several compact scales.
            // Circular dents in the neutral-vs-albedo subtraction commonly appear as
            // a bright/dark centre/ring structure even when artwork is busy.
            float[] s2 = BoxBlur(residual, width, height, 2);
            float[] s5 = BoxBlur(residual, width, height, 5);
            float[] s9 = BoxBlur(residual, width, height, 9);
            float[] s15 = BoxBlur(residual, width, height, 15);
            float[] output = new float[residual.Length];

            for (int i = 0; i < output.Length; i++)
            {
                float fine = MathF.Abs(s2[i] - s5[i]);
                float medium = MathF.Abs(s5[i] - s9[i]);
                float broad = MathF.Abs(s9[i] - s15[i]);
                // Prefer features that persist across at least two nearby spatial scales.
                float first = MathF.Max(fine, medium);
                float second = MathF.Min(MathF.Max(fine, broad), MathF.Max(medium, broad));
                output[i] = (first * 0.72f) + (second * 0.28f);
            }
            return output;
        }

        private static List<Region> FindHighVisibilityDimpleCandidates(
            float[] directDimple,
            float[] dimpleSeed,
            float[] residualDimple,
            float[] residual,
            float[] relief,
            float[] normal,
            float[] curvature,
            float[] polarity,
            float[] appearance,
            float[] line,
            float[] validity,
            int width,
            int height,
            int border,
            int maximum)
        {
            // This path is intentionally permissive compared with FindDentCandidates.
            // It exists for the exact case where the reconstructed 3D views plainly show
            // a dimple but the stricter cross-map consensus model rejects it.
            float directThreshold = Math.Max(0.18f, Percentile(directDimple, 0.920f));
            float seedThreshold = Math.Max(0.22f, Percentile(dimpleSeed, 0.935f));
            float residualThreshold = Math.Max(0.18f, Percentile(residualDimple, 0.925f));

            List<(int X, int Y, float Priority)> raw = new();
            for (int y = border + 6; y < height - border - 6; y += 2)
            {
                for (int x = border + 6; x < width - border - 6; x += 2)
                {
                    int i = y * width + x;
                    if (validity[i] < 0.48f)
                        continue;

                    bool directPeak = directDimple[i] >= directThreshold &&
                                      IsLocalMaximum(directDimple, width, height, x, y, 5);
                    bool seedPeak = dimpleSeed[i] >= seedThreshold &&
                                    IsLocalMaximum(dimpleSeed, width, height, x, y, 5);
                    bool residualPeak = residualDimple[i] >= residualThreshold &&
                                        IsLocalMaximum(residualDimple, width, height, x, y, 5);
                    if (!directPeak && !seedPeak && !residualPeak)
                        continue;

                    float localRelief = CenterSurroundContrast(relief, width, height, x, y, 14);
                    float localResidual = CenterSurroundContrast(residual, width, height, x, y, 14);
                    // A candidate may originate from either reconstructed relief OR the
                    // subtraction/ring map.  Do not require both at seed time.
                    if (localRelief < 0.0035f && localResidual < 0.018f)
                        continue;

                    float priority = MathF.Max(MathF.Max(directDimple[i], dimpleSeed[i]), residualDimple[i]);
                    priority += Math.Clamp(localRelief * 8.0f, 0.0f, 0.24f);
                    priority += Math.Clamp(localResidual * 2.2f, 0.0f, 0.24f);
                    priority += normal[i] * 0.06f;
                    priority += curvature[i] * 0.05f;
                    raw.Add((x, y, priority));
                }
            }

            List<(int X, int Y, float Priority)> selected = raw
                .OrderByDescending(item => item.Priority)
                .Take(28)
                .ToList();

            List<Region> regions = new();
            int[] radii = { 8, 11, 15, 20 };

            foreach ((int x, int y, float priority) in selected)
            {
                float bestConfidence = 0.0f;
                int bestRadius = 10;
                float bestDirect = 0.0f;
                float bestSeed = 0.0f;
                float bestResidualDimple = 0.0f;
                float bestResidualContrast = 0.0f;
                float bestRelief = 0.0f;
                float bestNormal = 0.0f;
                float bestCurvature = 0.0f;
                float bestPolarity = 0.0f;
                float bestAppearance = 0.0f;
                float bestLine = 0.0f;

                foreach (int radius in radii)
                {
                    float dd = PatchMean(directDimple, width, height, x, y, Math.Max(4, radius / 2));
                    float ds = PatchMean(dimpleSeed, width, height, x, y, Math.Max(4, radius / 2));
                    float rd = PatchMean(residualDimple, width, height, x, y, Math.Max(4, radius / 2));
                    float residualContrast = CenterSurroundContrast(residual, width, height, x, y, radius);
                    float reliefContrast = CenterSurroundContrast(relief, width, height, x, y, radius);
                    float n = PatchMean(normal, width, height, x, y, radius);
                    float c = PatchMean(curvature, width, height, x, y, radius);
                    float p = PatchMean(polarity, width, height, x, y, radius);
                    float a = PatchMean(appearance, width, height, x, y, radius);
                    float l = PatchMean(line, width, height, x, y, radius);
                    float v = PatchMean(validity, width, height, x, y, radius);

                    // Direct visibility owns the decision. Artwork/line penalties are soft.
                    float visibility =
                        (dd * 0.23f) +
                        (ds * 0.16f) +
                        (rd * 0.22f) +
                        (Math.Clamp(reliefContrast * 10.0f, 0.0f, 1.0f) * 0.14f) +
                        (Math.Clamp(residualContrast * 3.5f, 0.0f, 1.0f) * 0.13f) +
                        (n * 0.06f) +
                        (c * 0.04f) +
                        (p * 0.02f);

                    float softArtworkPenalty = Math.Clamp(a * 0.18f, 0.0f, 0.16f);
                    float softLinePenalty = Math.Clamp(l * 0.14f, 0.0f, 0.12f);
                    float confidence = Math.Clamp(
                        visibility * v * (1.0f - softArtworkPenalty) * (1.0f - softLinePenalty),
                        0.0f,
                        1.0f);

                    if (confidence > bestConfidence)
                    {
                        bestConfidence = confidence;
                        bestRadius = radius;
                        bestDirect = dd;
                        bestSeed = ds;
                        bestResidualDimple = rd;
                        bestResidualContrast = residualContrast;
                        bestRelief = reliefContrast;
                        bestNormal = n;
                        bestCurvature = c;
                        bestPolarity = p;
                        bestAppearance = a;
                        bestLine = l;
                    }
                }

                // Very small, simple gate: if a dimple is visibly present in the reconstructed
                // 3D response, don't let missing consensus erase it.
                bool reliefVisible = bestDirect >= 0.20f || bestSeed >= 0.25f || bestRelief >= 0.0050f;
                bool residualVisible = bestResidualDimple >= 0.20f || bestResidualContrast >= 0.020f;
                bool physicallySupported = bestNormal >= 0.075f || bestCurvature >= 0.075f || bestPolarity >= 0.06f;
                bool notLineArtifact = bestLine <= 0.88f;
                // Phase 9: an obvious subtraction/ring dimple is allowed through even when
                // the relief consensus is weaker.  Other 3D cues tune confidence rather than
                // vetoing a clearly reconstructed compact defect.
                if (!(reliefVisible || residualVisible) || !physicallySupported || !notLineArtifact || bestConfidence < 0.16f)
                    continue;

                int boxRadius = Math.Clamp((int)Math.Round(bestRadius * 1.25), 8, 28);
                int x0 = Math.Max(border, x - boxRadius);
                int y0 = Math.Max(border, y - boxRadius);
                int x1 = Math.Min(width - border - 1, x + boxRadius);
                int y1 = Math.Min(height - border - 1, y + boxRadius);

                // Map the rescue-path confidence into a user-facing potential-defect range.
                float rescueBoost = residualVisible ? 0.05f : 0.0f;
                float displayConfidence = Math.Clamp(0.55f + rescueBoost + (bestConfidence * 0.36f), 0.55f, 0.93f);
                regions.Add(new Region
                {
                    X = x0,
                    Y = y0,
                    Width = x1 - x0 + 1,
                    Height = y1 - y0 + 1,
                    Area = (x1 - x0 + 1) * (y1 - y0 + 1),
                    Peak = MathF.Max(bestDirect, bestSeed),
                    Mean = bestConfidence,
                    Confidence = displayConfidence,
                    Evidence = $"High-visibility dimple • direct {bestDirect:0.00} • seed {bestSeed:0.00} • residual-ring {bestResidualDimple:0.00} • residual contrast {bestResidualContrast:0.000} • relief {bestRelief:0.000} • normal {bestNormal:0.00} • curvature {bestCurvature:0.00} • polarity {bestPolarity:0.00} • artwork {bestAppearance:0.00} • line {bestLine:0.00}",
                    Kind = "POTENTIAL DENT"
                });
            }

            return NonMaximumSuppress(regions, maximum, overlapThreshold: 0.18f);
        }

        private static List<Region> FindScratchRegions(
            float[] scratch,
            float[] appearance,
            float[] line,
            int width,
            int height,
            float threshold,
            int border,
            int maximum)
        {
            byte[] visited = new byte[scratch.Length];
            int[] queue = new int[scratch.Length];
            List<Region> regions = new();

            for (int y = border; y < height - border; y++)
            {
                for (int x = border; x < width - border; x++)
                {
                    int start = y * width + x;
                    if (visited[start] != 0 || scratch[start] < threshold) continue;

                    int head = 0, tail = 0;
                    queue[tail++] = start;
                    visited[start] = 1;
                    int minX = x, maxX = x, minY = y, maxY = y, area = 0;
                    float peak = 0.0f, sum = 0.0f, lineSum = 0.0f, appearanceSum = 0.0f;

                    while (head < tail)
                    {
                        int i = queue[head++];
                        int py = i / width;
                        int px = i - (py * width);
                        area++;
                        peak = MathF.Max(peak, scratch[i]);
                        sum += scratch[i];
                        lineSum += line[i];
                        appearanceSum += appearance[i];
                        minX = Math.Min(minX, px); maxX = Math.Max(maxX, px);
                        minY = Math.Min(minY, py); maxY = Math.Max(maxY, py);

                        for (int yy = py - 1; yy <= py + 1; yy++)
                        {
                            if (yy < border || yy >= height - border) continue;
                            for (int xx = px - 1; xx <= px + 1; xx++)
                            {
                                if (xx < border || xx >= width - border) continue;
                                int ni = yy * width + xx;
                                if (visited[ni] == 0 && scratch[ni] >= threshold)
                                {
                                    visited[ni] = 1;
                                    queue[tail++] = ni;
                                }
                            }
                        }
                    }

                    int rw = maxX - minX + 1;
                    int rh = maxY - minY + 1;
                    if (area < 10 || rw < 2 || rh < 2) continue;
                    float aspect = Math.Max(rw, rh) / (float)Math.Max(1, Math.Min(rw, rh));
                    float fill = area / (float)Math.Max(1, rw * rh);
                    float lineMean = lineSum / Math.Max(area, 1);
                    float appearanceMean = appearanceSum / Math.Max(area, 1);
                    bool lineLike = aspect >= 2.0f || fill <= 0.40f;
                    if (!lineLike || area > width * height * 0.012f) continue;
                    if (lineMean < 0.22f) continue;

                    float mean = sum / area;
                    float confidence = Math.Clamp((peak * 0.50f) + (mean * 0.30f) + (lineMean * 0.20f) - (appearanceMean * 0.12f), 0.0f, 1.0f);
                    if (confidence < 0.42f) continue;

                    regions.Add(new Region
                    {
                        X = minX,
                        Y = minY,
                        Width = rw,
                        Height = rh,
                        Area = area,
                        Peak = peak,
                        Mean = mean,
                        Confidence = confidence,
                        Evidence = $"Line response {lineMean:0.00} • artwork {appearanceMean:0.00}",
                        Kind = "POTENTIAL SCRATCH"
                    });
                }
            }

            return NonMaximumSuppress(regions.OrderByDescending(r => r.Confidence).ToList(), maximum, overlapThreshold: 0.18f);
        }

        private static bool IsLocalMaximum(float[] data, int width, int height, int x, int y, int radius)
        {
            float center = data[y * width + x];
            for (int yy = Math.Max(0, y - radius); yy <= Math.Min(height - 1, y + radius); yy++)
            {
                for (int xx = Math.Max(0, x - radius); xx <= Math.Min(width - 1, x + radius); xx++)
                {
                    if (xx == x && yy == y) continue;
                    if (data[yy * width + xx] > center) return false;
                }
            }
            return true;
        }

        private static float PatchMean(float[] data, int width, int height, int cx, int cy, int radius)
        {
            int x0 = Math.Max(0, cx - radius);
            int x1 = Math.Min(width - 1, cx + radius);
            int y0 = Math.Max(0, cy - radius);
            int y1 = Math.Min(height - 1, cy + radius);
            double sum = 0.0;
            int count = 0;
            for (int y = y0; y <= y1; y += 2)
            {
                for (int x = x0; x <= x1; x += 2)
                {
                    sum += data[y * width + x];
                    count++;
                }
            }
            return count == 0 ? 0.0f : (float)(sum / count);
        }

        private static List<Region> NonMaximumSuppress(List<Region> candidates, int maximum, float overlapThreshold)
        {
            List<Region> kept = new();
            foreach (Region candidate in candidates.OrderByDescending(r => r.Confidence))
            {
                bool overlaps = kept.Any(existing => IntersectionOverUnion(candidate, existing) >= overlapThreshold || CentersTooClose(candidate, existing));
                if (overlaps) continue;
                kept.Add(candidate);
                if (kept.Count >= maximum) break;
            }
            return kept;
        }

        private static float IntersectionOverUnion(Region a, Region b)
        {
            int x0 = Math.Max(a.X, b.X);
            int y0 = Math.Max(a.Y, b.Y);
            int x1 = Math.Min(a.X + a.Width, b.X + b.Width);
            int y1 = Math.Min(a.Y + a.Height, b.Y + b.Height);
            int iw = Math.Max(0, x1 - x0);
            int ih = Math.Max(0, y1 - y0);
            float intersection = iw * ih;
            float union = (a.Width * a.Height) + (b.Width * b.Height) - intersection;
            return union <= 0 ? 0.0f : intersection / union;
        }

        private static bool CentersTooClose(Region a, Region b)
        {
            float ax = a.X + (a.Width * 0.5f);
            float ay = a.Y + (a.Height * 0.5f);
            float bx = b.X + (b.Width * 0.5f);
            float by = b.Y + (b.Height * 0.5f);
            float dx = ax - bx;
            float dy = ay - by;
            float distance = MathF.Sqrt((dx * dx) + (dy * dy));
            float minSpacing = MathF.Min(MathF.Max(a.Width, a.Height), MathF.Max(b.Width, b.Height)) * 0.55f;
            return distance < minSpacing;
        }

        private static List<Region> FindRegions(float[] score, float[] scratch, float[] dent, float[] dentBlob, float[] dentConsensus, int width, int height, float threshold, int border, int maximum)
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
                    float peak = 0, sum = 0, scratchSum = 0, dentSum = 0, radialSum = 0, consensusSum = 0;
                    while (head < tail)
                    {
                        int i = q[head++];
                        int py = i / width, px = i - py * width;
                        area++; sum += score[i]; peak = Math.Max(peak, score[i]);
                        scratchSum += scratch[i]; dentSum += dent[i];
                        radialSum += dentBlob[i]; consensusSum += dentConsensus[i];
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
                    int regionWidth = maxX - minX + 1;
                    int regionHeight = maxY - minY + 1;
                    if (area < 14 || regionWidth < 2 || regionHeight < 2) continue;

                    float fill = area / (float)Math.Max(1, regionWidth * regionHeight);
                    float aspect = Math.Max(regionWidth, regionHeight) / (float)Math.Max(1, Math.Min(regionWidth, regionHeight));
                    float scratchMean = scratchSum / Math.Max(area, 1);
                    float dentMean = dentSum / Math.Max(area, 1);
                    float blobMean = radialSum / Math.Max(area, 1);
                    float consensusMean = consensusSum / Math.Max(area, 1);
                    bool scratchKind = scratchMean >= dentMean;

                    // Morphology matters. Thin/elongated regions are credible scratch candidates;
                    // dents should be broader and spatially coherent. Very large filled regions are
                    // usually lighting drift, artwork/embossing, or a registration problem rather
                    // than a localized card defect.
                    if (scratchKind)
                    {
                        bool lineLike = aspect >= 2.1f || fill <= 0.42f;
                        if (!lineLike && area > 90) continue;
                        if (area > width * height * 0.025f) continue;
                    }
                    else
                    {
                        // A dent candidate must be a compact blob/ring-like structure with
                        // cross-map 3D support.  This is intentionally tolerant of asymmetric
                        // dents while still rejecting long player outlines, lettering and borders.
                        if (area < 12) continue;
                        if (area > width * height * 0.012f) continue;
                        if (aspect > 3.6f) continue;
                        if (fill < 0.12f) continue;
                        if (blobMean < 0.22f) continue;
                        if (consensusMean < 0.38f) continue;
                    }

                    regions.Add(new Region
                    {
                        X = minX,
                        Y = minY,
                        Width = regionWidth,
                        Height = regionHeight,
                        Area = area,
                        Peak = peak,
                        Mean = sum / area,
                        Kind = scratchKind ? "SCRATCH / SCUFF" : "DENT / IMPRESSION"
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
                bool scratch = region.Kind.Contains("SCRATCH", StringComparison.OrdinalIgnoreCase);
                Scalar color = scratch ? new Scalar(0, 215, 255) : new Scalar(0, 70, 255);
                Cv2.Rectangle(display, new OpenCvSharp.Rect(region.X, region.Y, region.Width, region.Height), color, 2);
                int confidencePercent = (int)Math.Clamp(MathF.Round(region.Confidence * 100.0f), 0, 100);
                string label = $"{number} {(scratch ? "SCRATCH" : "DENT")} {confidencePercent}%";
                Cv2.PutText(display, label, new OpenCvSharp.Point(region.X, Math.Max(16, region.Y - 5)), HersheyFonts.HersheySimplex, 0.42, color, 1, LineTypes.AntiAlias);
                number++;
            }
            Cv2.ImWrite(path, display, new[] { (int)ImwriteFlags.JpegQuality, 94 });
            await Task.CompletedTask;
        }
    }
}
