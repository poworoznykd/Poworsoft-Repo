/*
* FILE: ExcelCollectionExportService.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-12-26
* UPDATED: 2026-01-18
* DESCRIPTION:
*     Exports the user’s card collection to Excel.
*     - EMBEDS ORIGINAL FULL-QUALITY IMAGES (no thumbnail generation)
*     - Images are DISPLAYED as thumbnails by scaling to the cell size
*     - Four image columns: Front, Back, Front Overlay, Back Overlay
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using CollectIQ.Models;

namespace CollectIQ.Services
{
    public static class ExcelCollectionExportService
    {
        // Approximate Excel column-width to pixel factor
        private const double ExcelColWidthToPixels = 7.5;

        // Height of rows that contain images (points)
        private const double ImageRowHeight = 80.0;

        /// <summary>
        /// Exports the given cards to an .xlsx file in the specified folder.
        /// </summary>
        public static async Task<string> ExportAsync(IEnumerable<Card> cards, string outputDirectory)
        {
            if (cards == null)
            {
                throw new ArgumentNullException(nameof(cards));
            }

            var cardList = cards.ToList();
            if (cardList.Count == 0)
            {
                throw new InvalidOperationException("No cards to export.");
            }

            Directory.CreateDirectory(outputDirectory);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"CollectIQ_Export_{timestamp}.xlsx";
            string fullPath = Path.Combine(outputDirectory, fileName);

            return await Task.Run(() =>
            {
                using var workbook = new XLWorkbook();
                var ws = workbook.AddWorksheet("Collection");

                int row = 1;

                // -----------------------------------------------------------------
                // HEADERS – KEEPING YOUR REQUESTED ORDER
                // -----------------------------------------------------------------
                ws.Cell(row, 1).Value = "Front";
                ws.Cell(row, 2).Value = "Back";
                ws.Cell(row, 3).Value = "Front Overlay";
                ws.Cell(row, 4).Value = "Back Overlay";
                ws.Cell(row, 5).Value = "Title";
                ws.Cell(row, 6).Value = "Name";
                ws.Cell(row, 7).Value = "Team";
                ws.Cell(row, 8).Value = "Year";
                ws.Cell(row, 9).Value = "Set";
                ws.Cell(row, 10).Value = "Number";
                ws.Cell(row, 11).Value = "GradeCo";
                ws.Cell(row, 12).Value = "Grade";
                ws.Cell(row, 13).Value = "Purchase";
                ws.Cell(row, 14).Value = "Estimated";

                ws.Row(row).Style.Font.Bold = true;

                // Set reasonable widths for image columns (1–4)
                ws.Columns(1, 4).Width = 14;     // wide enough for small thumbs
                row++;

                // -----------------------------------------------------------------
                // DATA ROWS
                // -----------------------------------------------------------------
                foreach (var c in cardList)
                {
                    ws.Row(row).Height = ImageRowHeight;

                    // IMAGES – these calls embed the ORIGINAL file,
                    // but scale the displayed size to fit the cell.
                    TryAddPicture(ws, c.FrontImagePath, row, 1);
                    TryAddPicture(ws, c.BackImagePath, row, 2);
                    TryAddPicture(ws, c.FrontOverlayImagePath, row, 3);
                    TryAddPicture(ws, c.BackOverlayImagePath, row, 4);

                    // BASIC CARD DATA
                    ws.Cell(row, 5).Value = c.Title;
                    ws.Cell(row, 6).Value = c.Name;
                    ws.Cell(row, 7).Value = c.Team;
                    ws.Cell(row, 8).Value = c.Year;
                    ws.Cell(row, 9).Value = c.Set;
                    ws.Cell(row, 10).Value = c.Number;
                    ws.Cell(row, 11).Value = c.GradeCompany;
                    ws.Cell(row, 12).Value = c.Grade;
                    ws.Cell(row, 13).Value = c.PurchasePrice ?? 0m;
                    ws.Cell(row, 14).Value = c.EstimatedValue ?? 0m;

                    row++;
                }

                // Auto-fit the text columns (5–14) after data is in
                ws.Columns(5, 14).AdjustToContents();

                workbook.SaveAs(fullPath);
                return fullPath;
            });
        }

        /// <summary>
        /// Safely adds a picture anchored to the given cell, scaled to fit.
        /// Uses the ORIGINAL image file; only the displayed size is reduced.
        /// Never throws – logs and skips on any error.
        /// </summary>
        private static void TryAddPicture(IXLWorksheet ws, string? imagePath, int row, int column)
        {
            try
            {
                // No path, nothing to do
                if (string.IsNullOrWhiteSpace(imagePath))
                {
                    return;
                }

                // Skip URLs (old bad overlay paths like "https://...")
                if (imagePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"[THUMBNAIL] Skipping non-file image path: {imagePath}");
                    return;
                }

                if (!File.Exists(imagePath))
                {
                    Debug.WriteLine($"[THUMBNAIL] Picture file not found: {imagePath}");
                    return;
                }

                var cell = ws.Cell(row, column);

                // Create the picture anchored to the cell
                var picture = ws.AddPicture(imagePath)
                                .MoveTo(cell);

                // Scale to fit the cell (approximate thumbnail)
                double colWidth = cell.WorksheetColumn().Width; // Excel units
                double rowHeight = cell.WorksheetRow().Height;  // points

                double maxWidth = colWidth * ExcelColWidthToPixels;
                double maxHeight = rowHeight;

                // Avoid divide-by-zero, etc.
                if (picture.OriginalWidth <= 0 || picture.OriginalHeight <= 0)
                {
                    return;
                }

                double scaleX = maxWidth / picture.OriginalWidth;
                double scaleY = maxHeight / picture.OriginalHeight;
                double scale = Math.Min(scaleX, scaleY);

                if (scale < 1.0 && scale > 0.0)
                {
                    picture.Scale(scale);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[THUMBNAIL] Failed to add picture '{imagePath}': {ex}");
                // swallow – we don't want to break the export
            }
        }
    }
}
