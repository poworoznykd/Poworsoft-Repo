/*
* FILE: ExcelCollectionExportService.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-12-26
* DESCRIPTION:
*     Exports the user’s collection to an .xlsx file with an embedded
*     front thumbnail in the first column, using ClosedXML.
*     Backside is NOT thumbnailed – only its path is stored.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using CollectIQ.Models;

// ImageSharp
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Processing;

// aliases to avoid MAUI conflicts
using ImageSharpImage = SixLabors.ImageSharp.Image;
using ImageSharpSize = SixLabors.ImageSharp.Size;
using ImageSharpDecoderOptions = SixLabors.ImageSharp.Formats.DecoderOptions;

namespace CollectIQ.Services
{
    public static class ExcelCollectionExportService
    {
        /// <summary>
        /// Exports the given cards to an .xlsx file in the specified folder.
        /// Returns the full path to the created file.
        /// 
        /// NOTE:
        /// - Only uses *pre-generated* front thumbnails (Card.FrontThumbnailPath).
        /// - Does NOT generate thumbnails during export (for performance).
        /// - Back image is stored only as a path, not a thumbnail.
        /// </summary>
        public static async Task<string> ExportAsync(
            IEnumerable<Card> cards,
            string outputDirectory)
        {
            if (cards is null)
            {
                throw new ArgumentNullException(nameof(cards));
            }

            var cardList = cards.ToList();
            if (cardList.Count == 0)
            {
                throw new InvalidOperationException("There are no cards to export.");
            }

            return await Task.Run(() =>
            {
                // Root export folder
                Directory.CreateDirectory(outputDirectory);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string exportFolderName = $"CollectIQ_Excel_{timestamp}";
                string exportFolder = Path.Combine(outputDirectory, exportFolderName);
                Directory.CreateDirectory(exportFolder);

                string fileName = $"CollectIQ_Collection_{timestamp}.xlsx";
                string fullPath = Path.Combine(exportFolder, fileName);

                using (var workbook = new XLWorkbook())
                {
                    var ws = workbook.AddWorksheet("Collection");

                    // --------------------
                    // Header row
                    // --------------------
                    int row = 1;

                    ws.Cell(row, 1).Value = "Thumb";      // front thumbnail in first column
                    ws.Cell(row, 2).Value = "Title";
                    ws.Cell(row, 3).Value = "Name";
                    ws.Cell(row, 4).Value = "Team";
                    ws.Cell(row, 5).Value = "Year";
                    ws.Cell(row, 6).Value = "Set";
                    ws.Cell(row, 7).Value = "Number";
                    ws.Cell(row, 8).Value = "GradeCo";
                    ws.Cell(row, 9).Value = "Grade";
                    ws.Cell(row, 10).Value = "Purchase";
                    ws.Cell(row, 11).Value = "Estimated";
                    ws.Cell(row, 12).Value = "Front";
                    ws.Cell(row, 13).Value = "Back";
                    ws.Cell(row, 14).Value = "FrontOv";
                    ws.Cell(row, 15).Value = "BackOv";
                    ws.Cell(row, 16).Value = "FrontPath";
                    ws.Cell(row, 17).Value = "BackPath";
                    ws.Cell(row, 18).Value = "FrontOvPath";
                    ws.Cell(row, 19).Value = "BackOvPath";

                    ws.Row(row).Style.Font.Bold = true;

                    // Reasonable default column widths
                    ws.Column(1).Width = 18;              // thumbnail
                    ws.Columns(2, 8).AdjustToContents();   // text columns
                    ws.Column(9).Width = 10;               // Grade
                    ws.Column(10).Width = 10;              // Purchase
                    ws.Column(11).Width = 10;              // Estimated
                    ws.Columns(12, 15).Width = 8;          // flags
                    ws.Columns(16, 19).Width = 60;         // paths

                    // --------------------
                    // Data rows
                    // --------------------
                    row++;

                    const double ThumbnailRowHeight = 80.0; // points

                    foreach (var c in cardList)
                    {
                        // Fix row height for thumbnail
                        ws.Row(row).Height = ThumbnailRowHeight;

                        // --------------------
                        // Thumbnail (column 1)
                        // --------------------
                       
                        string? thumbPath = null;

                        if (!string.IsNullOrWhiteSpace(c.FrontThumbnailPath) &&
                            File.Exists(c.FrontThumbnailPath))
                        {
                            thumbPath = c.FrontThumbnailPath;
                        }
                        else if (!string.IsNullOrWhiteSpace(c.FrontImagePath) &&
                                 File.Exists(c.FrontImagePath))
                        {
                            // fallback: use full-size front image if no thumbnail
                            thumbPath = c.FrontImagePath;
                        }

                        TryAddPicture(ws, thumbPath, row, 1);


                        // --------------------
                        // Basic card data (shifted by +1 because col 1 is the thumb)
                        // --------------------
                        ws.Cell(row, 2).Value = c.Title;
                        ws.Cell(row, 3).Value = c.Name;
                        ws.Cell(row, 4).Value = c.Team;
                        ws.Cell(row, 5).Value = c.Year;
                        ws.Cell(row, 6).Value = c.Set;
                        ws.Cell(row, 7).Value = c.Number;
                        ws.Cell(row, 8).Value = c.GradeCompany;
                        ws.Cell(row, 9).Value = c.Grade;

                        if (c.PurchasePrice.HasValue)
                        {
                            ws.Cell(row, 10).Value = (double)c.PurchasePrice.Value;
                        }

                        if (c.EstimatedValue.HasValue)
                        {
                            ws.Cell(row, 11).Value = (double)c.EstimatedValue.Value;
                        }

                        // Flags
                        ws.Cell(row, 12).Value = string.IsNullOrEmpty(c.FrontImagePath) ? "" : "Front";
                        ws.Cell(row, 13).Value = string.IsNullOrEmpty(c.BackImagePath) ? "" : "Back";
                        ws.Cell(row, 14).Value = string.IsNullOrEmpty(c.FrontOverlayImagePath) ? "" : "Front";
                        ws.Cell(row, 15).Value = string.IsNullOrEmpty(c.BackOverlayImagePath) ? "" : "Back";

                        // Paths (as text)
                        ws.Cell(row, 16).Value = c.FrontImagePath ?? string.Empty;
                        ws.Cell(row, 17).Value = c.BackImagePath ?? string.Empty;
                        ws.Cell(row, 18).Value = c.FrontOverlayImagePath ?? string.Empty;
                        ws.Cell(row, 19).Value = c.BackOverlayImagePath ?? string.Empty;

                        row++;
                    }

                    workbook.SaveAs(fullPath);
                }

                return fullPath;
            });
        }

        /// <summary>
        /// Create an auto-oriented, resized JPEG thumbnail from sourceFullPath.
        /// Returns the full path to the thumbnail or null on failure.
        /// Never throws.
        /// 
        /// NOTE:
        /// This is intended to be called when setting the card image
        /// (NOT during export).
        /// </summary>
        public static string? CreateThumbnailFixedOrientation(
            string sourceFullPath,
            string imagesFolder,
            int maxSize)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourceFullPath) || !File.Exists(sourceFullPath))
                {
                    Debug.WriteLine($"[THUMBNAIL] Source file not found: {sourceFullPath}");
                    return null;
                }

                Directory.CreateDirectory(imagesFolder);

                string thumbFileName = "thumb_" + Path.GetFileName(sourceFullPath);
                string thumbFullPath = Path.Combine(imagesFolder, thumbFileName);

                Debug.WriteLine($"[THUMBNAIL] Creating thumbnail for {sourceFullPath} -> {thumbFullPath}");

                var decoderOptions = new ImageSharpDecoderOptions
                {
                    // Decode directly to a smaller size instead of full-res then downscale
                    TargetSize = new ImageSharpSize(maxSize, maxSize)
                };

                using (var fs = File.OpenRead(sourceFullPath))
                using (var image = ImageSharpImage.Load(decoderOptions, fs))
                {
                    // Auto-orient once the image is decoded to the target size
                    image.Mutate(ctx => ctx.AutoOrient());

                    // Save (format inferred from extension, e.g., .jpg)
                    image.Save(thumbFullPath);
                }

                return thumbFullPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[THUMBNAIL] Failed to create thumbnail for '{sourceFullPath}': {ex}");
                return null;
            }
        }

        /// <summary>
        /// Safely adds a picture anchored to the given cell, scaled to fit.
        /// Never throws if anything goes wrong.
        /// </summary>
        private static void TryAddPicture(IXLWorksheet ws, string? imagePath, int row, int column)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imagePath))
                {
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

                // Scale to fit the cell (approximate – good enough for thumbnails)
                double colWidth = cell.WorksheetColumn().Width; // Excel units
                double rowHeight = cell.WorksheetRow().Height;  // points

                // Excel column width to approx. pixels factor ~7.5
                double maxWidth = colWidth * 7.5;
                double maxHeight = rowHeight;

                double scaleX = maxWidth / picture.OriginalWidth;
                double scaleY = maxHeight / picture.OriginalHeight;
                double scale = Math.Min(scaleX, scaleY);

                if (scale < 1.0)
                {
                    picture.Scale(scale);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[THUMBNAIL] Failed to add picture '{imagePath}': {ex}");
                // Swallow – we don't want to break export / share sheet
            }
        }
    }
}
