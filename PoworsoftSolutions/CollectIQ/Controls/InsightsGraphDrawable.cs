using System;
using System.Collections.Generic;
using Microsoft.Maui.Graphics;

namespace CollectIQ.Controls
{
    /// <summary>
    /// Draws a "PriceCharting-style" grade price chart for a single card:
    /// - X axis: grades (RAW, PSA7, PSA8, PSA9, BGS9.5, PSA10)
    /// - Y axis: USD price
    /// - Optional horizontal "Suggested" line
    /// - Optional light volume bars (sales volume or listing count)
    /// </summary>
    public class InsightsGraphDrawable : IDrawable
    {
        private readonly List<GradePoint> _gradeSeries = new List<GradePoint>();
        private readonly List<double> _scatterComps = new List<double>();

        private double? _suggestedUsd;
        private double? _volume; // can be sales volume or listing count

        private readonly string[] _gradeLabels =
        {
            "RAW",
            "PSA 7",
            "PSA 8",
            "PSA 9",
            "BGS 9.5",
            "PSA 10"
        };

        public void SetData(
            Dictionary<string, double?> guidePricesByKey,
            IEnumerable<double>? compsUsd,
            double? suggestedUsd,
            double? volume)
        {
            _gradeSeries.Clear();
            _scatterComps.Clear();

            _suggestedUsd = suggestedUsd;
            _volume = volume;

            // Build the grade points in a fixed, predictable order.
            // Keys are: raw, psa7, psa8, psa9, bgs95, psa10
            AddPoint("raw", guidePricesByKey);
            AddPoint("psa7", guidePricesByKey);
            AddPoint("psa8", guidePricesByKey);
            AddPoint("psa9", guidePricesByKey);
            AddPoint("bgs95", guidePricesByKey);
            AddPoint("psa10", guidePricesByKey);

            if (compsUsd != null)
            {
                foreach (double v in compsUsd)
                {
                    if (v > 0)
                    {
                        _scatterComps.Add(v);
                    }
                }
            }
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (dirtyRect.Width <= 10 || dirtyRect.Height <= 10)
            {
                return;
            }

            // Layout
            float pad = 14f;
            float left = dirtyRect.Left + pad;
            float top = dirtyRect.Top + pad;
            float right = dirtyRect.Right - pad;
            float bottom = dirtyRect.Bottom - pad;

            float plotTop = top + 6;
            float plotBottom = bottom - 18; // leave space for labels
            float plotLeft = left + 18;     // leave space for y labels
            float plotRight = right;

            RectF plot = new RectF(plotLeft, plotTop, plotRight - plotLeft, plotBottom - plotTop);

            // Background card with soft "glow" edge
            DrawBackground(canvas, dirtyRect);

            // Determine scale (max value across guide points + comps + suggested)
            double max = 0;
            foreach (GradePoint p in _gradeSeries)
            {
                if (p.ValueUsd.HasValue)
                {
                    max = Math.Max(max, p.ValueUsd.Value);
                }
            }

            foreach (double c in _scatterComps)
            {
                max = Math.Max(max, c);
            }

            if (_suggestedUsd.HasValue)
            {
                max = Math.Max(max, _suggestedUsd.Value);
            }

            if (max <= 0)
            {
                DrawEmpty(canvas, plot);
                return;
            }

            // Add headroom
            max *= 1.10;

            // Grid
            DrawGrid(canvas, plot, max);

            // Optional "volume" bars (light, subtle) - we draw a single aggregate bar
            DrawVolume(canvas, plot);

            // Scatter points (eBay listings)
            DrawScatter(canvas, plot, max);

            // Guide line
            DrawGuideLine(canvas, plot, max);

            // Suggested line
            DrawSuggestedLine(canvas, plot, max);

            // X labels
            DrawXAxisLabels(canvas, plot);
        }

        private void AddPoint(string key, Dictionary<string, double?> map)
        {
            map.TryGetValue(key, out double? v);
            _gradeSeries.Add(new GradePoint(key, v));
        }

        private void DrawBackground(ICanvas canvas, RectF dirtyRect)
        {
            // Main fill
            canvas.SaveState();
            canvas.FillColor = new Color(0.10f, 0.14f, 0.22f, 0.98f);
            canvas.FillRoundedRectangle(dirtyRect.Left + 2, dirtyRect.Top + 2, dirtyRect.Width - 4, dirtyRect.Height - 4, 16);

            // Border
            canvas.StrokeColor = new Color(0.25f, 0.85f, 0.85f, 0.30f);
            canvas.StrokeSize = 2;
            canvas.DrawRoundedRectangle(dirtyRect.Left + 2, dirtyRect.Top + 2, dirtyRect.Width - 4, dirtyRect.Height - 4, 16);

            canvas.RestoreState();
        }

        private void DrawEmpty(ICanvas canvas, RectF plot)
        {
            canvas.SaveState();
            canvas.FontColor = Colors.LightGray;
            canvas.FontSize = 12;
            canvas.DrawString("No data to chart.", plot, HorizontalAlignment.Center, VerticalAlignment.Center);
            canvas.RestoreState();
        }

