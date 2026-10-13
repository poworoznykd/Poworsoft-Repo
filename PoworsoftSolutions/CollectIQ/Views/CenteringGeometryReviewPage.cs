using CollectIQ.Models.Inspection.Geometry;
using OpenCvSharp;

namespace CollectIQ.Views
{
    public sealed class CenteringGeometryReviewPage : ContentPage
    {
        private readonly TaskCompletionSource<CardPoint[]?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly string previewPath;
        private CardPoint[] physicalCorners;
        private int quarterTurns;
        private readonly Image overlayImage;
        private readonly Image flatImage;
        private readonly Label statusLabel;

        public CenteringGeometryReviewPage(
            string previewPath,
            string detectedOverlayPath,
            string trueFormPath,
            IReadOnlyList<CardPoint> initialCorners)
        {
            this.previewPath = previewPath;
            physicalCorners = initialCorners.Select(p => new CardPoint(p.X, p.Y)).ToArray();

            var previewInfo = SixLabors.ImageSharp.Image.Identify(previewPath);
            if (previewInfo is not null)
                quarterTurns = DeterminePortraitQuarterTurns(physicalCorners, previewInfo.Width, previewInfo.Height);

            Title = "Confirm Centering Card Lock";
            BackgroundColor = Color.FromArgb("#050814");

            overlayImage = new Image
            {
                Source = File.Exists(detectedOverlayPath) ? ImageSource.FromFile(detectedOverlayPath) : ImageSource.FromFile(previewPath),
                Aspect = Aspect.AspectFit,
                HeightRequest = 300
            };
            flatImage = new Image
            {
                Source = File.Exists(trueFormPath) ? ImageSource.FromFile(trueFormPath) : null,
                Aspect = Aspect.AspectFit,
                HeightRequest = 300
            };
            statusLabel = new Label
            {
                Text = "Check all four physical corners and make sure the flat card is upright before continuing.",
                TextColor = Color.FromArgb("#E2E8F0"),
                FontSize = 13
            };

            var adjust = MakeButton("ADJUST 4 CORNERS", "#253047");
            adjust.Clicked += async (_, _) => await AdjustCornersAsync();

            var rotateLeft = MakeButton("↺ LEFT", "#253047");
            rotateLeft.Clicked += async (_, _) => await RotateAsync(-1);
            var rotate180 = MakeButton("↻ 180°", "#253047");
            rotate180.Clicked += async (_, _) => await RotateAsync(2);
            var rotateRight = MakeButton("RIGHT ↻", "#253047");
            rotateRight.Clicked += async (_, _) => await RotateAsync(1);

            var confirm = MakeButton("CONFIRM CARD LOCK → CENTERING", "#1D6A2A");
            confirm.HeightRequest = 54;
            confirm.FontAttributes = FontAttributes.Bold;
            confirm.Clicked += async (_, _) =>
            {
                completion.TrySetResult(ApplyQuarterTurns(physicalCorners, quarterTurns));
                await Navigation.PopModalAsync();
            };

            var cancel = MakeButton("CANCEL", "#3B1720");
            cancel.Clicked += async (_, _) =>
            {
                completion.TrySetResult(null);
                await Navigation.PopModalAsync();
            };

            Content = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Padding = new Thickness(16, 18, 16, 28),
                    Spacing = 12,
                    Children =
                    {
                        new Label
                        {
                            Text = "CONFIRM THE 4 PHYSICAL CARD CORNERS",
                            TextColor = Color.FromArgb("#86FF70"),
                            FontSize = 22,
                            FontAttributes = FontAttributes.Bold
                        },
                        new Label
                        {
                            Text = "CollectIQ found these corners automatically. If they are exact, confirm them. If not, open the precision editor. The TrueForm preview must also be upright; use the rotate buttons if the camera orientation came through incorrectly.",
                            TextColor = Color.FromArgb("#9FB3C8"),
                            FontSize = 12
                        },
                        statusLabel,
                        new Label { Text = "AUTO CARD LOCK", TextColor = Colors.White, FontAttributes = FontAttributes.Bold },
                        overlayImage,
                        adjust,
                        new Label { Text = "TRUEFORM ORIENTATION", TextColor = Colors.White, FontAttributes = FontAttributes.Bold },
                        flatImage,
                        new HorizontalStackLayout { Spacing = 8, Children = { rotateLeft, rotate180, rotateRight } },
                        new HorizontalStackLayout { Spacing = 8, Children = { cancel, confirm } }
                    }
                }
            };

