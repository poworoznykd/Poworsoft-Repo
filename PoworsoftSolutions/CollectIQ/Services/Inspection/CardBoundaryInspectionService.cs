using CollectIQ.Interfaces;
using CollectIQ.Models.Inspection.Geometry;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImageSharpImage = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;

namespace CollectIQ.Services.Inspection
{
    public sealed class CardBoundaryInspectionService
    {
        public const int CanonicalWidth = 750;
        public const int CanonicalHeight = 1050;

        private readonly ICardGeometryService geometryService;

        public CardBoundaryInspectionService(ICardGeometryService geometryService)
        {
            this.geometryService = geometryService;
        }

        public async Task<CardBoundaryInspectionResult> AnalyzeAsync(string imagePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                throw new InvalidOperationException("Capture or load a card image first.");

            using ImageSharpImage source = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(imagePath, cancellationToken);
            source.Mutate(x => x.AutoOrient());

            // EXACT same physical-card detector used by the working Centering workflow.
            CardGeometryResult geometry = geometryService.DetectCard(source);
            if (!geometry.Success || geometry.Corners.Length != 4)
                throw new InvalidOperationException("CollectIQ could not find all four physical outer card corners. Keep the entire card visible on a plain contrasting background and retake it.");

            using ImageSharpImage canonical = WarpToCanonical(source, geometry.Corners);
            string outputDirectory = Path.Combine(FileSystem.AppDataDirectory, "BoundaryInspections", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(outputDirectory);

            string canonicalPath = Path.Combine(outputDirectory, "normalized_card_processing.png");
            await SaveAsync(canonical, canonicalPath, cancellationToken);

            // Display copies intentionally include a dark border around the whole
            // physical card. Measurement still uses the exact 750x1050 canonical
            // card, but the UI never zooms/crops the edges out of view.
            string normalizedPath = Path.Combine(outputDirectory, "normalized_card_full_view.png");
            using (ImageSharpImage padded = CreatePaddedDisplayImage(canonical, 64))
            {
                await SaveAsync(padded, normalizedPath, cancellationToken);
            }

            float[] gray = ExtractLuminance(canonical);
            float[] chroma = ExtractChroma(canonical);
            float[] gradient = GradientMagnitude(gray, CanonicalWidth, CanonicalHeight);

            const int edgeBand = 34;
            const int cornerSize = 115;
            const int cornerInset = 10;

            RegionScore top = ScoreEdge(gray, chroma, gradient, EdgeSide.Top, edgeBand, cornerSize);
            RegionScore right = ScoreEdge(gray, chroma, gradient, EdgeSide.Right, edgeBand, cornerSize);
            RegionScore bottom = ScoreEdge(gray, chroma, gradient, EdgeSide.Bottom, edgeBand, cornerSize);
            RegionScore left = ScoreEdge(gray, chroma, gradient, EdgeSide.Left, edgeBand, cornerSize);

            RegionScore tl = ScoreCorner(gray, chroma, gradient, 0, 0, cornerSize, cornerInset);
            RegionScore tr = ScoreCorner(gray, chroma, gradient, CanonicalWidth - cornerSize, 0, cornerSize, cornerInset);
            RegionScore br = ScoreCorner(gray, chroma, gradient, CanonicalWidth - cornerSize, CanonicalHeight - cornerSize, cornerSize, cornerInset);
            RegionScore bl = ScoreCorner(gray, chroma, gradient, 0, CanonicalHeight - cornerSize, cornerSize, cornerInset);

            using ImageSharpImage edgeOverlayCanonical = canonical.Clone();
            DrawEdgeHeat(edgeOverlayCanonical, top, right, bottom, left, edgeBand);
            string edgeOverlayPath = Path.Combine(outputDirectory, "edge_analysis_full_view.png");
            using (ImageSharpImage edgeDisplay = CreatePaddedDisplayImage(edgeOverlayCanonical, 64))
            {
                await SaveAsync(edgeDisplay, edgeOverlayPath, cancellationToken);
            }

            using ImageSharpImage cornerOverlayCanonical = canonical.Clone();
            DrawCornerHeat(cornerOverlayCanonical, tl, tr, br, bl, cornerSize);
            string cornerOverlayPath = Path.Combine(outputDirectory, "corner_analysis_full_view.png");
            using (ImageSharpImage cornerDisplay = CreatePaddedDisplayImage(cornerOverlayCanonical, 64))
            {
                await SaveAsync(cornerDisplay, cornerOverlayPath, cancellationToken);
            }

            // Find the strongest local candidate in each corner/edge and create a
            // dedicated magnified inspection crop. These are supplemental closeups;
            // the full-card overview above remains the primary context.
            RegionHotspot tlHot = FindCornerHotspot(gray, chroma, gradient, 0, 0, cornerSize);
            RegionHotspot trHot = FindCornerHotspot(gray, chroma, gradient, CanonicalWidth - cornerSize, 0, cornerSize);
            RegionHotspot brHot = FindCornerHotspot(gray, chroma, gradient, CanonicalWidth - cornerSize, CanonicalHeight - cornerSize, cornerSize);
            RegionHotspot blHot = FindCornerHotspot(gray, chroma, gradient, 0, CanonicalHeight - cornerSize, cornerSize);

            RegionHotspot topHot = FindEdgeHotspot(gray, chroma, gradient, EdgeSide.Top, edgeBand, cornerSize);
            RegionHotspot rightHot = FindEdgeHotspot(gray, chroma, gradient, EdgeSide.Right, edgeBand, cornerSize);
            RegionHotspot bottomHot = FindEdgeHotspot(gray, chroma, gradient, EdgeSide.Bottom, edgeBand, cornerSize);
            RegionHotspot leftHot = FindEdgeHotspot(gray, chroma, gradient, EdgeSide.Left, edgeBand, cornerSize);

            string tlClose = await SaveCornerCloseupAsync(canonical, outputDirectory, "corner_top_left", 0, 0, cornerSize, tlHot, cancellationToken);
            string trClose = await SaveCornerCloseupAsync(canonical, outputDirectory, "corner_top_right", CanonicalWidth - cornerSize, 0, cornerSize, trHot, cancellationToken);
            string brClose = await SaveCornerCloseupAsync(canonical, outputDirectory, "corner_bottom_right", CanonicalWidth - cornerSize, CanonicalHeight - cornerSize, cornerSize, brHot, cancellationToken);
            string blClose = await SaveCornerCloseupAsync(canonical, outputDirectory, "corner_bottom_left", 0, CanonicalHeight - cornerSize, cornerSize, blHot, cancellationToken);

            string topClose = await SaveEdgeCloseupAsync(canonical, outputDirectory, "edge_top", EdgeSide.Top, edgeBand, cornerSize, topHot, cancellationToken);
            string rightClose = await SaveEdgeCloseupAsync(canonical, outputDirectory, "edge_right", EdgeSide.Right, edgeBand, cornerSize, rightHot, cancellationToken);
            string bottomClose = await SaveEdgeCloseupAsync(canonical, outputDirectory, "edge_bottom", EdgeSide.Bottom, edgeBand, cornerSize, bottomHot, cancellationToken);
            string leftClose = await SaveEdgeCloseupAsync(canonical, outputDirectory, "edge_left", EdgeSide.Left, edgeBand, cornerSize, leftHot, cancellationToken);

            return new CardBoundaryInspectionResult
            {
                NormalizedImagePath = normalizedPath,
                ProcessingImagePath = canonicalPath,
                EdgeOverlayPath = edgeOverlayPath,
                CornerOverlayPath = cornerOverlayPath,
                DetectionConfidence = geometry.Confidence * 100.0,
                TopEdge = top,
                RightEdge = right,
                BottomEdge = bottom,
                LeftEdge = left,
                TopLeft = tl,
                TopRight = tr,
                BottomRight = br,
                BottomLeft = bl,

                TopLeftCloseupPath = tlClose,
                TopRightCloseupPath = trClose,
                BottomRightCloseupPath = brClose,
                BottomLeftCloseupPath = blClose,
                TopLeftExplanation = BuildExplanation("top-left corner", tl, tlHot),
                TopRightExplanation = BuildExplanation("top-right corner", tr, trHot),
                BottomRightExplanation = BuildExplanation("bottom-right corner", br, brHot),
                BottomLeftExplanation = BuildExplanation("bottom-left corner", bl, blHot),

                TopEdgeCloseupPath = topClose,
                RightEdgeCloseupPath = rightClose,
                BottomEdgeCloseupPath = bottomClose,
                LeftEdgeCloseupPath = leftClose,
                TopEdgeExplanation = BuildExplanation("top edge", top, topHot),
                RightEdgeExplanation = BuildExplanation("right edge", right, rightHot),
                BottomEdgeExplanation = BuildExplanation("bottom edge", bottom, bottomHot),
                LeftEdgeExplanation = BuildExplanation("left edge", left, leftHot)
            };
        }

        private static RegionScore ScoreEdge(float[] gray, float[] chroma, float[] gradient, EdgeSide side, int band, int cornerExclusion)
        {
            List<float> g = new();
            List<float> white = new();
            List<float> texture = new();

            int x0 = side == EdgeSide.Left ? 2 : side == EdgeSide.Right ? CanonicalWidth - band : cornerExclusion;
            int x1 = side == EdgeSide.Left ? band : side == EdgeSide.Right ? CanonicalWidth - 2 : CanonicalWidth - cornerExclusion;
            int y0 = side == EdgeSide.Top ? 2 : side == EdgeSide.Bottom ? CanonicalHeight - band : cornerExclusion;
            int y1 = side == EdgeSide.Top ? band : side == EdgeSide.Bottom ? CanonicalHeight - 2 : CanonicalHeight - cornerExclusion;

            float innerMean = MeanRegion(gray,
                side == EdgeSide.Left ? band + 12 : side == EdgeSide.Right ? CanonicalWidth - band - 42 : cornerExclusion,
                side == EdgeSide.Top ? band + 12 : side == EdgeSide.Bottom ? CanonicalHeight - band - 42 : cornerExclusion,
                side is EdgeSide.Left or EdgeSide.Right ? 28 : CanonicalWidth - (cornerExclusion * 2),
                side is EdgeSide.Top or EdgeSide.Bottom ? 28 : CanonicalHeight - (cornerExclusion * 2));

            for (int y = Math.Max(1,y0); y < Math.Min(CanonicalHeight-1,y1); y++)
            for (int x = Math.Max(1,x0); x < Math.Min(CanonicalWidth-1,x1); x++)
            {
                int i=y*CanonicalWidth+x;
                g.Add(gradient[i]);
                float bright=MathF.Max(0, gray[i]-innerMean-0.07f);
                float lowChroma=Math.Clamp((0.18f-chroma[i])/0.18f,0,1);
                white.Add(bright*lowChroma);
                texture.Add(MathF.Abs(gray[i]-MedianCross(gray,x,y)));
            }

            return BuildScore(g, white, texture);
        }

        private static RegionScore ScoreCorner(float[] gray, float[] chroma, float[] gradient, int startX, int startY, int size, int inset)
        {
            List<float> g=new(); List<float> white=new(); List<float> texture=new();
            float interiorMean = MeanRegion(gray,
                Math.Clamp(startX + (startX == 0 ? size : -35), 0, CanonicalWidth-40),
                Math.Clamp(startY + (startY == 0 ? size : -35), 0, CanonicalHeight-40),
                35,35);

            for(int y=startY+inset; y<Math.Min(startY+size-inset,CanonicalHeight-1); y++)
            for(int x=startX+inset; x<Math.Min(startX+size-inset,CanonicalWidth-1); x++)
            {
                int localX=x-startX, localY=y-startY;
                bool nearOuter = localX < 35 || localY < 35 || localX > size-36 || localY > size-36;
                if(!nearOuter) continue;
                int i=y*CanonicalWidth+x;
                g.Add(gradient[i]);
                float bright=MathF.Max(0, gray[i]-interiorMean-0.06f);
                float lowChroma=Math.Clamp((0.20f-chroma[i])/0.20f,0,1);
                white.Add(bright*lowChroma);
                texture.Add(MathF.Abs(gray[i]-MedianCross(gray,x,y)));
            }
            return BuildScore(g,white,texture);
        }

        private static RegionScore BuildScore(List<float> gradient, List<float> whitening, List<float> texture)
        {
            float rough = Percentile(gradient,0.90f);
            float white = Percentile(whitening,0.94f);
            float tex = Percentile(texture,0.92f);
            double damage = Math.Clamp((rough*52.0)+(white*190.0)+(tex*105.0),0,100);
            string label = damage < 22 ? "Clean / low anomaly" : damage < 42 ? "Minor wear candidate" : damage < 68 ? "Moderate damage candidate" : "Strong damage candidate";
            return new RegionScore { DamageScore=damage, Roughness=rough, Whitening=white, Texture=tex, Label=label };
        }

        private static float MedianCross(float[] data,int x,int y)
        {
            Span<float> v=stackalloc float[5];
            v[0]=data[y*CanonicalWidth+x]; v[1]=data[y*CanonicalWidth+x-1]; v[2]=data[y*CanonicalWidth+x+1]; v[3]=data[(y-1)*CanonicalWidth+x]; v[4]=data[(y+1)*CanonicalWidth+x];
            v.Sort(); return v[2];
        }

        private static float MeanRegion(float[] data,int x,int y,int w,int h)
        {
            x=Math.Clamp(x,0,CanonicalWidth-1); y=Math.Clamp(y,0,CanonicalHeight-1); w=Math.Max(1,Math.Min(w,CanonicalWidth-x)); h=Math.Max(1,Math.Min(h,CanonicalHeight-y));
            double sum=0; int n=0;
            for(int yy=y;yy<y+h;yy++) for(int xx=x;xx<x+w;xx++){sum+=data[yy*CanonicalWidth+xx];n++;}
            return n==0?0:(float)(sum/n);
        }

        private static float Percentile(List<float> values,float p)
        {
            if(values.Count==0) return 0; values.Sort(); int idx=Math.Clamp((int)Math.Round((values.Count-1)*p),0,values.Count-1); return values[idx];
        }

        private static void DrawEdgeHeat(ImageSharpImage image, RegionScore top, RegionScore right, RegionScore bottom, RegionScore left, int band)
        {
            DrawBand(image,0,0,CanonicalWidth,band,top.DamageScore);
            DrawBand(image,CanonicalWidth-band,0,band,CanonicalHeight,right.DamageScore);
            DrawBand(image,0,CanonicalHeight-band,CanonicalWidth,band,bottom.DamageScore);
            DrawBand(image,0,0,band,CanonicalHeight,left.DamageScore);
        }

        private static void DrawCornerHeat(ImageSharpImage image, RegionScore tl, RegionScore tr, RegionScore br, RegionScore bl, int size)
        {
            DrawBox(image,0,0,size,size,tl.DamageScore);
            DrawBox(image,CanonicalWidth-size,0,size,size,tr.DamageScore);
            DrawBox(image,CanonicalWidth-size,CanonicalHeight-size,size,size,br.DamageScore);
            DrawBox(image,0,CanonicalHeight-size,size,size,bl.DamageScore);
        }

        private static void DrawBand(ImageSharpImage image,int x,int y,int w,int h,double score)
        {
            Rgba32 c=ScoreColor(score); int thickness=5;
            DrawRect(image,x,y,w,h,c,thickness);
        }
        private static void DrawBox(ImageSharpImage image,int x,int y,int w,int h,double score)=>DrawRect(image,x,y,w,h,ScoreColor(score),6);
        private static Rgba32 ScoreColor(double score)=>score<22?new Rgba32(34,197,94):score<42?new Rgba32(250,204,21):score<68?new Rgba32(249,115,22):new Rgba32(239,68,68);
        private static void DrawRect(ImageSharpImage image,int x,int y,int w,int h,Rgba32 c,int t)
        {
            for(int k=0;k<t;k++)
            {
                int x0=Math.Clamp(x+k,0,image.Width-1), x1=Math.Clamp(x+w-1-k,0,image.Width-1), y0=Math.Clamp(y+k,0,image.Height-1), y1=Math.Clamp(y+h-1-k,0,image.Height-1);
                for(int xx=x0;xx<=x1;xx++){image[xx,y0]=c;image[xx,y1]=c;}
                for(int yy=y0;yy<=y1;yy++){image[x0,yy]=c;image[x1,yy]=c;}
            }
        }

        private static ImageSharpImage CreatePaddedDisplayImage(ImageSharpImage canonical, int padding)
        {
            ImageSharpImage display = new(
                canonical.Width + (padding * 2),
                canonical.Height + (padding * 2),
                new Rgba32(5, 8, 20, 255));

            Rgba32[] canonicalPixels = new Rgba32[canonical.Width * canonical.Height];
            canonical.CopyPixelDataTo(canonicalPixels);

            display.ProcessPixelRows(target =>
            {
                for (int y = 0; y < canonical.Height; y++)
                {
                    ReadOnlySpan<Rgba32> src = canonicalPixels.AsSpan(
                        y * canonical.Width,
                        canonical.Width);
                    Span<Rgba32> dst = target.GetRowSpan(y + padding);
                    src.CopyTo(dst.Slice(padding, canonical.Width));
                }
            });

            return display;
        }

        private static RegionHotspot FindCornerHotspot(
            float[] gray,
            float[] chroma,
            float[] gradient,
            int startX,
            int startY,
            int size)
        {
            RegionHotspot best = new() { X = startX + (size / 2), Y = startY + (size / 2) };
            float interiorMean = MeanRegion(
                gray,
                Math.Clamp(startX + (startX == 0 ? size : -35), 0, CanonicalWidth - 40),
                Math.Clamp(startY + (startY == 0 ? size : -35), 0, CanonicalHeight - 40),
                35,
                35);

            for (int y = Math.Max(2, startY + 6); y < Math.Min(CanonicalHeight - 2, startY + size - 6); y++)
            for (int x = Math.Max(2, startX + 6); x < Math.Min(CanonicalWidth - 2, startX + size - 6); x++)
            {
                int localX = x - startX;
                int localY = y - startY;
                bool nearOuter = localX < 42 || localY < 42 || localX > size - 43 || localY > size - 43;
                if (!nearOuter) continue;

                RegionHotspot candidate = CalculateHotspot(gray, chroma, gradient, x, y, interiorMean);
                if (candidate.Score > best.Score)
                    best = candidate;
            }

            return best;
        }

        private static RegionHotspot FindEdgeHotspot(
            float[] gray,
            float[] chroma,
            float[] gradient,
            EdgeSide side,
            int band,
            int cornerExclusion)
        {
            int x0 = side == EdgeSide.Left ? 3 : side == EdgeSide.Right ? CanonicalWidth - band : cornerExclusion;
            int x1 = side == EdgeSide.Left ? band : side == EdgeSide.Right ? CanonicalWidth - 3 : CanonicalWidth - cornerExclusion;
            int y0 = side == EdgeSide.Top ? 3 : side == EdgeSide.Bottom ? CanonicalHeight - band : cornerExclusion;
            int y1 = side == EdgeSide.Top ? band : side == EdgeSide.Bottom ? CanonicalHeight - 3 : CanonicalHeight - cornerExclusion;

            float innerMean = MeanRegion(
                gray,
                side == EdgeSide.Left ? band + 12 : side == EdgeSide.Right ? CanonicalWidth - band - 42 : cornerExclusion,
                side == EdgeSide.Top ? band + 12 : side == EdgeSide.Bottom ? CanonicalHeight - band - 42 : cornerExclusion,
                side is EdgeSide.Left or EdgeSide.Right ? 28 : CanonicalWidth - (cornerExclusion * 2),
                side is EdgeSide.Top or EdgeSide.Bottom ? 28 : CanonicalHeight - (cornerExclusion * 2));

            RegionHotspot best = new()
            {
                X = Math.Clamp((x0 + x1) / 2, 0, CanonicalWidth - 1),
                Y = Math.Clamp((y0 + y1) / 2, 0, CanonicalHeight - 1)
            };

            for (int y = Math.Max(2, y0); y < Math.Min(CanonicalHeight - 2, y1); y++)
            for (int x = Math.Max(2, x0); x < Math.Min(CanonicalWidth - 2, x1); x++)
            {
                RegionHotspot candidate = CalculateHotspot(gray, chroma, gradient, x, y, innerMean);
                if (candidate.Score > best.Score)
                    best = candidate;
            }

            return best;
        }

        private static RegionHotspot CalculateHotspot(
            float[] gray,
            float[] chroma,
            float[] gradient,
            int x,
            int y,
            float interiorMean)
        {
            int i = (y * CanonicalWidth) + x;
            float whitening = MathF.Max(0, gray[i] - interiorMean - 0.055f) *
                              Math.Clamp((0.20f - chroma[i]) / 0.20f, 0, 1);
            float texture = MathF.Abs(gray[i] - MedianCross(gray, x, y));
            float roughness = gradient[i];

            double score = Math.Clamp(
                (roughness * 52.0) +
                (whitening * 190.0) +
                (texture * 105.0),
                0,
                100);

            string dominant =
                whitening >= roughness && whitening >= texture ? "whitening/chipping" :
                texture >= roughness ? "surface/edge texture disruption" :
                "rough or irregular boundary";

            return new RegionHotspot
            {
                X = x,
                Y = y,
                Score = score,
                Roughness = roughness,
                Whitening = whitening,
                Texture = texture,
                DominantEvidence = dominant
            };
        }

        private static string BuildExplanation(string regionName, RegionScore score, RegionHotspot hot)
        {
            double roughnessEvidence = Math.Clamp(hot.Roughness * 100.0, 0, 100);
            double whiteningEvidence = Math.Clamp(hot.Whitening * 500.0, 0, 100);
            double textureEvidence = Math.Clamp(hot.Texture * 300.0, 0, 100);

            bool structuralSupport =
                roughnessEvidence >= 20 ||
                textureEvidence >= 18;

            bool whiteningOnly =
                whiteningEvidence >= 20 &&
                roughnessEvidence < 18 &&
                textureEvidence < 16;

            string verdict;
            string meaning;

            if (score.DamageScore < 22)
            {
                verdict = "RESULT: NO MEANINGFUL DAMAGE SIGNAL";
                meaning =
                    "The machine-vision response is weak. This area is not being called damaged.";
            }
            else if (whiteningOnly && score.DamageScore < 58)
            {
                verdict = "RESULT: LIKELY NORMAL BORDER / LIGHTING";
                meaning =
                    "Brightness changed, but there is not enough matching roughness or texture break to support a chip or frayed edge. " +
                    "Treat this as a likely false positive unless the physical card visibly shows damage here.";
            }
            else if (score.DamageScore < 42 || !structuralSupport)
            {
                verdict = "RESULT: WEAK / INCONCLUSIVE CANDIDATE";
                meaning =
                    "Something changed at this location, but the evidence is not strong enough to confidently call physical damage.";
            }
            else if (score.DamageScore < 68)
            {
                verdict = "RESULT: POSSIBLE PHYSICAL DAMAGE";
                meaning =
                    "The highlighted location has more than one supporting machine-vision cue. Inspect this exact area for whitening, chipping, fraying, rounding or a broken edge profile.";
            }
            else
            {
                verdict = "RESULT: STRONG PHYSICAL-DAMAGE CANDIDATE";
                meaning =
                    "Multiple machine-vision cues agree at this location. This is the highest-priority area to inspect manually.";
            }

            string dominantDescription = hot.DominantEvidence switch
            {
                "whitening/chipping" =>
                    "brightness/low-colour change that can be caused by exposed paper fibres, but can also come from a naturally white border or glare",
                "surface/edge texture disruption" =>
                    "a local texture break compared with nearby card material, which is more consistent with scuffing, chipping or crushed fibres",
                _ =>
                    "an irregular local boundary/gradient response, which can be caused by a nick, fray, rounding or a deformed edge"
            };

            return
                $"{verdict}\n\n" +
                $"WHAT IT IS LOOKING AT: The highlighted square is the strongest response found along the physical {regionName}. " +
                $"The detector compares that outer boundary with nearby card material; it is not grading the printed player/artwork in the middle of the card.\n\n" +
                $"WHY IT PICKED THIS SPOT: Mostly {hot.DominantEvidence}. In plain language, that means {dominantDescription}.\n\n" +
                $"EVIDENCE: overall {score.DamageScore:0}/100 • roughness {roughnessEvidence:0}/100 • whitening {whiteningEvidence:0}/100 • texture break {textureEvidence:0}/100.\n\n" +
                $"{meaning}";
        }

        private static async Task<string> SaveCornerCloseupAsync(
            ImageSharpImage canonical,
            string outputDirectory,
            string name,
            int x,
            int y,
            int size,
            RegionHotspot hot,
            CancellationToken ct)
        {
            int margin = 28;
            int cropX = Math.Clamp(x - margin, 0, CanonicalWidth - 1);
            int cropY = Math.Clamp(y - margin, 0, CanonicalHeight - 1);
            int cropRight = Math.Min(CanonicalWidth, x + size + margin);
            int cropBottom = Math.Min(CanonicalHeight, y + size + margin);
            int cropW = Math.Max(1, cropRight - cropX);
            int cropH = Math.Max(1, cropBottom - cropY);

            using ImageSharpImage crop = canonical.Clone(context =>
                context.Crop(new SixLabors.ImageSharp.Rectangle(cropX, cropY, cropW, cropH)));

            int localX = hot.X - cropX;
            int localY = hot.Y - cropY;
            DrawHotspotBox(crop, localX, localY, hot.Score);

            using ImageSharpImage enlarged = crop.Clone(context =>
                context.Resize(crop.Width * 3, crop.Height * 3, KnownResamplers.NearestNeighbor));

            string path = Path.Combine(outputDirectory, name + "_closeup.png");
            await SaveAsync(enlarged, path, ct);
            return path;
        }

        private static async Task<string> SaveEdgeCloseupAsync(
            ImageSharpImage canonical,
            string outputDirectory,
            string name,
            EdgeSide side,
            int band,
            int cornerExclusion,
            RegionHotspot hot,
            CancellationToken ct)
        {
            int x;
            int y;
            int w;
            int h;

            if (side == EdgeSide.Top || side == EdgeSide.Bottom)
            {
                x = Math.Max(0, cornerExclusion - 20);
                w = CanonicalWidth - (x * 2);
                h = 110;
                y = side == EdgeSide.Top ? 0 : CanonicalHeight - h;
            }
            else
            {
                y = Math.Max(0, cornerExclusion - 20);
                h = CanonicalHeight - (y * 2);
                w = 110;
                x = side == EdgeSide.Left ? 0 : CanonicalWidth - w;
            }

            using ImageSharpImage crop = canonical.Clone(context =>
                context.Crop(new SixLabors.ImageSharp.Rectangle(x, y, w, h)));

            DrawHotspotBox(crop, hot.X - x, hot.Y - y, hot.Score);

            int scale = side == EdgeSide.Top || side == EdgeSide.Bottom ? 2 : 2;
            using ImageSharpImage enlarged = crop.Clone(context =>
                context.Resize(crop.Width * scale, crop.Height * scale, KnownResamplers.NearestNeighbor));

            string path = Path.Combine(outputDirectory, name + "_closeup.png");
            await SaveAsync(enlarged, path, ct);
            return path;
        }

        private static void DrawHotspotBox(ImageSharpImage image, int cx, int cy, double score)
        {
            // Do not visually imply damage when the response is below the screening threshold.
            if (score < 22)
                return;

            Rgba32 color = ScoreColor(score);
            int half = score >= 68 ? 22 : 18;
            int x = Math.Clamp(cx - half, 0, image.Width - 1);
            int y = Math.Clamp(cy - half, 0, image.Height - 1);
            int w = Math.Max(1, Math.Min(half * 2, image.Width - x));
            int h = Math.Max(1, Math.Min(half * 2, image.Height - y));
            DrawRect(image, x, y, w, h, color, score >= 68 ? 6 : 5);
        }

        private static float[] ExtractLuminance(ImageSharpImage image)
        {
            float[] result=new float[image.Width*image.Height]; int p=0;
            image.ProcessPixelRows(a=>{for(int y=0;y<image.Height;y++){Span<Rgba32> row=a.GetRowSpan(y);for(int x=0;x<image.Width;x++){Rgba32 c=row[x];result[p++]=(0.2126f*c.R+0.7152f*c.G+0.0722f*c.B)/255f;}}}); return result;
        }
        private static float[] ExtractChroma(ImageSharpImage image)
        {
            float[] result=new float[image.Width*image.Height]; int p=0;
            image.ProcessPixelRows(a=>{for(int y=0;y<image.Height;y++){Span<Rgba32> row=a.GetRowSpan(y);for(int x=0;x<image.Width;x++){Rgba32 c=row[x];float max=Math.Max(c.R,Math.Max(c.G,c.B));float min=Math.Min(c.R,Math.Min(c.G,c.B));result[p++]=(max-min)/255f;}}}); return result;
        }
        private static float[] GradientMagnitude(float[] g,int w,int h)
        {
            float[] o=new float[g.Length]; for(int y=1;y<h-1;y++)for(int x=1;x<w-1;x++){int i=y*w+x;float dx=(g[i+1]-g[i-1])*0.5f,dy=(g[i+w]-g[i-w])*0.5f;o[i]=MathF.Sqrt(dx*dx+dy*dy);} return o;
        }

        private static async Task SaveAsync(ImageSharpImage image,string path,CancellationToken ct)
        {
            await using FileStream fs=new(path,FileMode.Create,FileAccess.Write,FileShare.None); await image.SaveAsync(fs,new PngEncoder(),ct);
        }

        private static ImageSharpImage WarpToCanonical(ImageSharpImage source,IReadOnlyList<CardPoint> sourceCorners)
        {
            CardPoint[] destination={new(0,0),new(CanonicalWidth-1,0),new(CanonicalWidth-1,CanonicalHeight-1),new(0,CanonicalHeight-1)};
            double[] matrix=SolveHomography(destination,sourceCorners); Rgba32[] src=CopyPixels(source); ImageSharpImage output=new(CanonicalWidth,CanonicalHeight);
            output.ProcessPixelRows(a=>{for(int y=0;y<CanonicalHeight;y++){Span<Rgba32> row=a.GetRowSpan(y);for(int x=0;x<CanonicalWidth;x++){MapProjective(matrix,x,y,out float sx,out float sy);row[x]=SampleBilinear(src,source.Width,source.Height,sx,sy);}}}); return output;
        }
        private static Rgba32[] CopyPixels(ImageSharpImage image){Rgba32[] p=new Rgba32[image.Width*image.Height];image.CopyPixelDataTo(p);return p;}
        private static Rgba32 SampleBilinear(Rgba32[] p,int w,int h,float x,float y)
        {
            if(x<0||y<0||x>w-1||y>h-1)return new Rgba32(0,0,0,255);int x0=(int)MathF.Floor(x),y0=(int)MathF.Floor(y),x1=Math.Min(x0+1,w-1),y1=Math.Min(y0+1,h-1);float fx=x-x0,fy=y-y0;Rgba32 a=p[y0*w+x0],b=p[y0*w+x1],c=p[y1*w+x0],d=p[y1*w+x1];return new Rgba32(Lerp(Lerp(a.R,b.R,fx),Lerp(c.R,d.R,fx),fy),Lerp(Lerp(a.G,b.G,fx),Lerp(c.G,d.G,fx),fy),Lerp(Lerp(a.B,b.B,fx),Lerp(c.B,d.B,fx),fy),255);
        }
        private static byte Lerp(byte a,byte b,float t)=>(byte)Math.Clamp((int)MathF.Round(a+((b-a)*t)),0,255);
        private static void MapProjective(double[] m,float x,float y,out float sx,out float sy){double d=(m[6]*x)+(m[7]*y)+1.0;sx=(float)(((m[0]*x)+(m[1]*y)+m[2])/d);sy=(float)(((m[3]*x)+(m[4]*y)+m[5])/d);}
        private static double[] SolveHomography(IReadOnlyList<CardPoint> destination,IReadOnlyList<CardPoint> source)
        {
            double[,] a=new double[8,8];double[] b=new double[8];for(int i=0;i<4;i++){double x=destination[i].X,y=destination[i].Y,u=source[i].X,v=source[i].Y;int r=i*2;a[r,0]=x;a[r,1]=y;a[r,2]=1;a[r,6]=-u*x;a[r,7]=-u*y;b[r]=u;a[r+1,3]=x;a[r+1,4]=y;a[r+1,5]=1;a[r+1,6]=-v*x;a[r+1,7]=-v*y;b[r+1]=v;}return GaussianElimination(a,b);
        }
        private static double[] GaussianElimination(double[,] a,double[] b)
        {
            int n=b.Length;double[,] m=new double[n,n+1];for(int r=0;r<n;r++){for(int c=0;c<n;c++)m[r,c]=a[r,c];m[r,n]=b[r];}for(int c=0;c<n;c++){int pivot=c;for(int r=c+1;r<n;r++)if(Math.Abs(m[r,c])>Math.Abs(m[pivot,c]))pivot=r;if(pivot!=c)for(int k=c;k<=n;k++){double tmp=m[c,k];m[c,k]=m[pivot,k];m[pivot,k]=tmp;}double div=m[c,c];if(Math.Abs(div)<1e-12)throw new InvalidOperationException("Could not solve card perspective transform.");for(int k=c;k<=n;k++)m[c,k]/=div;for(int r=0;r<n;r++){if(r==c)continue;double f=m[r,c];for(int k=c;k<=n;k++)m[r,k]-=f*m[c,k];}}double[] x=new double[n];for(int i=0;i<n;i++)x[i]=m[i,n];return x;
        }
        private enum EdgeSide { Top,Right,Bottom,Left }
    }

