/*
* FILE: PdfCollectionExportService.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2026-01-18
* DESCRIPTION:
*     Exports the user’s card collection to PDF using SkiaSharp (NO QuestPDF).
*     - Fixed layout (no overlapping)
*     - Wrap + clamp for long text
*     - Optional images (Front/Back)
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CollectIQ.Models;
using SkiaSharp;

namespace CollectIQ.Services
{
    public static class PdfCollectionExportService
    {
        // Page setup (Letter)
        private const float PageWidth = 612f;   // 8.5 * 72
        private const float PageHeight = 792f;  // 11  * 72
        private const float Margin = 28f;

        // Table layout
        private const float RowHeight = 58f;
        private const float HeaderHeight = 26f;

        // Columns (tuned for your layout)
        private const float ColImg = 64f;     // Front image cell
        private const float ColTitle = 250f;  // Title/Name
        private const float ColSet = 120f;    // Set/Team/Number
        private const float ColYear = 46f;    // Year
        private const float ColGrade = 80f;   // Grade
        private const float ColPrice = 80f;   // Estimated

        // Image thumb size
        private const float ThumbW = 56f;
        private const float ThumbH = 42f;

        public static async Task<string> ExportAsync(
            IEnumerable<Card> cards,
            string outputDirectory,
            bool includeImages)
        {
            if (cards == null)
                throw new ArgumentNullException(nameof(cards));

            var list = cards.ToList();
            if (list.Count == 0)
                throw new InvalidOperationException("There are no cards to export.");

            Directory.CreateDirectory(outputDirectory);

            string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string fileName = $"CollectIQ_Collection_{ts}.pdf";
            string fullPath = Path.Combine(outputDirectory, fileName);

            await Task.Run(() =>
            {
                using var stream = File.OpenWrite(fullPath);
                using var document = SKDocument.CreatePdf(stream);

                // Typeface once
                using var typeface = SKTypeface.FromFamilyName("Arial");

                // Fonts (SKFont is REQUIRED for DrawText in newer SkiaSharp)
                using var fontTitle = new SKFont(typeface, 16f);
                using var fontHeader = new SKFont(typeface, 10f);
                using var fontText = new SKFont(typeface, 10f);
                using var fontSmall = new SKFont(typeface, 9f);

                // Paints (color/style)
                using var paintWhite = new SKPaint { IsAntialias = true, Color = SKColors.White };
                using var paintSmall = new SKPaint { IsAntialias = true, Color = new SKColor(170, 190, 210) };
                using var paintHeaderText = new SKPaint { IsAntialias = true, Color = SKColors.White };
                using var paintLine = new SKPaint { IsAntialias = true, Color = new SKColor(55, 75, 95), StrokeWidth = 1f };
                using var paintHeaderBg = new SKPaint { IsAntialias = true, Color = new SKColor(20, 30, 45) };

                int pageNumber = 0;
                SKCanvas canvas = StartPage(document, ref pageNumber);

                float y = Margin + 48f;

                DrawHeaderBlock(canvas, fontTitle, fontSmall, paintWhite, paintSmall, ts);

                y += 18f;
                DrawTableHeader(canvas, ref y, paintHeaderBg, fontHeader, paintHeaderText, paintLine, includeImages);

                bool alt = false;

                foreach (var card in list)
                {
                    if (y + RowHeight + 36f > PageHeight - Margin)
                    {
                        DrawFooter(canvas, fontSmall, paintSmall, pageNumber);
                        canvas = StartPage(document, ref pageNumber);

                        y = Margin + 48f;
                        DrawHeaderBlock(canvas, fontTitle, fontSmall, paintWhite, paintSmall, ts);

                        y += 18f;
                        DrawTableHeader(canvas, ref y, paintHeaderBg, fontHeader, paintHeaderText, paintLine, includeImages);
                    }

                    DrawRow(
                        canvas,
                        y,
                        card,
                        fontText,
                        fontSmall,
                        paintWhite,
                        paintSmall,
                        paintLine,
                        includeImages,
                        alt);

                    y += RowHeight;
                    alt = !alt;
                }

                DrawFooter(canvas, fontSmall, paintSmall, pageNumber);
                document.Close();
            });

            return fullPath;
        }

        private static SKCanvas StartPage(SKDocument document, ref int pageNumber)
        {
            pageNumber++;
            var pageCanvas = document.BeginPage(PageWidth, PageHeight);
            pageCanvas.Clear(new SKColor(5, 8, 20)); // app vibe background
            return pageCanvas;
        }

        private static void DrawHeaderBlock(
            SKCanvas canvas,
            SKFont fontTitle,
            SKFont fontSmall,
            SKPaint paintTitle,
            SKPaint paintSmall,
            string timestamp)
        {
            canvas.DrawText("CollectIQ — Collection Export", Margin, Margin + 18f, fontTitle, paintTitle);
            canvas.DrawText($"Generated: {timestamp}", Margin, Margin + 36f, fontSmall, paintSmall);

            using var divider = new SKPaint { Color = new SKColor(40, 60, 80), StrokeWidth = 1f, IsAntialias = true };
            canvas.DrawLine(Margin, Margin + 44f, PageWidth - Margin, Margin + 44f, divider);
        }

        private static void DrawFooter(SKCanvas canvas, SKFont fontSmall, SKPaint paintSmall, int pageNumber)
        {
            string footer = $"Page {pageNumber}";
            float w = MeasureText(fontSmall, footer);

            canvas.DrawText(footer, (PageWidth - w) / 2f, PageHeight - Margin, fontSmall, paintSmall);
            canvas.Flush();
        }

        private static void DrawTableHeader(
            SKCanvas canvas,
            ref float y,
            SKPaint paintHeaderBg,
            SKFont fontHeader,
            SKPaint paintHeaderText,
            SKPaint paintLine,
            bool includeImages)
        {
            float x = Margin;
            float tableW = PageWidth - (Margin * 2f);

            canvas.DrawRect(x, y, tableW, HeaderHeight, paintHeaderBg);

            float cx = x;

            if (includeImages)
            {
                DrawHeaderCell(canvas, "Front", cx, y, fontHeader, paintHeaderText);
                cx += ColImg;
            }

            DrawHeaderCell(canvas, "Title / Name", cx, y, fontHeader, paintHeaderText);
            cx += ColTitle;

            DrawHeaderCell(canvas, "Set / Team / #", cx, y, fontHeader, paintHeaderText);
            cx += ColSet;

            DrawHeaderCell(canvas, "Year", cx, y, fontHeader, paintHeaderText);
            cx += ColYear;

            DrawHeaderCell(canvas, "Grade", cx, y, fontHeader, paintHeaderText);
            cx += ColGrade;

            DrawHeaderCell(canvas, "Est.", cx, y, fontHeader, paintHeaderText);

            canvas.DrawLine(x, y + HeaderHeight, x + tableW, y + HeaderHeight, paintLine);
            y += HeaderHeight;
        }

        private static void DrawHeaderCell(SKCanvas canvas, string text, float x, float y, SKFont font, SKPaint paint)
        {
            canvas.DrawText(text, x + 6f, y + 17f, font, paint);
        }

        private static void DrawRow(
            SKCanvas canvas,
            float y,
            Card card,
            SKFont fontText,
            SKFont fontSmall,
            SKPaint paintText,
            SKPaint paintSmall,
            SKPaint paintLine,
            bool includeImages,
            bool altBackground)
        {
            float x = Margin;
            float tableW = PageWidth - (Margin * 2f);

            if (altBackground)
            {
                using var bg = new SKPaint { Color = new SKColor(8, 12, 26), IsAntialias = true };
                canvas.DrawRect(x, y, tableW, RowHeight, bg);
            }

            float cx = x;

            if (includeImages)
            {
                DrawThumbCell(canvas, card.FrontImagePath, cx, y, ColImg, RowHeight);
                cx += ColImg;
            }

            // Title/Name
            string title = (card.Title ?? string.Empty).Trim();
            string name = (card.Name ?? string.Empty).Trim();
            string titleLine = string.IsNullOrWhiteSpace(title) ? name : title;

            DrawWrappedText(canvas, titleLine, cx + 6f, y + 18f, ColTitle - 12f, fontText, paintText, maxLines: 2);

            if (!string.IsNullOrWhiteSpace(name) && !string.Equals(title, name, StringComparison.OrdinalIgnoreCase))
            {
                DrawWrappedText(canvas, name, cx + 6f, y + 40f, ColTitle - 12f, fontSmall, paintSmall, maxLines: 1);
            }

            cx += ColTitle;

            // Set/Team/#
            string setTeamNo = BuildSetTeamNumber(card);
            DrawWrappedText(canvas, setTeamNo, cx + 6f, y + 18f, ColSet - 12f, fontSmall, paintSmall, maxLines: 2);
            cx += ColSet;

            // Year
            string year = card.Year.HasValue && card.Year.Value > 0
                ? card.Year.Value.ToString(CultureInfo.InvariantCulture)
                : string.Empty;

            canvas.DrawText(year, cx + 6f, y + 28f, fontText, paintText);
            cx += ColYear;

            // Grade (double? safe)
            string grade = BuildGrade(card);
            DrawWrappedText(canvas, grade, cx + 6f, y + 28f, ColGrade - 12f, fontText, paintText, maxLines: 1);
            cx += ColGrade;

            // Estimated value (right aligned)
            string est = card.EstimatedValue.HasValue && card.EstimatedValue.Value > 0m
                ? $"${card.EstimatedValue.Value:0.00}"
                : string.Empty;

            float estW = MeasureText(fontText, est);
            canvas.DrawText(est, cx + ColPrice - 6f - estW, y + 28f, fontText, paintText);

            canvas.DrawLine(x, y + RowHeight, x + tableW, y + RowHeight, paintLine);
        }

        private static void DrawThumbCell(SKCanvas canvas, string? imagePath, float x, float y, float w, float h)
        {
            using var border = new SKPaint
            {
                Color = new SKColor(45, 65, 85),
                StrokeWidth = 1f,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke
            };

            canvas.DrawRect(x + 4f, y + 8f, w - 8f, h - 16f, border);

            if (string.IsNullOrWhiteSpace(imagePath) || imagePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return;

            if (!File.Exists(imagePath))
                return;

            try
            {
                using var bmp = SafeDecodeDownscaled(imagePath, 400, 400);
                if (bmp == null)
                    return;

                using var resized = bmp.Resize(new SKImageInfo((int)ThumbW, (int)ThumbH), SKFilterQuality.Medium);
                if (resized == null)
                    return;

                float px = x + (w - ThumbW) / 2f;
                float py = y + (h - ThumbH) / 2f;

                canvas.DrawBitmap(resized, new SKRect(px, py, px + ThumbW, py + ThumbH));
            }
            catch
            {
                // Never throw from export
            }
        }

        private static SKBitmap? SafeDecodeDownscaled(string path, int maxW, int maxH)
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var codec = SKCodec.Create(stream);
                if (codec == null)
                    return null;

                var info = codec.Info;

                int sample = 1;
                while ((info.Width / sample) > maxW || (info.Height / sample) > maxH)
                {
                    sample *= 2;
                }

                var scaledInfo = new SKImageInfo(info.Width / sample, info.Height / sample);
                var bmp = new SKBitmap(scaledInfo);

                var result = codec.GetPixels(scaledInfo, bmp.GetPixels());
                return result == SKCodecResult.Success || result == SKCodecResult.IncompleteInput ? bmp : null;
            }
            catch
            {
                return null;
            }
        }

        private static void DrawWrappedText(
            SKCanvas canvas,
            string text,
            float x,
            float y,
            float maxWidth,
            SKFont font,
            SKPaint paint,
            int maxLines)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var lines = WrapText(text, font, maxWidth, maxLines);
            float lineH = font.Size + 2f;

            for (int i = 0; i < lines.Count; i++)
            {
                canvas.DrawText(lines[i], x, y + (i * lineH), font, paint);
            }
        }

        private static List<string> WrapText(string text, SKFont font, float maxWidth, int maxLines)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var lines = new List<string>();
            string current = string.Empty;

            foreach (var w in words)
            {
                string candidate = string.IsNullOrEmpty(current) ? w : current + " " + w;

                if (MeasureText(font, candidate) <= maxWidth)
                {
                    current = candidate;
                }
                else
                {
                    if (!string.IsNullOrEmpty(current))
                    {
                        lines.Add(current);
                    }

                    current = w;

                    if (lines.Count == maxLines - 1)
                    {
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(current) && lines.Count < maxLines)
            {
                lines.Add(current);
            }

            if (lines.Count == maxLines)
            {
                string last = lines[^1];
                while (MeasureText(font, last + "…") > maxWidth && last.Length > 0)
                {
                    last = last.Substring(0, last.Length - 1);
                }

                lines[^1] = last.Length > 0 ? last + "…" : "…";
            }

            return lines;
        }

        private static float MeasureText(SKFont font, string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;

            return font.MeasureText(text);
        }

        private static string BuildGrade(Card card)
        {
            string gradeCompany = card.GradeCompany?.Trim() ?? string.Empty;

            if (card.Grade.HasValue)
            {
                string gradeNumber = card.Grade.Value.ToString("0.##", CultureInfo.InvariantCulture);

                return string.IsNullOrWhiteSpace(gradeCompany)
                    ? gradeNumber
                    : $"{gradeCompany} {gradeNumber}".Trim();
            }

            return gradeCompany;
        }

        private static string BuildSetTeamNumber(Card card)
        {
            string set = (card.Set ?? string.Empty).Trim();
            string team = (card.Team ?? string.Empty).Trim();
            string num = (card.Number ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(num))
            {
                num = "#" + num;
            }
            //TODO remove after just checking origin push
            string part1 = string.Join(" • ", new[] { set, team }.Where(s => !string.IsNullOrWhiteSpace(s)));
            string part2 = string.Join(" ", new[] { part1, num }.Where(s => !string.IsNullOrWhiteSpace(s)));

            return part2.Trim();
        }
    }
}