            // The camera may deliver a landscape-oriented card even though the physical
            // trading card is portrait. Rebuild immediately using the card's long/short
            // side geometry so TrueForm starts portrait before the user reviews it.
            Dispatcher.Dispatch(async () => await RebuildAsync());
        }

        public Task<CardPoint[]?> WaitForCornersAsync() => completion.Task;

        protected override bool OnBackButtonPressed()
        {
            completion.TrySetResult(null);
            return base.OnBackButtonPressed();
        }

        private async Task AdjustCornersAsync()
        {
            var editor = new CardCornerEditorPage(
                previewPath,
                "CENTERING — PRECISION 4-CORNER REVIEW",
                "The automatic corners are loaded. Adjust only what is wrong, then use the magnified crosshair and 1-pixel arrows for exact placement.",
                "UPDATE CENTERING CARD LOCK",
                physicalCorners);

            await Navigation.PushModalAsync(editor);
            CardPoint[]? updated = await editor.WaitForCornersAsync();
            if (updated is null)
                return;

            physicalCorners = updated;
            await RebuildAsync();
            statusLabel.Text = "✓ Manual corner update applied. Check the overlay and TrueForm again before confirming.";
            statusLabel.TextColor = Color.FromArgb("#86FF70");
        }

        private async Task RotateAsync(int delta)
        {
            quarterTurns = ((quarterTurns + delta) % 4 + 4) % 4;
            await RebuildAsync();
            statusLabel.Text = quarterTurns == 0
                ? "TrueForm orientation reset to the detected direction."
                : $"TrueForm orientation adjusted by {quarterTurns * 90}°. Confirm only when the card is upright.";
            statusLabel.TextColor = Color.FromArgb("#FACC15");
        }

        private async Task RebuildAsync()
        {
            try
            {
                ReviewImages images = await Task.Run(() => BuildImages());
                overlayImage.Source = ImageSource.FromFile(images.OverlayPath);
                flatImage.Source = ImageSource.FromFile(images.FlatPath);
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"Preview update failed: {ex.Message}";
                statusLabel.TextColor = Color.FromArgb("#FF8A80");
            }
        }

        private ReviewImages BuildImages()
        {
            using Mat source = Cv2.ImRead(previewPath, ImreadModes.Color);
            if (source.Empty())
                throw new InvalidOperationException("Could not reopen the oriented Centering preview.");

            CardPoint[] pixelPhysical = physicalCorners.Select(p => new CardPoint(
                p.X * Math.Max(source.Width - 1, 1),
                p.Y * Math.Max(source.Height - 1, 1))).ToArray();

            string directory = Path.GetDirectoryName(previewPath) ?? Path.GetTempPath();
            string token = Guid.NewGuid().ToString("N");
            string overlayPath = Path.Combine(directory, $"centering_confirm_{token}.jpg");
            string flatPath = Path.Combine(directory, $"centering_trueform_confirm_{token}.jpg");

            using (Mat overlay = source.Clone())
            {
                Scalar green = new(20, 255, 20);
                Scalar yellow = new(0, 235, 255);
                string[] labels = { "TL", "TR", "BR", "BL" };
                for (int i = 0; i < 4; i++)
                {
                    var a = new OpenCvSharp.Point((int)Math.Round(pixelPhysical[i].X), (int)Math.Round(pixelPhysical[i].Y));
                    var b = new OpenCvSharp.Point((int)Math.Round(pixelPhysical[(i + 1) % 4].X), (int)Math.Round(pixelPhysical[(i + 1) % 4].Y));
                    Cv2.Line(overlay, a, b, green, 3, LineTypes.AntiAlias);
                    Cv2.Circle(overlay, a, 9, yellow, -1, LineTypes.AntiAlias);
                    Cv2.PutText(overlay, labels[i], a + new OpenCvSharp.Point(8, -8), HersheyFonts.HersheySimplex, 0.55, green, 2, LineTypes.AntiAlias);
                }
                Cv2.ImWrite(overlayPath, overlay, new[] { (int)ImwriteFlags.JpegQuality, 94 });
            }

            CardPoint[] oriented = ApplyQuarterTurns(physicalCorners, quarterTurns);
            Point2f[] src = oriented.Select(p => new Point2f(
                p.X * Math.Max(source.Width - 1, 1),
                p.Y * Math.Max(source.Height - 1, 1))).ToArray();
            Point2f[] dst =
            {
                new(22, 32), new(727, 32), new(727, 1017), new(22, 1017)
            };
            using Mat transform = Cv2.GetPerspectiveTransform(src, dst);
            using Mat flat = new(1050, 750, source.Type(), Scalar.Black);
            Cv2.WarpPerspective(source, flat, transform, new OpenCvSharp.Size(750, 1050), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.Black);
            Cv2.ImWrite(flatPath, flat, new[] { (int)ImwriteFlags.JpegQuality, 94 });

            return new ReviewImages(overlayPath, flatPath);
        }

        private static int DeterminePortraitQuarterTurns(IReadOnlyList<CardPoint> corners, int width, int height)
        {
            if (corners.Count != 4)
                return 0;

            static double Distance(CardPoint a, CardPoint b, int w, int h)
            {
                double dx = (b.X - a.X) * Math.Max(w - 1, 1);
                double dy = (b.Y - a.Y) * Math.Max(h - 1, 1);
                return Math.Sqrt((dx * dx) + (dy * dy));
            }

            double imageHorizontalSides = (Distance(corners[0], corners[1], width, height) +
                                           Distance(corners[2], corners[3], width, height)) * 0.5;
            double imageVerticalSides = (Distance(corners[1], corners[2], width, height) +
                                         Distance(corners[3], corners[0], width, height)) * 0.5;

            // A standard card is taller than it is wide. If the current top/bottom
            // sides are the long sides, the card is sideways in the source image.
            return imageHorizontalSides > imageVerticalSides ? 1 : 0;
        }

        private static CardPoint[] ApplyQuarterTurns(IReadOnlyList<CardPoint> corners, int turns)
        {
            int shift = ((turns % 4) + 4) % 4;
            return Enumerable.Range(0, 4)
                .Select(i => corners[(i - shift + 4) % 4])
                .Select(p => new CardPoint(p.X, p.Y))
                .ToArray();
        }

        private static Button MakeButton(string text, string background) => new()
        {
            Text = text,
            BackgroundColor = Color.FromArgb(background),
            TextColor = Colors.White,
            CornerRadius = 16
        };

        private sealed record ReviewImages(string OverlayPath, string FlatPath);
    }
}