    public sealed class CardBoundaryInspectionResult
    {
        public string NormalizedImagePath { get; set; }=string.Empty;
        public string ProcessingImagePath { get; set; }=string.Empty;
        public string EdgeOverlayPath { get; set; }=string.Empty;
        public string CornerOverlayPath { get; set; }=string.Empty;
        public double DetectionConfidence { get; set; }
        public RegionScore TopEdge { get; set; }=new(); public RegionScore RightEdge { get; set; }=new(); public RegionScore BottomEdge { get; set; }=new(); public RegionScore LeftEdge { get; set; }=new();
        public RegionScore TopLeft { get; set; }=new(); public RegionScore TopRight { get; set; }=new(); public RegionScore BottomRight { get; set; }=new(); public RegionScore BottomLeft { get; set; }=new();

        public string TopLeftCloseupPath { get; set; }=string.Empty;
        public string TopRightCloseupPath { get; set; }=string.Empty;
        public string BottomRightCloseupPath { get; set; }=string.Empty;
        public string BottomLeftCloseupPath { get; set; }=string.Empty;
        public string TopLeftExplanation { get; set; }=string.Empty;
        public string TopRightExplanation { get; set; }=string.Empty;
        public string BottomRightExplanation { get; set; }=string.Empty;
        public string BottomLeftExplanation { get; set; }=string.Empty;

        public string TopEdgeCloseupPath { get; set; }=string.Empty;
        public string RightEdgeCloseupPath { get; set; }=string.Empty;
        public string BottomEdgeCloseupPath { get; set; }=string.Empty;
        public string LeftEdgeCloseupPath { get; set; }=string.Empty;
        public string TopEdgeExplanation { get; set; }=string.Empty;
        public string RightEdgeExplanation { get; set; }=string.Empty;
        public string BottomEdgeExplanation { get; set; }=string.Empty;
        public string LeftEdgeExplanation { get; set; }=string.Empty;
    }

    internal sealed class RegionHotspot
    {
        public int X { get; set; }
        public int Y { get; set; }
        public double Score { get; set; }
        public float Roughness { get; set; }
        public float Whitening { get; set; }
        public float Texture { get; set; }
        public string DominantEvidence { get; set; }=string.Empty;
    }

    public sealed class RegionScore
    {
        public double DamageScore { get; set; } public float Roughness { get; set; } public float Whitening { get; set; } public float Texture { get; set; } public string Label { get; set; }=string.Empty;
    }
}
