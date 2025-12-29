/*
* FILE: ExcelCollectionExportService.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-12-26
* UPDATED: 2026-01-18
* DESCRIPTION:
*     Exports the user’s card collection to Excel.
*     - EMBEDS ORIGINAL FULL-QUALITY IMAGES (no thumbnail generation here)
*     - Images are DISPLAYED as thumbnails by scaling to the cell size
*     - Four image columns: Front, Back, FrontOv, BackOv
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
        /// Folder structure matches the original implementation:
        ///   <outputDirectory>\CollectIQ_Excel_<ts>\CollectIQ_Collection_<ts>.xlsx
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
                throw new InvalidOperationException("There are no cards to export.");
            }

            // Root export folder (unchanged behaviour)
            Directory.CreateDirectory(outputDirectory);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string exportFolderName = $"CollectIQ_Excel_{timestamp}";
            string exportFolder = Path.Combine(outputDirectory, exportFolderName);
            Directory.CreateDirectory(exportFolder);

            string fileName = $"CollectIQ_Collection_{timestamp}.xlsx";
            string fullPath = Path.Combine(exportFolder, fileName);

            return await Task.Run(() =>
            {
                using var workbook = new XLWorkbook();
                var ws = workbook.AddWorksheet("Collection");

                int row = 1;

                // -----------------------------------------------------------------
                // HEADERS – KEEPING YOUR ORDER
                //   1: Front
                //   2: Back
                //   3: FrontOv
                //   4: BackOv
                //   5+: card fields
                // -----------------------------------------------------------------
                ws.Cell(row, 1).Value = "Front";
                ws.Cell(row, 2).Value = "Back";
                ws.Cell(row, 3).Value = "FrontOv";
                ws.Cell(row, 4).Value = "BackOv";
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

                // Image columns get a reasonable width
                ws.Columns(1, 4).Width = 14;

                row++;

                // -----------------------------------------------------------------
                // DATA ROWS
                // -----------------------------------------------------------------
                foreach (var c in cardList)
                {
                    ws.Row(row).Height = ImageRowHeight;

                    // MAIN IMAGES (raw)
                    TryAddPicture(ws, c.FrontImagePath, row, 1);
                    TryAddPicture(ws, c.BackImagePath, row, 2);

                    // Overlay paths: use stored paths, or fall back to legacy "<base>_overlay.png"
                    string? frontOverlayPath = FirstNonEmpty(
                        c.FrontOverlayImagePath,
                        BuildOverlayPathFromBaseImage(c.FrontImagePath));

                    string? backOverlayPath = FirstNonEmpty(
                        c.BackOverlayImagePath,
                        BuildOverlayPathFromBaseImage(c.BackImagePath));

                    // OVERLAY IMAGES (drawn alone, scaled to cell)
                    TryAddPicture(ws, frontOverlayPath, row, 3);
                    TryAddPicture(ws, backOverlayPath, row, 4);

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

                // Auto-fit the text columns (5–14)
                ws.Columns(5, 14).AdjustToContents();

                workbook.SaveAs(fullPath);
                return fullPath;
            });
        }

        /// <summary>
        /// Returns the first non-null / non-whitespace string from the arguments.
        /// </summary>
        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v))
                {
                    return v;
                }
            }

            return null;
        }

        /// <summary>
        /// Builds the legacy overlay path: "&lt;baseImagePathWithoutExt&gt;_overlay.png".
        /// Returns null if baseImagePath is invalid.
        /// </summary>
        private static string? BuildOverlayPathFromBaseImage(string? baseImagePath)
        {
            if (string.IsNullOrWhiteSpace(baseImagePath))
            {
                return null;
            }

            try
            {
                var dir = Path.GetDirectoryName(baseImagePath);
                var name = Path.GetFileNameWithoutExtension(baseImagePath);

                if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(name))
                {
                    return null;
                }

                return Path.Combine(dir, name + "_overlay.png");
            }
            catch
            {
                return null;
            }
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
                if (string.IsNullOrWhiteSpace(imagePath))
                {
                    return;
                }

                // Skip URLs
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

                var picture = ws.AddPicture(imagePath)
                                .MoveTo(cell);

                double colWidth = cell.WorksheetColumn().Width; // Excel units
                double rowHeight = cell.WorksheetRow().Height;   // points

                double maxWidth = colWidth * ExcelColWidthToPixels;
                double maxHeight = rowHeight;

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
            }
        }
    }
}
