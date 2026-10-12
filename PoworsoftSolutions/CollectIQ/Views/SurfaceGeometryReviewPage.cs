using CollectIQ.Models.Inspection.Geometry;
using CollectIQ.Models.Inspection.Registration;
using OpenCvSharp;

namespace CollectIQ.Views
{
    public sealed class SurfaceGeometryReviewPage : ContentPage
    {
        private const int CanonicalWidth = 750;
        private const int CanonicalHeight = 1050;

        private readonly TaskCompletionSource<Dictionary<string, IReadOnlyList<CardPoint>>?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<string, IReadOnlyList<CardPoint>> frameCorners = new(StringComparer.OrdinalIgnoreCase);
        private readonly CardRegistrationResult registration;

        public SurfaceGeometryReviewPage(CardRegistrationResult registration)
        {
            this.registration = registration;
            Title = "Surface Geometry Review";
            BackgroundColor = Color.FromArgb("#050814");

            foreach (RegisteredCardFrame frame in registration.Frames.Values)
                frameCorners[frame.Key] = Normalize(frame);

            var content = new VerticalStackLayout
            {
                Padding = new Thickness(16, 18, 16, 24),
                Spacing = 14
            };

            content.Children.Add(new Label
            {
                Text = "REVIEW THE 4 CORNERS BEFORE PHOTOMETRIC INSPECTION",
                TextColor = Color.FromArgb("#86FF70"),
                FontSize = 22,
                FontAttributes = FontAttributes.Bold
            });
            content.Children.Add(new Label
            {
                Text = "CollectIQ found and flattened all five card images. Check the green outline and the flat preview. If anything is off, adjust that image. Your corrected overlay and flat preview will refresh immediately before you continue.",
                TextColor = Color.FromArgb("#9FB3C8"),
                FontSize = 13
            });

            foreach (string key in new[] { "Reference", "Top", "Right", "Bottom", "Left" })
            {
                if (registration.Frames.TryGetValue(key, out RegisteredCardFrame? frame))
                    content.Children.Add(BuildFrameCard(frame));
            }

            var continueButton = new Button
            {
                Text = "CONTINUE TO PHOTOMETRIC INSPECTION",
                BackgroundColor = Color.FromArgb("#1D6A2A"),
                TextColor = Color.FromArgb("#F0FFED"),
                FontAttributes = FontAttributes.Bold,
                CornerRadius = 20,
                HeightRequest = 54
            };
            continueButton.Clicked += async (_, _) =>
            {
                completion.TrySetResult(new Dictionary<string, IReadOnlyList<CardPoint>>(frameCorners, StringComparer.OrdinalIgnoreCase));
                await Navigation.PopModalAsync();
            };

            var cancelButton = new Button
            {
                Text = "CANCEL",
                BackgroundColor = Color.FromArgb("#3B1720"),
                TextColor = Colors.White,
                CornerRadius = 16
            };
            cancelButton.Clicked += async (_, _) =>
            {
                completion.TrySetResult(null);
                await Navigation.PopModalAsync();
            };

            content.Children.Add(new HorizontalStackLayout { Spacing = 10, Children = { cancelButton, continueButton } });
            Content = new ScrollView { Content = content };
        }

        public Task<Dictionary<string, IReadOnlyList<CardPoint>>?> WaitForResultAsync() => completion.Task;

        protected override bool OnBackButtonPressed()
        {
            completion.TrySetResult(null);
            return base.OnBackButtonPressed();
        }

