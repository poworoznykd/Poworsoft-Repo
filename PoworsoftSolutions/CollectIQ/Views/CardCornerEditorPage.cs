using CollectIQ.Models.Inspection.Geometry;

namespace CollectIQ.Views
{
    /// <summary>
    /// Precision four-corner editor used by Surface. The existing detected corners can be
    /// supplied so the user only needs to correct a bad point instead of starting over.
    /// A full-card view is used for coarse placement and a magnified corner view plus
    /// one-pixel arrow nudges is used for final placement.
    /// </summary>
    public sealed class CardCornerEditorPage : ContentPage
    {
        // Internal order must stay TL, TR, BR, BL because the perspective warp expects it.
        private static readonly string[] CornerNames =
        {
            "TOP LEFT", "TOP RIGHT", "BOTTOM RIGHT", "BOTTOM LEFT"
        };

        // Button order is more natural visually: TL, TR, BL, BR.
        private static readonly int[] ButtonCornerOrder = { 0, 1, 3, 2 };

        private readonly TaskCompletionSource<CardPoint[]?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Point?[] corners = new Point?[4];
        private readonly Grid viewport;
        private readonly Grid transformLayer;
        private readonly GraphicsView overlay;
        private readonly Grid precisionViewport;
        private readonly Grid precisionLayer;
        private readonly Label precisionLabel;
        private readonly Label instruction;
        private readonly Button useButton;
        private readonly CornerDrawable drawable;
        private readonly double sourceWidth;
        private readonly double sourceHeight;
        private readonly List<Button> cornerButtons = new();
        private readonly string previewImagePath;

        private int activeCorner;
        private double scale = 1.0;
        private double scaleStart = 1.0;
        private double translateX;
        private double translateY;
        private double panStartX;
        private double panStartY;
        private bool panMoved;
        private DateTime ignoreTapUntil = DateTime.MinValue;
        private const double PrecisionScale = 5.5;

