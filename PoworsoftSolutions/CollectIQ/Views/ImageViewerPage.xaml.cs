//
// FILE: ImageViewerPage.xaml.cs
// PROJECT: CollectIQ (Mobile Application)
// PROGRAMMER: Darryl Poworoznyk
// FIRST VERSION: 2025-11-06
// DESCRIPTION:
//     Implements stabilized overlay drawing with neon glow animations,
//     color palette selection, and persistent save/load functionality
//     for inspection overlays. This version follows SET Coding Standards.
//

using CollectIQ.Models;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace CollectIQ.Views
{
    /// <summary>
    /// CLASS: ImageViewerPage
    /// PURPOSE:
    ///     Displays a zoomable image and provides a toggleable overlay
    ///     for freehand drawing, using neon-themed CollectIQ visual design.
    /// </summary>
    public partial class ImageViewerPage : ContentPage, IDrawable
    {
        private SKBitmap? overlayBitmap; // Cached bitmap of saved overlay

        // === CONSTANTS ===
        private const float STROKE_WIDTH = 4.0f;
        private const float HALO_BASE_OPACITY = 0.6f;
        private const float HALO_PULSE_HIGH = 1.0f;
        private const float WATERMARK_VISIBLE_OPACITY = 0.55f;
        private const uint FADE_DURATION_MS = 250;
        private const uint HALO_PULSE_TIME_MS = 800;

        // === READONLY COLORS ===
        private readonly Color COLOR_NEON_CYAN = new(0.0f, 1.0f, 1.0f, 1.0f);
        private readonly Color COLOR_DEFAULT_RED = new(1.0f, 0.0f, 0.0f, 0.6f);

        // === INSTANCE FIELDS ===
        private bool overlayVisible = false;
        private bool isDrawing = false;
        private bool pulseActive = false;
        private List<PointF> strokePoints = new();
        private readonly List<(List<PointF> Points, Color StrokeColor)> completedStrokes = new();
        private string overlayFilePath = string.Empty;
        private Color currentColor;

        // Card integration (optional)
        private readonly Card? owningCard;
        private readonly bool isFrontImage;

        // === CONSTRUCTORS ===

        /// <summary>
        /// FUNCTION: ImageViewerPage
        /// DESCRIPTION:
        ///     Default constructor used by existing callers. Uses only the image
        ///     path and stores overlays next to that image file. Front/back
        ///     tracking is not enabled without a Card instance.
        /// PARAMETERS:
        ///     imagePath – Path of the image to display.
        /// RETURNS:
        ///     None.
        /// </summary>
        public ImageViewerPage(string imagePath)
            : this(imagePath, null, true)
        {
        }

        /// <summary>
        /// FUNCTION: ImageViewerPage
        /// DESCRIPTION:
        ///     Full constructor that can optionally associate this viewer with
        ///     a Card and a specific side (front/back). When a Card is provided,
        ///     the overlay path is loaded from and saved back to the card’s
        ///     FrontOverlayImagePath or BackOverlayImagePath fields.
        /// PARAMETERS:
        ///     imagePath  – Path of the image to display.
        ///     card       – Card that owns this image (can be null).
        ///     isFront    – True for front image / overlay, false for back.
        /// RETURNS:
        ///     None.
        /// </summary>
        public ImageViewerPage(string imagePath, Card? card, bool isFront)
        {
            InitializeComponent();

            owningCard = card;
            isFrontImage = isFront;

            ZoomImage.Source = ImageSource.FromFile(imagePath);
            ZoomImage.AnchorX = 0.5;
            ZoomImage.AnchorY = 0.5;

            // Determine overlay path:
            // 1) If we have a Card and it already has an overlay path, use that.
            // 2) Otherwise, fall back to the legacy "<image>_overlay.png" path.
            if (owningCard != null)
            {
                string cardOverlayPath = isFrontImage
                    ? owningCard.FrontOverlayImagePath
                    : owningCard.BackOverlayImagePath;

                if (!string.IsNullOrWhiteSpace(cardOverlayPath))
                {
                    overlayFilePath = cardOverlayPath;
                }
                else
                {
                    overlayFilePath = Path.Combine(
                        Path.GetDirectoryName(imagePath)!,
                        $"{Path.GetFileNameWithoutExtension(imagePath)}_overlay.png");
                }
            }
            else
            {
                overlayFilePath = Path.Combine(
                    Path.GetDirectoryName(imagePath)!,
                    $"{Path.GetFileNameWithoutExtension(imagePath)}_overlay.png");
            }

            currentColor = COLOR_DEFAULT_RED;
            OverlayCanvas.Drawable = this;

            // Ensure halo is visible even before activation
            OverlayHalo.Opacity = HALO_BASE_OPACITY;
            LoadOverlayIfExists();
        }

        // === EVENT HANDLERS ===

        /// <summary>
        /// FUNCTION: OnOverlayToggleTapped
        /// DESCRIPTION:
        ///     Toggles overlay visibility and updates visual state.
        ///     When active: fills toggle green, brightens icon, starts halo pulse.
        ///     When inactive: clears fill, stops pulse but halo remains faint.
        /// </summary>
        private async void OnOverlayToggleTapped(object sender, EventArgs e)
        {
            overlayVisible = !overlayVisible;
            OverlayCanvas.IsVisible = overlayVisible;

            if (overlayVisible)
            {
                // Overlay ON: fill neon green, brighten icon, start halo pulse
                OverlayToggleFrame.BackgroundColor = Color.FromArgb("#39FF14");
                OverlayToggleIcon.Opacity = 1.0f;

                await OverlayWatermark.FadeTo(WATERMARK_VISIBLE_OPACITY, FADE_DURATION_MS, Easing.CubicIn);

                pulseActive = true;
                _ = PulseHalo();
            }
            else
            {
                // Overlay OFF: transparent fill, dim icon, stop halo pulse
                OverlayToggleFrame.BackgroundColor = Colors.Transparent;
                OverlayToggleIcon.Opacity = 0.8f;

                await OverlayWatermark.FadeTo(0.0f, FADE_DURATION_MS, Easing.CubicOut);

                pulseActive = false;
                OverlayHalo.Opacity = HALO_BASE_OPACITY; // Always visible faintly
            }
        }

        /// <summary>
        /// FUNCTION: PulseHalo
        /// DESCRIPTION:
        ///     Performs continuous opacity animation to create a breathing glow
        ///     while overlay mode is active. The halo remains faint when inactive.
        /// RETURNS:
        ///     Task (asynchronous loop).
        /// </summary>
        private async Task PulseHalo()
        {
            while (pulseActive)
            {
                await OverlayHalo.FadeTo(HALO_PULSE_HIGH, HALO_PULSE_TIME_MS, Easing.CubicInOut);
                await OverlayHalo.FadeTo(HALO_BASE_OPACITY, HALO_PULSE_TIME_MS, Easing.CubicInOut);
            }
        }

        /// <summary>
        /// FUNCTION: OnStartInteraction
        /// DESCRIPTION:
        ///     Begins a new freehand stroke when the user starts touching the canvas.
        /// PARAMETERS:
        ///     sender – GraphicsView object.
        ///     e – Touch event data.
        /// RETURNS:
        ///     None.
        /// </summary>
        private void OnStartInteraction(object? sender, TouchEventArgs e)
        {
            if (!overlayVisible)
                return;

            isDrawing = true;
            strokePoints = new List<PointF> { e.Touches[0] };
        }

        /// <summary>
        /// FUNCTION: OnDragInteraction
        /// DESCRIPTION:
        ///     Adds points to the active stroke during dragging.
        /// </summary>
        private void OnDragInteraction(object? sender, TouchEventArgs e)
        {
            if (!overlayVisible || !isDrawing)
                return;

            strokePoints.Add(e.Touches[0]);
            OverlayCanvas.Invalidate();
        }

        /// <summary>
        /// FUNCTION: OnEndInteraction
        /// DESCRIPTION:
        ///     Ends the stroke and stores it for rendering.
        /// </summary>
        private void OnEndInteraction(object? sender, TouchEventArgs e)
        {
            if (!overlayVisible)
                return;

            if (isDrawing && strokePoints.Count > 1)
                completedStrokes.Add((new List<PointF>(strokePoints), currentColor));

            strokePoints.Clear();
            isDrawing = false;
            OverlayCanvas.Invalidate();
        }

        /// <summary>
        /// FUNCTION: OnColorSelected
        /// DESCRIPTION:
        ///     Updates brush color and highlights the selected color circle.
        /// </summary>
        private void OnColorSelected(object sender, TappedEventArgs e)
        {
            if (sender is not Frame frame)
                return;

            currentColor = Color.FromArgb((string)e.Parameter);
            HighlightActiveColor(frame);
        }

        /// <summary>
        /// FUNCTION: HighlightActiveColor
        /// DESCRIPTION:
        ///     Adds neon cyan border to the selected color circle.
        /// </summary>
        private void HighlightActiveColor(Frame activeFrame)
        {
            foreach (var child in ColorPalette.Children)
            {
                if (child is Frame f)
                {
                    f.BorderColor = Colors.Transparent;
                    f.Shadow = null;
                }
            }

            activeFrame.BorderColor = COLOR_NEON_CYAN;
            activeFrame.Shadow = new Shadow
            {
                Brush = new SolidColorBrush(COLOR_NEON_CYAN),
                Radius = 10.0f,
                Opacity = 0.8f
            };
        }

        /// <summary>
        /// FUNCTION: OnSaveOverlayClicked
        /// DESCRIPTION:
        ///     Saves current overlay strokes by merging them with any existing overlay.
        ///     If overlay path is invalid or a URL, creates a valid file path in
        ///     AppDataDirectory/Overlays. Updates Card model if provided.
        /// </summary>
        private async void OnSaveOverlayClicked(object sender, EventArgs e)
        {
            try
            {
                // ----------------------------------------------------------
                // 1. Ensure overlayFilePath is valid and writable
                // ----------------------------------------------------------
                if (string.IsNullOrWhiteSpace(overlayFilePath) || overlayFilePath.StartsWith("http"))
                {
                    var overlayRoot = Path.Combine(FileSystem.AppDataDirectory, "Overlays");
                    Directory.CreateDirectory(overlayRoot);

                    var fileName = owningCard != null
                        ? $"{owningCard.Id}_{(isFrontImage ? "front" : "back")}_overlay.png"
                        : $"overlay_{Guid.NewGuid()}.png";

                    overlayFilePath = Path.Combine(overlayRoot, fileName);
                }

                // ----------------------------------------------------------
                // 2. If no strokes & file exists, no need to save again
                // ----------------------------------------------------------
                if (completedStrokes.Count == 0 && File.Exists(overlayFilePath))
                {
                    await DisplayAlert("No New Overlay", "No new drawing to save.", "OK");
                    return;
                }

                // Ensure directory exists
                var dir = Path.GetDirectoryName(overlayFilePath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                // ----------------------------------------------------------
                // 3. Build a drawing surface
                // ----------------------------------------------------------
                int width = (int)Math.Max(OverlayCanvas.Width, 1);
                int height = (int)Math.Max(OverlayCanvas.Height, 1);

                SKImageInfo info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
                using SKSurface surface = SKSurface.Create(info);
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);

                // ----------------------------------------------------------
                // 4. Draw existing overlay first (layer 1)
                // ----------------------------------------------------------
                if (File.Exists(overlayFilePath))
                {
                    try
                    {
                        using var fs = File.OpenRead(overlayFilePath);
                        using var oldBmp = SKBitmap.Decode(fs);
                        if (oldBmp != null)
                            canvas.DrawBitmap(oldBmp, 0, 0);
                    }
                    catch (Exception loadEx)
                    {
                        Console.WriteLine($"[Overlay] Failed to load previous: {loadEx.Message}");
                    }
                }

                // ----------------------------------------------------------
                // 5. Draw new strokes (layer 2)
                // ----------------------------------------------------------
                foreach (var (points, color) in completedStrokes)
                {
                    using SKPaint paint = new SKPaint
                    {
                        Color = color.ToSKColor(),
                        Style = SKPaintStyle.Stroke,
                        StrokeWidth = STROKE_WIDTH,
                        IsAntialias = true,
                    };

                    for (int i = 1; i < points.Count; i++)
                    {
                        canvas.DrawLine(points[i - 1].X, points[i - 1].Y,
                                        points[i].X, points[i].Y, paint);
                    }
                }

                // ----------------------------------------------------------
                // 6. SAVE MERGED RESULT
                // ----------------------------------------------------------
                using SKImage finalImage = surface.Snapshot();
                using SKData data = finalImage.Encode(SKEncodedImageFormat.Png, 100);
                await File.WriteAllBytesAsync(overlayFilePath, data.ToArray());

                // ----------------------------------------------------------
                // 7. Update Card + DB
                // ----------------------------------------------------------
                if (owningCard != null)
                {
                    if (isFrontImage)
                        owningCard.FrontOverlayImagePath = overlayFilePath;
                    else
                        owningCard.BackOverlayImagePath = overlayFilePath;

                    await App.Database.UpdateCardAsync(owningCard);
                }

                // ----------------------------------------------------------
                // 8. Refresh In-Memory + UI
                // ----------------------------------------------------------
                LoadOverlayIfExists();
                completedStrokes.Clear();
                OverlayCanvas.Invalidate();

                await DisplayAlert("Overlay Updated", "Your drawings have been merged and saved.", "OK");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Overlay] ERROR: {ex}");
                await DisplayAlert("Unable to save overlay", "There was an error saving the overlay.", "OK");
            }
        }

        /// <summary>
        /// FUNCTION: OnDeleteOverlayClicked
        /// DESCRIPTION:
        ///     Deletes the overlay PNG file and clears stored strokes.
        ///     If a Card was provided, also clears its overlay path.
        /// </summary>
        private async void OnDeleteOverlayClicked(object sender, EventArgs e)
        {
            bool confirm = await DisplayAlert("Delete Overlay?",
                "This will permanently remove the saved overlay.", "Delete", "Cancel");

            if (!confirm)
                return;

            if (File.Exists(overlayFilePath))
                File.Delete(overlayFilePath);

            overlayBitmap = null;
            completedStrokes.Clear();
            OverlayCanvas.Invalidate();

            // Clear any stored overlay reference on the card
            if (owningCard != null)
            {
                if (isFrontImage)
                {
                    owningCard.FrontOverlayImagePath = string.Empty;
                }
                else
                {
                    owningCard.BackOverlayImagePath = string.Empty;
                }
            }

            await DisplayAlert("Deleted", "Overlay has been deleted.", "OK");
        }

        /// <summary>
        /// FUNCTION: LoadOverlayIfExists
        /// DESCRIPTION:
        ///     Loads a previously saved transparent PNG overlay image
        ///     while preserving its alpha channel.
        /// RETURNS:
        ///     None.
        /// </summary>
        private void LoadOverlayIfExists()
        {
            try
            {
                if (File.Exists(overlayFilePath))
                {
                    using FileStream stream = File.OpenRead(overlayFilePath);
                    overlayBitmap = SKBitmap.Decode(stream); // keeps alpha if saved properly
                }
                else
                {
                    overlayBitmap = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ImageViewerPage] Failed to load overlay: {ex.Message}");
                overlayBitmap = null;
            }
        }

        /// <summary>
        /// FUNCTION: Draw
        /// DESCRIPTION:
        ///     Renders the loaded overlay bitmap (if one exists),
        ///     followed by all completed strokes and the currently active stroke.
        /// PARAMETERS:
        ///     canvas – The MAUI drawing surface.
        ///     dirtyRect – The portion of the canvas that must be redrawn.
        /// RETURNS:
        ///     None.
        /// </summary>
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            // Exit early if overlay mode is disabled
            if (!overlayVisible)
                return;

            // === Draw the loaded overlay bitmap first (if available) ===
            if (overlayBitmap != null)
            {
                try
                {
                    using MemoryStream stream = new MemoryStream();
                    using (SKImage image = SKImage.FromBitmap(overlayBitmap))
                    {
                        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
                        data.SaveTo(stream);
                        stream.Position = 0;

                        // Explicitly use Microsoft.Maui.Graphics.IImage to avoid ambiguity
                        Microsoft.Maui.Graphics.IImage mauiImage =
                            Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(stream);

                        canvas.Alpha = 1.0f; // ensure blending enabled
                        canvas.DrawImage(
                            mauiImage,
                            dirtyRect.X,
                            dirtyRect.Y,
                            dirtyRect.Width,
                            dirtyRect.Height
                        );
                        canvas.Alpha = 1.0f; // reset (good habit for subsequent strokes)
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ImageViewerPage] Error drawing overlay bitmap: {ex.Message}");
                }
            }

            // === Draw all completed saved strokes ===
            foreach (var (points, color) in completedStrokes)
            {
                canvas.StrokeColor = color;
                canvas.StrokeSize = STROKE_WIDTH;

                for (int i = 1; i < points.Count; i++)
                {
                    canvas.DrawLine(points[i - 1], points[i]);
                }
            }

            // === Draw the active in-progress stroke ===
            if (strokePoints.Count > 1)
            {
                canvas.StrokeColor = currentColor;
                canvas.StrokeSize = STROKE_WIDTH;

                for (int i = 1; i < strokePoints.Count; i++)
                {
                    canvas.DrawLine(strokePoints[i - 1], strokePoints[i]);
                }
            }
        }

        /// <summary>
        /// FUNCTION: OnCloseClicked
        /// DESCRIPTION:
        ///     Closes the ImageViewerPage modal.
        /// </summary>
        private async void OnCloseClicked(object sender, EventArgs e)
        {
            pulseActive = false; // Stop any pulse animation
            await Navigation.PopModalAsync(true);
        }
    }
}