        private View BuildFrameCard(RegisteredCardFrame frame)
        {
            Label status = new Label
            {
                Text = $"Auto-detected corners • confidence {frame.Geometry.Confidence * 100.0:0}%",
                TextColor = Color.FromArgb("#E2E8F0"),
                FontSize = 12
            };

            var overlayImage = new Image
            {
                Source = File.Exists(frame.DetectionOverlayPath) ? ImageSource.FromFile(frame.DetectionOverlayPath) : null,
                Aspect = Aspect.AspectFit,
                HeightRequest = 180
            };
            var flatImage = new Image
            {
                Source = File.Exists(frame.RegisteredImagePath) ? ImageSource.FromFile(frame.RegisteredImagePath) : null,
                Aspect = Aspect.AspectFit,
                HeightRequest = 180
            };

            var adjustButton = new Button
            {
                Text = $"ADJUST {frame.Key.ToUpperInvariant()} CORNERS",
                BackgroundColor = Color.FromArgb("#253047"),
                TextColor = Colors.White,
                CornerRadius = 16
            };
            adjustButton.Clicked += async (_, _) =>
            {
                string title = $"{frame.Key.ToUpperInvariant()} IMAGE — PRECISION CORNER REVIEW";
                string subtitle = "The current corners are already loaded. Select TL/TR/BL/BR, tap the large image for a coarse move, then use the magnified crosshair and 1-pixel arrows for exact placement.";

                var picker = new CardCornerEditorPage(
                    frame.PreviewImagePath,
                    title,
                    subtitle,
                    $"UPDATE {frame.Key.ToUpperInvariant()} WITH THESE 4 CORNERS",
                    frameCorners[frame.Key]);

                await Navigation.PushModalAsync(picker);
                CardPoint[]? result = await picker.WaitForCornersAsync();
                if (result is null)
                    return;

                adjustButton.IsEnabled = false;
                adjustButton.Text = "UPDATING PREVIEWS…";
                status.Text = "Applying your exact corners and rebuilding the overlay + flat preview…";
                status.TextColor = Color.FromArgb("#FACC15");

                try
                {
                    frameCorners[frame.Key] = result;
                    ReviewImages refreshed = await Task.Run(() => RebuildReviewImages(frame, result));

                    frame.DetectionOverlayPath = refreshed.OverlayPath;
                    frame.RegisteredImagePath = refreshed.FlatPath;
                    frame.Geometry.Corners = refreshed.PixelCorners;
                    frame.Geometry.Confidence = 1.0;

                    // Unique filenames avoid Android image caching, so the user sees the
                    // corrected geometry immediately after returning from the editor.
                    overlayImage.Source = ImageSource.FromFile(refreshed.OverlayPath);
                    flatImage.Source = ImageSource.FromFile(refreshed.FlatPath);

                    status.Text = "✓ CORNERS UPDATED — these exact corners are now locked for the photometric run.";
                    status.TextColor = Color.FromArgb("#86FF70");
                }
                catch (Exception ex)
                {
                    status.Text = $"Corner update failed: {ex.Message}";
                    status.TextColor = Color.FromArgb("#FF8A80");
                }
                finally
                {
                    adjustButton.IsEnabled = true;
                    adjustButton.Text = $"ADJUST {frame.Key.ToUpperInvariant()} CORNERS";
                }
            };

            return new Frame
            {
                BackgroundColor = Color.FromArgb("#0F172A"),
                BorderColor = Color.FromArgb("#253047"),
                CornerRadius = 18,
                Padding = new Thickness(12),
                Content = new VerticalStackLayout
                {
                    Spacing = 10,
                    Children =
                    {
                        new Label
                        {
                            Text = frame.Key.Equals("Reference", StringComparison.OrdinalIgnoreCase)
                                ? "REFERENCE"
                                : frame.Key.ToUpperInvariant(),
                            TextColor = Colors.White,
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold
                        },
                        status,
                        new Label { Text = "Detected / confirmed corners", TextColor = Color.FromArgb("#9FB3C8"), FontSize = 12 },
                        overlayImage,
                        new Label { Text = "Flat / normalized preview", TextColor = Color.FromArgb("#9FB3C8"), FontSize = 12 },
                        flatImage,
                        adjustButton
                    }
                }
            };
        }

        private static ReviewImages RebuildReviewImages(RegisteredCardFrame frame, IReadOnlyList<CardPoint> normalizedCorners)
        {
            if (string.IsNullOrWhiteSpace(frame.PreviewImagePath) || !File.Exists(frame.PreviewImagePath))
                throw new FileNotFoundException("The bounded review image is no longer available.", frame.PreviewImagePath);
            if (normalizedCorners.Count < 4)
                throw new InvalidOperationException("Four corners are required.");

            using Mat source = Cv2.ImRead(frame.PreviewImagePath, ImreadModes.Color);
            if (source.Empty())
                throw new InvalidOperationException("CollectIQ could not reopen the review image.");

            CardPoint[] pixelCorners = normalizedCorners
                .Take(4)
                .Select(p => new CardPoint(
                    p.X * Math.Max(source.Width - 1, 1),
                    p.Y * Math.Max(source.Height - 1, 1)))
                .ToArray();

            string directory = Path.GetDirectoryName(frame.PreviewImagePath) ?? Path.GetTempPath();
            string token = Guid.NewGuid().ToString("N");
            string safeKey = new string(frame.Key.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            string overlayPath = Path.Combine(directory, $"review_confirmed_{safeKey}_{token}.jpg");
            string flatPath = Path.Combine(directory, $"review_flat_{safeKey}_{token}.jpg");

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
                Cv2.PutText(overlay, $"{frame.Key.ToUpperInvariant()}: CONFIRMED CORNERS",
                    new OpenCvSharp.Point(12, 28), HersheyFonts.HersheySimplex, 0.65, green, 2, LineTypes.AntiAlias);
                Cv2.ImWrite(overlayPath, overlay, new[] { (int)ImwriteFlags.JpegQuality, 94 });
            }

            Point2f[] src = pixelCorners
                .Select(p => new Point2f((float)p.X, (float)p.Y))
                .ToArray();
            Point2f[] dst =
            {
                new(0, 0),
                new(CanonicalWidth - 1, 0),
                new(CanonicalWidth - 1, CanonicalHeight - 1),
                new(0, CanonicalHeight - 1)
            };
            using Mat transform = Cv2.GetPerspectiveTransform(src, dst);
            using Mat flat = new();
            Cv2.WarpPerspective(source, flat, transform, new OpenCvSharp.Size(CanonicalWidth, CanonicalHeight),
                InterpolationFlags.Linear, BorderTypes.Replicate);
            Cv2.ImWrite(flatPath, flat, new[] { (int)ImwriteFlags.JpegQuality, 94 });

            return new ReviewImages(overlayPath, flatPath, pixelCorners);
        }

        private static IReadOnlyList<CardPoint> Normalize(RegisteredCardFrame frame)
        {
            float width = Math.Max(frame.Geometry.SourceWidth - 1, 1);
            float height = Math.Max(frame.Geometry.SourceHeight - 1, 1);
            return frame.Geometry.Corners
                .Select(p => new CardPoint(p.X / width, p.Y / height))
                .ToArray();
        }

        private sealed record ReviewImages(string OverlayPath, string FlatPath, CardPoint[] PixelCorners);
    }
}
