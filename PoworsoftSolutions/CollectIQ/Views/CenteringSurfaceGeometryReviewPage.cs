using CollectIQ.Models.Inspection.Geometry;
using CollectIQ.Models.Inspection.Registration;
using OpenCvSharp;

namespace CollectIQ.Views
{
    /// <summary>
    /// Centering uses the same geometry-review behavior as Surface, but for one image.
    /// The editor receives the same bounded OpenCV preview and normalized corner coordinates
    /// that Surface uses, so coarse taps, precision zoom, and arrow nudges share one coordinate system.
    /// </summary>
    public sealed class CenteringSurfaceGeometryReviewPage : ContentPage
    {
        private const int CanonicalWidth = 750;
        private const int CanonicalHeight = 1050;

        private readonly TaskCompletionSource<CardPoint[]?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly RegisteredCardFrame frame;
        private CardPoint[] frameCorners;
        private int orientationQuarterTurns;
        private readonly Image overlayImage;
        private readonly Image flatImage;
        private readonly Label statusLabel;

        public CenteringSurfaceGeometryReviewPage(RegisteredCardFrame frame)
        {
            this.frame = frame;
            frameCorners = Normalize(frame).ToArray();

            Title = "Centering Geometry Review";
            BackgroundColor = Color.FromArgb("#050814");

            overlayImage = new Image
            {
                Source = File.Exists(frame.DetectionOverlayPath) ? ImageSource.FromFile(frame.DetectionOverlayPath) : null,
                Aspect = Aspect.AspectFit,
                HeightRequest = 280
            };
            flatImage = new Image
            {
                Source = File.Exists(frame.RegisteredImagePath) ? ImageSource.FromFile(frame.RegisteredImagePath) : null,
                Aspect = Aspect.AspectFit,
                HeightRequest = 300
            };
            statusLabel = new Label
            {
                Text = $"Auto-detected corners • confidence {frame.Geometry.Confidence * 100.0:0}%",
                TextColor = Color.FromArgb("#E2E8F0"),
                FontSize = 12
            };

            var adjust = new Button
            {
                Text = "ADJUST 4 CORNERS",
                BackgroundColor = Color.FromArgb("#253047"),
                TextColor = Colors.White,
                CornerRadius = 16
            };
            adjust.Clicked += async (_, _) => await AdjustCornersAsync(adjust);

            var rotateLeft = MakeButton("↺ LEFT", "#253047");
            var rotate180 = MakeButton("↻ 180°", "#253047");
            var rotateRight = MakeButton("RIGHT ↻", "#253047");
            rotateLeft.Clicked += async (_, _) => await RotateAsync(-1);
            rotate180.Clicked += async (_, _) => await RotateAsync(2);
            rotateRight.Clicked += async (_, _) => await RotateAsync(1);

            var confirm = MakeButton("CONFIRM CARD LOCK → CENTERING", "#1D6A2A");
            confirm.HeightRequest = 54;
            confirm.FontAttributes = FontAttributes.Bold;
            confirm.Clicked += async (_, _) =>
            {
                completion.TrySetResult(ApplyQuarterTurns(frameCorners, orientationQuarterTurns));
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
                            Text = "REVIEW THE 4 CORNERS BEFORE CENTERING",
                            TextColor = Color.FromArgb("#86FF70"),
                            FontSize = 22,
                            FontAttributes = FontAttributes.Bold
                        },
                        new Label
                        {
                            Text = "This is the same card-lock review used by Surface, but with one image. Check the green outline and flat preview. If anything is off, adjust the corners before centering is measured.",
                            TextColor = Color.FromArgb("#9FB3C8"),
                            FontSize = 13
                        },
                        statusLabel,
                        new Label { Text = "DETECTED / CONFIRMED CORNERS", TextColor = Colors.White, FontAttributes = FontAttributes.Bold },
                        overlayImage,
                        adjust,
                        new Label { Text = "FLAT / NORMALIZED PREVIEW", TextColor = Colors.White, FontAttributes = FontAttributes.Bold },
                        flatImage,
                        new Label
                        {
                            Text = "If the flat card is sideways or upside-down, rotate it here before confirming.",
                            TextColor = Color.FromArgb("#9FB3C8"),
                            FontSize = 12
                        },
                        new HorizontalStackLayout { Spacing = 8, Children = { rotateLeft, rotate180, rotateRight } },
                        new HorizontalStackLayout { Spacing = 8, Children = { cancel, confirm } }
                    }
                }
            };
        }

        public Task<CardPoint[]?> WaitForCornersAsync() => completion.Task;

        protected override bool OnBackButtonPressed()
        {
            completion.TrySetResult(null);
            return base.OnBackButtonPressed();
        }

        private async Task AdjustCornersAsync(Button adjustButton)
        {
            var editor = new CardCornerEditorPage(
                frame.PreviewImagePath,
                "CENTERING — PRECISION CORNER REVIEW",
                "Select TL/TR/BL/BR, tap the large image for the general corner position, then use the magnified crosshair and 1-pixel arrows for exact placement. This is the same precision editor Surface uses.",
                "UPDATE CENTERING WITH THESE 4 CORNERS",
                frameCorners);

            await Navigation.PushModalAsync(editor);
            CardPoint[]? updated = await editor.WaitForCornersAsync();
            if (updated is null)
                return;

            adjustButton.IsEnabled = false;
            adjustButton.Text = "UPDATING PREVIEWS…";
            statusLabel.Text = "Applying your exact corners and rebuilding the overlay + flat preview…";
            statusLabel.TextColor = Color.FromArgb("#FACC15");

            try
            {
                frameCorners = updated;
                ReviewImages refreshed = await Task.Run(() => RebuildReviewImages(frame, frameCorners, orientationQuarterTurns));
                overlayImage.Source = ImageSource.FromFile(refreshed.OverlayPath);
                flatImage.Source = ImageSource.FromFile(refreshed.FlatPath);
                statusLabel.Text = "✓ CORNERS UPDATED — these exact corners will be used for Centering.";
                statusLabel.TextColor = Color.FromArgb("#86FF70");
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"Corner update failed: {ex.Message}";
                statusLabel.TextColor = Color.FromArgb("#FF8A80");
            }
            finally
            {
                adjustButton.IsEnabled = true;
                adjustButton.Text = "ADJUST 4 CORNERS";
            }
        }

        private async Task RotateAsync(int delta)
        {
            orientationQuarterTurns = ((orientationQuarterTurns + delta) % 4 + 4) % 4;
            ReviewImages refreshed = await Task.Run(() => RebuildReviewImages(frame, frameCorners, orientationQuarterTurns));
            flatImage.Source = ImageSource.FromFile(refreshed.FlatPath);
            statusLabel.Text = orientationQuarterTurns == 0
                ? "Orientation reset."
                : $"Orientation adjusted by {orientationQuarterTurns * 90}°.";
            statusLabel.TextColor = Color.FromArgb("#FACC15");
        }

        private static ReviewImages RebuildReviewImages(
            RegisteredCardFrame frame,
            IReadOnlyList<CardPoint> normalizedCorners,
            int quarterTurns)
        {
            using Mat source = Cv2.ImRead(frame.PreviewImagePath, ImreadModes.Color);
            if (source.Empty())
                throw new InvalidOperationException("CollectIQ could not reopen the bounded card preview.");

            CardPoint[] pixelCorners = normalizedCorners.Take(4).Select(p => new CardPoint(
                p.X * Math.Max(source.Width - 1, 1),
                p.Y * Math.Max(source.Height - 1, 1))).ToArray();

            string directory = Path.GetDirectoryName(frame.PreviewImagePath) ?? Path.GetTempPath();
            string token = Guid.NewGuid().ToString("N");
            string overlayPath = Path.Combine(directory, $"centering_review_confirmed_{token}.jpg");
            string flatPath = Path.Combine(directory, $"centering_review_flat_{token}.jpg");

            using (Mat overlay = source.Clone())
            {
                Scalar green = new(20, 255, 20);
                Scalar yellow = new(0, 235, 255);
                string[] labels = { "TL", "TR", "BR", "BL" };
                for (int i = 0; i < 4; i++)
                {
                    CardPoint a = pixelCorners[i];
                    CardPoint b = pixelCorners[(i + 1) % 4];
                    Cv2.Line(overlay,
                        new OpenCvSharp.Point((int)Math.Round(a.X), (int)Math.Round(a.Y)),
                        new OpenCvSharp.Point((int)Math.Round(b.X), (int)Math.Round(b.Y)),
                        green, 3, LineTypes.AntiAlias);
                }
                for (int i = 0; i < 4; i++)
                {
                    int x = (int)Math.Round(pixelCorners[i].X);
                    int y = (int)Math.Round(pixelCorners[i].Y);
                    Cv2.Circle(overlay, new OpenCvSharp.Point(x, y), 9, yellow, -1, LineTypes.AntiAlias);
                    Cv2.PutText(overlay, labels[i], new OpenCvSharp.Point(x + 8, y - 8),
                        HersheyFonts.HersheySimplex, 0.55, green, 2, LineTypes.AntiAlias);
                }
                Cv2.ImWrite(overlayPath, overlay, new[] { (int)ImwriteFlags.JpegQuality, 94 });
            }

            CardPoint[] oriented = ApplyQuarterTurns(normalizedCorners, quarterTurns);
            Point2f[] src = oriented.Select(p => new Point2f(
                p.X * Math.Max(source.Width - 1, 1),
                p.Y * Math.Max(source.Height - 1, 1))).ToArray();
            Point2f[] dst =
            {
                new(0, 0), new(CanonicalWidth - 1, 0),
                new(CanonicalWidth - 1, CanonicalHeight - 1), new(0, CanonicalHeight - 1)
            };
            using Mat transform = Cv2.GetPerspectiveTransform(src, dst);
            using Mat flat = new();
            Cv2.WarpPerspective(source, flat, transform, new OpenCvSharp.Size(CanonicalWidth, CanonicalHeight),
                InterpolationFlags.Linear, BorderTypes.Replicate);
            Cv2.ImWrite(flatPath, flat, new[] { (int)ImwriteFlags.JpegQuality, 94 });

            return new ReviewImages(overlayPath, flatPath);
        }

        private static CardPoint[] ApplyQuarterTurns(IReadOnlyList<CardPoint> corners, int turns)
        {
            int shift = ((turns % 4) + 4) % 4;
            return Enumerable.Range(0, 4)
                .Select(i => corners[(i - shift + 4) % 4])
                .Select(p => new CardPoint(p.X, p.Y))
                .ToArray();
        }

        private static IReadOnlyList<CardPoint> Normalize(RegisteredCardFrame frame)
        {
            float width = Math.Max(frame.Geometry.SourceWidth - 1, 1);
            float height = Math.Max(frame.Geometry.SourceHeight - 1, 1);
            return frame.Geometry.Corners
                .Select(p => new CardPoint(p.X / width, p.Y / height))
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
