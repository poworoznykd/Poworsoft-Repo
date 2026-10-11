using CollectIQ.Models.Inspection.Geometry;

namespace CollectIQ.Views
{
    /// <summary>
    /// Manual recovery used only when Surface cannot lock the neutral reference.
    /// The preview is the bounded, already-auto-oriented image created by the
    /// shared card normalizer, so the returned 0..1 coordinates map directly to
    /// the reference geometry used by Surface.
    /// </summary>
    public sealed class SurfaceReferenceCornerPickerPage : ContentPage
    {
        private static readonly string[] CornerNames =
        {
            "TOP LEFT", "TOP RIGHT", "BOTTOM RIGHT", "BOTTOM LEFT"
        };

        private readonly TaskCompletionSource<CardPoint[]?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Point?[] corners = new Point?[4];
        private readonly Grid viewport;
        private readonly Grid transformLayer;
        private readonly GraphicsView overlay;
        private readonly Label instruction;
        private readonly Button useButton;
        private readonly CornerDrawable drawable;
        private readonly double sourceWidth;
        private readonly double sourceHeight;
        private int activeCorner;
        private double scale = 1.0;
        private double scaleStart = 1.0;
        private double translateX;
        private double translateY;
        private double panStartX;
        private double panStartY;
        private bool panMoved;
        private DateTime ignoreTapUntil = DateTime.MinValue;

        public SurfaceReferenceCornerPickerPage(string previewImagePath)
        {
            Title = "Surface Reference Corners";
            BackgroundColor = Color.FromArgb("#050814");

            var info = SixLabors.ImageSharp.Image.Identify(previewImagePath);
            sourceWidth = Math.Max(1, info?.Width ?? 1);
            sourceHeight = Math.Max(1, info?.Height ?? 1);

            instruction = new Label
            {
                Text = "Pinch to zoom, drag to pan, then tap TOP LEFT on the physical card.",
                TextColor = Color.FromArgb("#E2E8F0"),
                FontSize = 13
            };

            viewport = new Grid
            {
                HeightRequest = 570,
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
            drawable = new CornerDrawable(corners, GetAspectFitRect);
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
            viewport.SizeChanged += (_, _) => overlay.Invalidate();

            var buttons = new Grid { ColumnSpacing = 6 };
            for (int i = 0; i < 4; i++)
                buttons.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var button = new Button
                {
                    Text = CornerNames[i].Replace(" ", "\n"),
                    FontSize = 9,
                    CornerRadius = 12,
                    BackgroundColor = Color.FromArgb("#253047"),
                    TextColor = Colors.White
                };
                button.Clicked += (_, _) =>
                {
                    activeCorner = index;
                    instruction.Text = $"Tap {CornerNames[index]} on the physical card.";
                };
                Grid.SetColumn(button, i);
                buttons.Children.Add(button);
            }

            useButton = new Button
            {
                Text = "USE THESE 4 CORNERS → CONTINUE SURFACE",
                IsEnabled = false,
                HeightRequest = 52,
                CornerRadius = 20,
                BackgroundColor = Color.FromArgb("#1D6A2A"),
                TextColor = Color.FromArgb("#F0FFED"),
                FontAttributes = FontAttributes.Bold
            };
            useButton.Clicked += OnUseClicked;

            var resetButton = new Button
            {
                Text = "RESET VIEW",
                BackgroundColor = Color.FromArgb("#253047"),
                TextColor = Colors.White,
                CornerRadius = 16
            };
            resetButton.Clicked += (_, _) => ResetView();

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
                    Padding = new Thickness(16, 18, 16, 24),
                    Spacing = 12,
                    Children =
                    {
                        new Label { Text = "REFERENCE IMAGE — MANUAL 4-CORNER LOCK", TextColor = Color.FromArgb("#86FF70"), FontSize = 22, FontAttributes = FontAttributes.Bold },
                        new Label { Text = "Only the NEUTRAL REFERENCE failed automatic Card Lock. Set its four PHYSICAL card corners once. TOP, RIGHT, BOTTOM and LEFT will inherit this reference geometry automatically.", TextColor = Color.FromArgb("#9FB3C8"), FontSize = 12 },
                        instruction,
                        viewport,
                        buttons,
                        new HorizontalStackLayout { Spacing = 8, Children = { resetButton, cancelButton } },
                        useButton
                    }
                }
            };
        }


        protected override bool OnBackButtonPressed()
        {
            completion.TrySetResult(null);
            return base.OnBackButtonPressed();
        }

        public Task<CardPoint[]?> WaitForCornersAsync() => completion.Task;

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
            overlay.Invalidate();

            bool complete = corners.All(x => x.HasValue);
            useButton.IsEnabled = complete;
            if (!complete)
            {
                for (int offset = 1; offset <= 4; offset++)
                {
                    int candidate = (activeCorner + offset) % 4;
                    if (!corners[candidate].HasValue)
                    {
                        activeCorner = candidate;
                        break;
                    }
                }
                instruction.Text = $"Corner saved. Tap {CornerNames[activeCorner]} next.";
            }
            else
            {
                instruction.Text = "All four corners are set. Check the green quadrilateral, then continue Surface.";
            }
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

        private Rect GetAspectFitRect()
        {
            double width = Math.Max(1, viewport.Width);
            double height = Math.Max(1, viewport.Height);
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

        private sealed class CornerDrawable : IDrawable
        {
            private readonly Point?[] corners;
            private readonly Func<Rect> getImageRect;

            public CornerDrawable(Point?[] corners, Func<Rect> getImageRect)
            {
                this.corners = corners;
                this.getImageRect = getImageRect;
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

                canvas.FillColor = Color.FromArgb("#FFD84D");
                foreach (PointF? p in mapped)
                {
                    if (!p.HasValue) continue;
                    canvas.FillCircle(p.Value.X, p.Value.Y, 9);
                }
            }
        }
    }
}