        private void DrawGrid(ICanvas canvas, RectF plot, double max)
        {
            canvas.SaveState();

            int lines = 4;
            canvas.StrokeColor = new Color(1, 1, 1, 0.10f);
            canvas.StrokeSize = 1;

            for (int i = 0; i <= lines; i++)
            {
                float y = plot.Top + (plot.Height * i / lines);
                canvas.DrawLine(plot.Left, y, plot.Right, y);

                // Y labels (0, max/2, max...)
                double labelVal = max * (1 - (i / (double)lines));
                if (i == lines) labelVal = 0;

                canvas.FontSize = 10;
                canvas.FontColor = new Color(1, 1, 1, 0.45f);
                canvas.DrawString(
                    $"${Math.Round(labelVal, 0)}",
                    plot.Left - 16,
                    y - 6,
                    40,
                    12,
                    HorizontalAlignment.Right,
                    VerticalAlignment.Center);
            }

            canvas.RestoreState();
        }

        private void DrawVolume(ICanvas canvas, RectF plot)
        {
            if (!_volume.HasValue || _volume.Value <= 0)
            {
                return;
            }

            canvas.SaveState();

            // One subtle bar along bottom-left representing volume
            float barW = Math.Min(80, plot.Width * 0.25f);
            float barH = 10;
            float x = plot.Left + 6;
            float y = plot.Bottom - barH - 4;

            canvas.FillColor = new Color(0.75f, 0.75f, 0.75f, 0.25f);
            canvas.FillRoundedRectangle(x, y, barW, barH, 5);

            canvas.FontSize = 9;
            canvas.FontColor = new Color(1, 1, 1, 0.55f);
            canvas.DrawString($"Vol: {Math.Round(_volume.Value, 0)}", x + barW + 6, y - 1, 120, 14, HorizontalAlignment.Left, VerticalAlignment.Center);

            canvas.RestoreState();
        }

        private void DrawScatter(ICanvas canvas, RectF plot, double max)
        {
            if (_scatterComps.Count == 0)
            {
                return;
            }

            canvas.SaveState();

            // Scatter is drawn as small dots across the plot width.
            // We spread them evenly so it looks like "activity".
            canvas.FillColor = new Color(0.60f, 0.95f, 0.65f, 0.55f);

            int n = _scatterComps.Count;
            for (int i = 0; i < n; i++)
            {
                double v = _scatterComps[i];
                float x = plot.Left + (plot.Width * i / Math.Max(1, n - 1));
                float y = plot.Bottom - (float)((v / max) * plot.Height);

                canvas.FillCircle(x, y, 2.6f);
            }

            canvas.RestoreState();
        }

        private void DrawGuideLine(ICanvas canvas, RectF plot, double max)
        {
            // Draw line connecting available grade points.
            // Use a bright aqua stroke to match CollectIQ styling.
            canvas.SaveState();

            canvas.StrokeColor = new Color(0.20f, 0.90f, 1.00f, 0.95f);
            canvas.StrokeSize = 2.5f;

            float step = plot.Width / Math.Max(1, _gradeSeries.Count - 1);

            GradePoint? prev = null;
            for (int i = 0; i < _gradeSeries.Count; i++)
            {
                GradePoint p = _gradeSeries[i];
                if (!p.ValueUsd.HasValue)
                {
                    continue;
                }

                float x = plot.Left + (step * i);
                float y = plot.Bottom - (float)((p.ValueUsd.Value / max) * plot.Height);

                if (prev != null && prev.ValueUsd.HasValue)
                {
                    float px = plot.Left + (step * prev.Index);
                    float py = plot.Bottom - (float)((prev.ValueUsd.Value / max) * plot.Height);
                    canvas.DrawLine(px, py, x, y);
                }

                // marker
                canvas.FillColor = new Color(0.20f, 0.90f, 1.00f, 0.90f);
                canvas.FillCircle(x, y, 3.8f);

                p.Index = i;
                prev = p;
            }

            canvas.RestoreState();
        }

        private void DrawSuggestedLine(ICanvas canvas, RectF plot, double max)
        {
            if (!_suggestedUsd.HasValue || _suggestedUsd.Value <= 0)
            {
                return;
            }

            canvas.SaveState();

            float y = plot.Bottom - (float)((_suggestedUsd.Value / max) * plot.Height);

            canvas.StrokeColor = new Color(1.00f, 0.75f, 0.20f, 0.95f);
            canvas.StrokeSize = 2;

            // dashed
            float dash = 6;
            for (float x = plot.Left; x < plot.Right; x += dash * 2)
            {
                canvas.DrawLine(x, y, Math.Min(x + dash, plot.Right), y);
            }

            canvas.FontSize = 10;
            canvas.FontColor = new Color(1, 1, 1, 0.70f);
            canvas.DrawString($"Suggested: ${Math.Round(_suggestedUsd.Value, 2)}", plot.Right - 130, y - 14, 130, 14, HorizontalAlignment.Right, VerticalAlignment.Center);

            canvas.RestoreState();
        }

        private void DrawXAxisLabels(ICanvas canvas, RectF plot)
        {
            canvas.SaveState();

            canvas.FontSize = 9;
            canvas.FontColor = new Color(1, 1, 1, 0.60f);

            float step = plot.Width / Math.Max(1, _gradeLabels.Length - 1);

            for (int i = 0; i < _gradeLabels.Length; i++)
            {
                float x = plot.Left + (step * i);
                canvas.DrawString(_gradeLabels[i], x - 22, plot.Bottom + 2, 44, 14, HorizontalAlignment.Center, VerticalAlignment.Top);
            }

            canvas.RestoreState();
        }

        private class GradePoint
        {
            public GradePoint(string key, double? valueUsd)
            {
                Key = key;
                ValueUsd = valueUsd;
            }

            public string Key { get; }
            public double? ValueUsd { get; }
            public int Index { get; set; }
        }
    }
}