        public CardCornerEditorPage(
            string previewImagePath,
            string titleText = "REFERENCE IMAGE — MANUAL 4-CORNER LOCK",
            string subtitleText = "Set the four PHYSICAL card corners. Use the large image for coarse placement, then the magnified view and arrows for pixel-level accuracy.",
            string continueButtonText = "USE THESE 4 CORNERS → CONTINUE SURFACE",
            IReadOnlyList<CardPoint>? initialCorners = null)
        {
            this.previewImagePath = previewImagePath;
            Title = "Precision Card Corners";
            BackgroundColor = Color.FromArgb("#050814");

            var info = SixLabors.ImageSharp.Image.Identify(previewImagePath);
            sourceWidth = Math.Max(1, info?.Width ?? 1);
            sourceHeight = Math.Max(1, info?.Height ?? 1);

            if (initialCorners is not null && initialCorners.Count >= 4)
            {
                for (int i = 0; i < 4; i++)
                    corners[i] = new Point(
                        Math.Clamp(initialCorners[i].X, 0f, 1f),
                        Math.Clamp(initialCorners[i].Y, 0f, 1f));
            }

            instruction = new Label
            {
                Text = corners.All(p => p.HasValue)
                    ? "Detected corners are loaded. Select a corner, tap near it for a coarse move, then use the arrows for exact placement."
                    : "Select TOP LEFT and tap the physical card corner. Continue around the card.",
                TextColor = Color.FromArgb("#E2E8F0"),
                FontSize = 13
            };

            viewport = new Grid
            {
                HeightRequest = 500,
                BackgroundColor = Colors.Black,
                IsClippedToBounds = true
            };

            transformLayer = new Grid();
            var image = new Image
            {
                Source = ImageSource.FromFile(previewImagePath),
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                InputTransparent = true
            };
            drawable = new CornerDrawable(corners, GetAspectFitRect, () => activeCorner);
            overlay = new GraphicsView
            {
                Drawable = drawable,
                InputTransparent = true
            };
            transformLayer.Children.Add(image);
            transformLayer.Children.Add(overlay);
            viewport.Children.Add(transformLayer);

            var tap = new TapGestureRecognizer();
            tap.Tapped += OnTapped;
            viewport.GestureRecognizers.Add(tap);
            var pinch = new PinchGestureRecognizer();
            pinch.PinchUpdated += OnPinch;
            viewport.GestureRecognizers.Add(pinch);
            var pan = new PanGestureRecognizer();
            pan.PanUpdated += OnPan;
            viewport.GestureRecognizers.Add(pan);
            viewport.SizeChanged += (_, _) =>
            {
                overlay.Invalidate();
                UpdatePrecisionView();
            };

            var cornerButtons = new Grid { ColumnSpacing = 6 };
            for (int i = 0; i < 4; i++)
                cornerButtons.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            foreach ((int cornerIndex, int column) in ButtonCornerOrder.Select((cornerIndex, column) => (cornerIndex, column)))
            {
                int index = cornerIndex;
                var button = new Button
                {
                    Text = ShortCornerName(index),
                    FontSize = 11,
                    CornerRadius = 12,
                    BackgroundColor = Color.FromArgb("#253047"),
                    TextColor = Colors.White,
                    Padding = new Thickness(4)
                };
                button.Clicked += (_, _) => SelectCorner(index);
                cornerButtons.Add(button, column, 0);
                this.cornerButtons.Add(button);
            }

            precisionLabel = new Label
            {
                Text = "PRECISION VIEW — TOP LEFT",
                TextColor = Color.FromArgb("#86FF70"),
                FontAttributes = FontAttributes.Bold,
                FontSize = 15
            };

            precisionViewport = new Grid
            {
                HeightRequest = 230,
                BackgroundColor = Colors.Black,
                IsClippedToBounds = true
            };
            precisionLayer = new Grid();
            precisionLayer.Children.Add(new Image
            {
                Source = ImageSource.FromFile(previewImagePath),
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                InputTransparent = true
            });
            precisionViewport.Children.Add(precisionLayer);
            precisionViewport.Children.Add(new GraphicsView
            {
                Drawable = new PrecisionCrosshairDrawable(),
                InputTransparent = true
            });
            precisionViewport.SizeChanged += (_, _) => UpdatePrecisionView();

            var arrows = BuildArrowPad();

            useButton = new Button
            {
                Text = continueButtonText,
                IsEnabled = corners.All(x => x.HasValue),
                HeightRequest = 52,
                CornerRadius = 20,
                BackgroundColor = Color.FromArgb("#1D6A2A"),
                TextColor = Color.FromArgb("#F0FFED"),
                FontAttributes = FontAttributes.Bold
            };
            useButton.Clicked += OnUseClicked;

            var resetViewButton = new Button
            {
                Text = "RESET ZOOM",
                BackgroundColor = Color.FromArgb("#253047"),
                TextColor = Colors.White,
                CornerRadius = 16
            };
            resetViewButton.Clicked += (_, _) => ResetView();

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

            Content = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Padding = new Thickness(16, 18, 16, 30),
                    Spacing = 12,
                    Children =
                    {
                        new Label { Text = titleText, TextColor = Color.FromArgb("#86FF70"), FontSize = 22, FontAttributes = FontAttributes.Bold },
                        new Label { Text = subtitleText, TextColor = Color.FromArgb("#9FB3C8"), FontSize = 12 },
                        instruction,
                        viewport,
                        cornerButtons,
                        precisionLabel,
                        precisionViewport,
                        new Label { Text = "NUDGE SELECTED CORNER — 1 SOURCE PIXEL PER TAP", TextColor = Color.FromArgb("#9FB3C8"), FontSize = 11 },
                        arrows,
                        new HorizontalStackLayout { Spacing = 8, Children = { resetViewButton, cancelButton } },
                        useButton
                    }
                }
            };

            SelectCorner(0);
        }

        protected override bool OnBackButtonPressed()
        {
            completion.TrySetResult(null);
            return base.OnBackButtonPressed();
        }

        public Task<CardPoint[]?> WaitForCornersAsync() => completion.Task;

        private Grid BuildArrowPad()
        {
            var grid = new Grid
            {
                RowSpacing = 6,
                ColumnSpacing = 6,
                HorizontalOptions = LayoutOptions.Center
            };
            for (int i = 0; i < 3; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(74)));
            }

            grid.Add(CreateNudgeButton("↑", 0, -1), 1, 0);
            grid.Add(CreateNudgeButton("←", -1, 0), 0, 1);
            grid.Add(CreateNudgeButton("●", 0, 0), 1, 1);
            grid.Add(CreateNudgeButton("→", 1, 0), 2, 1);
            grid.Add(CreateNudgeButton("↓", 0, 1), 1, 2);
            return grid;
        }

        private Button CreateNudgeButton(string text, int dx, int dy)
        {
            var button = new Button
            {
                Text = text,
                FontSize = 22,
                HeightRequest = 48,
                CornerRadius = 12,
                BackgroundColor = dx == 0 && dy == 0 ? Color.FromArgb("#162033") : Color.FromArgb("#253047"),
                TextColor = dx == 0 && dy == 0 ? Color.FromArgb("#86FF70") : Colors.White,
                IsEnabled = !(dx == 0 && dy == 0)
            };
            if (dx != 0 || dy != 0)
                button.Clicked += (_, _) => NudgeActiveCorner(dx, dy);
            return button;
        }

        private void SelectCorner(int index)
        {
            activeCorner = Math.Clamp(index, 0, 3);
            instruction.Text = corners[activeCorner].HasValue
                ? $"{CornerNames[activeCorner]} selected. Tap the large image to move it, or use the arrows below for pixel-level adjustment."
                : $"{CornerNames[activeCorner]} selected. Tap its physical card corner on the large image.";
            precisionLabel.Text = $"PRECISION VIEW — {CornerNames[activeCorner]}";

            foreach (Button button in cornerButtons)
                button.BackgroundColor = Color.FromArgb("#253047");

            int buttonPosition = Array.IndexOf(ButtonCornerOrder, activeCorner);
            if (buttonPosition >= 0 && buttonPosition < cornerButtons.Count)
                cornerButtons[buttonPosition].BackgroundColor = Color.FromArgb("#1D6A2A");

            overlay.Invalidate();
            UpdatePrecisionView();
        }

        private void OnTapped(object? sender, TappedEventArgs e)
        {
            if (DateTime.UtcNow < ignoreTapUntil || viewport.Width <= 1 || viewport.Height <= 1)
                return;

            Point? p = e.GetPosition(viewport);
            if (p is null) return;

            double cx = viewport.Width / 2.0;
            double cy = viewport.Height / 2.0;
            double imageX = ((p.Value.X - cx - translateX) / scale) + cx;
            double imageY = ((p.Value.Y - cy - translateY) / scale) + cy;
            Rect fit = GetAspectFitRect();

            if (imageX < fit.X || imageX > fit.Right || imageY < fit.Y || imageY > fit.Bottom)
            {
                instruction.Text = "That tap is outside the displayed photo. Pan/zoom and tap the physical card corner.";
                return;
            }

            double nx = Math.Clamp((imageX - fit.X) / fit.Width, 0.0, 1.0);
            double ny = Math.Clamp((imageY - fit.Y) / fit.Height, 0.0, 1.0);
            corners[activeCorner] = new Point(nx, ny);
            OnCornerChanged();

            if (corners.All(x => x.HasValue))
                instruction.Text = $"{CornerNames[activeCorner]} moved. Use the precision arrows if needed, then update the image.";
            else
                SelectNextUnsetCorner();
        }

        private void NudgeActiveCorner(int dxPixels, int dyPixels)
        {
            if (!corners[activeCorner].HasValue)
            {
                instruction.Text = $"Place {CornerNames[activeCorner]} once on the large image before nudging it.";
                return;
            }

            Point current = corners[activeCorner]!.Value;
            double nx = Math.Clamp(current.X + (dxPixels / Math.Max(1.0, sourceWidth - 1.0)), 0.0, 1.0);
            double ny = Math.Clamp(current.Y + (dyPixels / Math.Max(1.0, sourceHeight - 1.0)), 0.0, 1.0);
            corners[activeCorner] = new Point(nx, ny);
            OnCornerChanged();
            instruction.Text = $"{CornerNames[activeCorner]} nudged {Math.Abs(dxPixels) + Math.Abs(dyPixels)} px. Crosshair stays fixed while the image moves beneath it.";
        }

        private void OnCornerChanged()
        {
            overlay.Invalidate();
            useButton.IsEnabled = corners.All(x => x.HasValue);
            UpdatePrecisionView();
        }

        private void SelectNextUnsetCorner()
        {
            for (int offset = 1; offset <= 4; offset++)
            {
                int candidate = (activeCorner + offset) % 4;
                if (!corners[candidate].HasValue)
                {
                    SelectCorner(candidate);
                    return;
                }
            }
        }

        private void UpdatePrecisionView()
        {
            if (precisionViewport.Width <= 1 || precisionViewport.Height <= 1 || !corners[activeCorner].HasValue)
                return;

            Point point = corners[activeCorner]!.Value;
            Rect fit = GetAspectFitRect(precisionViewport.Width, precisionViewport.Height);
            double px = fit.X + point.X * fit.Width;
            double py = fit.Y + point.Y * fit.Height;
            double cx = precisionViewport.Width / 2.0;
            double cy = precisionViewport.Height / 2.0;

            precisionLayer.AnchorX = 0.5;
            precisionLayer.AnchorY = 0.5;
            precisionLayer.Scale = PrecisionScale;
            precisionLayer.TranslationX = -(px - cx) * PrecisionScale;
            precisionLayer.TranslationY = -(py - cy) * PrecisionScale;
        }

        private void OnPinch(object? sender, PinchGestureUpdatedEventArgs e)
        {
            if (e.Status == GestureStatus.Started)
            {
                scaleStart = scale;
                return;
            }
            if (e.Status == GestureStatus.Running)
            {
                scale = Math.Clamp(scaleStart * e.Scale, 1.0, 8.0);
                ClampTranslation();
                ApplyTransform();
                return;
            }
            ignoreTapUntil = DateTime.UtcNow.AddMilliseconds(180);
        }

        private void OnPan(object? sender, PanUpdatedEventArgs e)
        {
            if (e.StatusType == GestureStatus.Started)
            {
                panStartX = translateX;
                panStartY = translateY;
                panMoved = false;
                return;
            }
            if (e.StatusType == GestureStatus.Running)
            {
                if (Math.Abs(e.TotalX) > 3 || Math.Abs(e.TotalY) > 3) panMoved = true;
                translateX = panStartX + e.TotalX;
                translateY = panStartY + e.TotalY;
                ClampTranslation();
                ApplyTransform();
                return;
            }
            if (panMoved) ignoreTapUntil = DateTime.UtcNow.AddMilliseconds(180);
        }

        private void ClampTranslation()
        {
            double maxX = Math.Max(0, (viewport.Width * scale - viewport.Width) / 2.0);
            double maxY = Math.Max(0, (viewport.Height * scale - viewport.Height) / 2.0);
            translateX = Math.Clamp(translateX, -maxX, maxX);
            translateY = Math.Clamp(translateY, -maxY, maxY);
        }

        private void ApplyTransform()
        {
            transformLayer.Scale = scale;
            transformLayer.TranslationX = translateX;
            transformLayer.TranslationY = translateY;
        }

        private void ResetView()
        {
            scale = 1.0;
            scaleStart = 1.0;
            translateX = translateY = panStartX = panStartY = 0;
            ApplyTransform();
        }

        private Rect GetAspectFitRect() => GetAspectFitRect(viewport.Width, viewport.Height);

        private Rect GetAspectFitRect(double viewportWidth, double viewportHeight)
        {
            double width = Math.Max(1, viewportWidth);
            double height = Math.Max(1, viewportHeight);
            double fitScale = Math.Min(width / sourceWidth, height / sourceHeight);
            double imageWidth = sourceWidth * fitScale;
            double imageHeight = sourceHeight * fitScale;
            return new Rect((width - imageWidth) / 2.0, (height - imageHeight) / 2.0, imageWidth, imageHeight);
        }

        private async void OnUseClicked(object? sender, EventArgs e)
        {
            if (corners.Any(x => !x.HasValue)) return;
            CardPoint[] result = corners.Select(x => new CardPoint((float)x!.Value.X, (float)x.Value.Y)).ToArray();
            completion.TrySetResult(result);
            await Navigation.PopModalAsync();
        }

        private static string ShortCornerName(int index) => index switch
        {
            0 => "TL",
            1 => "TR",
            2 => "BR",
            3 => "BL",
            _ => "?"
        };

        private sealed class CornerDrawable : IDrawable
        {
            private readonly Point?[] corners;
            private readonly Func<Rect> getImageRect;
            private readonly Func<int> getActiveCorner;

            public CornerDrawable(Point?[] corners, Func<Rect> getImageRect, Func<int> getActiveCorner)
            {
                this.corners = corners;
                this.getImageRect = getImageRect;
                this.getActiveCorner = getActiveCorner;
            }

            public void Draw(ICanvas canvas, RectF dirtyRect)
            {
                Rect fit = getImageRect();
                PointF?[] mapped = corners.Select(p => p.HasValue
                    ? new PointF((float)(fit.X + p.Value.X * fit.Width), (float)(fit.Y + p.Value.Y * fit.Height))
                    : (PointF?)null).ToArray();

                canvas.StrokeColor = Color.FromArgb("#39FF14");
                canvas.StrokeSize = 3;
                for (int i = 0; i < 4; i++)
                {
                    if (mapped[i].HasValue && mapped[(i + 1) % 4].HasValue)
                    {
                        PointF a = mapped[i]!.Value;
                        PointF b = mapped[(i + 1) % 4]!.Value;
                        canvas.DrawLine(a.X, a.Y, b.X, b.Y);
                    }
                }

                int active = getActiveCorner();
                for (int i = 0; i < mapped.Length; i++)
                {
                    if (!mapped[i].HasValue) continue;
                    PointF p = mapped[i]!.Value;
                    canvas.FillColor = i == active ? Color.FromArgb("#FFEA00") : Color.FromArgb("#39FF14");
                    canvas.FillCircle(p.X, p.Y, i == active ? 12 : 9);
                    canvas.StrokeColor = Colors.Black;
                    canvas.StrokeSize = 2;
                    canvas.DrawCircle(p.X, p.Y, i == active ? 12 : 9);
                }
            }
        }

        private sealed class PrecisionCrosshairDrawable : IDrawable
        {
            public void Draw(ICanvas canvas, RectF dirtyRect)
            {
                float cx = dirtyRect.X + (dirtyRect.Width / 2f);
                float cy = dirtyRect.Y + (dirtyRect.Height / 2f);
                canvas.StrokeColor = Color.FromArgb("#FFEA00");
                canvas.StrokeSize = 2;
                canvas.DrawLine(cx - 28, cy, cx + 28, cy);
                canvas.DrawLine(cx, cy - 28, cx, cy + 28);
                canvas.DrawCircle(cx, cy, 9);
                canvas.FillColor = Color.FromArgb("#FFEA00");
                canvas.FillCircle(cx, cy, 2.5f);
            }
        }
    }
}
