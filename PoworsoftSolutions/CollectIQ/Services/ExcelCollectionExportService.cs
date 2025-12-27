/*
* FILE: ExcelCollectionExportService.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-12-26
* DESCRIPTION:
*     Exports the user’s collection to an .xlsx file with embedded
*     front/back thumbnails, using ClosedXML. This does NOT replace
*     the CSV exporter – it lives beside it.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using CollectIQ.Models;

namespace CollectIQ.Services
{
    public static class ExcelCollectionExportService
    {
        /// <summary>
        /// Exports the given cards to an .xlsx file in the specified folder.
        /// Returns the full path to the created file.
        /// </summary>
        public static async Task<string> ExportAsync(
            IEnumerable<Card> cards,
            string outputDirectory)
        {
            if (cards is null)
            {
                throw new ArgumentNullException(nameof(cards));
            }

            // Materialize once so we can safely iterate multiple times.
            var cardList = cards.ToList();
            if (cardList.Count == 0)
            {
                throw new InvalidOperationException("There are no cards to export.");
            }

            return await Task.Run(() =>
            {
                Directory.CreateDirectory(outputDirectory);

                string fileName = $"CollectIQ_Collection_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                string fullPath = Path.Combine(outputDirectory, fileName);

                using (var workbook = new XLWorkbook())
                {
                    var ws = workbook.AddWorksheet("Collection");

                    // --------------------
                    // Header row
                    // --------------------
                    int row = 1;

                    ws.Cell(row, 1).Value = "Title";
                    ws.Cell(row, 2).Value = "Name";
                    ws.Cell(row, 3).Value = "Team";
                    ws.Cell(row, 4).Value = "Year";
                    ws.Cell(row, 5).Value = "Set";
                    ws.Cell(row, 6).Value = "Number";
                    ws.Cell(row, 7).Value = "GradeCo";
                    ws.Cell(row, 8).Value = "Grade";
                    ws.Cell(row, 9).Value = "Purchase";
                    ws.Cell(row, 10).Value = "Estimated";
                    ws.Cell(row, 11).Value = "Front";
                    ws.Cell(row, 12).Value = "Back";
                    ws.Cell(row, 13).Value = "FrontOv";
                    ws.Cell(row, 14).Value = "BackOv";
                    ws.Cell(row, 15).Value = "FrontPath";
                    ws.Cell(row, 16).Value = "BackPath";
                    ws.Cell(row, 17).Value = "FrontOvPath";
                    ws.Cell(row, 18).Value = "BackOvPath";
                    ws.Cell(row, 19).Value = "FrontThumb";
                    ws.Cell(row, 20).Value = "BackThumb";

                    ws.Row(row).Style.Font.Bold = true;

                    // Reasonable default column widths
                    ws.Columns(1, 8).AdjustToContents();
                    ws.Column(9).Width = 10;  // Purchase
                    ws.Column(10).Width = 10; // Estimated
                    ws.Columns(11, 14).Width = 8;
                    ws.Columns(15, 18).Width = 60; // paths
                    ws.Columns(19, 20).Width = 18; // thumbnails

                    // --------------------
                    // Data rows
                    // --------------------
                    row++;

                    // Fixed row height for thumbnail rows (in points)
                    const double ThumbnailRowHeight = 80.0;

                    foreach (var c in cardList)
                    {
                        // Basic card data
                        ws.Cell(row, 1).Value = c.Title;
                        ws.Cell(row, 2).Value = c.Name;
                        ws.Cell(row, 3).Value = c.Team;
                        ws.Cell(row, 4).Value = c.Year;
                        ws.Cell(row, 5).Value = c.Set;
                        ws.Cell(row, 6).Value = c.Number;
                        ws.Cell(row, 7).Value = c.GradeCompany;
                        ws.Cell(row, 8).Value = c.Grade;

                        if (c.PurchasePrice.HasValue)
                        {
                            ws.Cell(row, 9).Value = (double)c.PurchasePrice.Value;
                        }

                        if (c.EstimatedValue.HasValue)
                        {
                            ws.Cell(row, 10).Value = (double)c.EstimatedValue.Value;
                        }

                        // Flags (keep your existing semantics)
                        ws.Cell(row, 11).Value = string.IsNullOrEmpty(c.FrontImagePath) ? "" : "Front";
                        ws.Cell(row, 12).Value = string.IsNullOrEmpty(c.BackImagePath) ? "" : "Back";
                        ws.Cell(row, 13).Value = string.IsNullOrEmpty(c.FrontOverlayImagePath) ? "" : "Front";
                        ws.Cell(row, 14).Value = string.IsNullOrEmpty(c.BackOverlayImagePath) ? "" : "Back";

                        // Paths (as text)
                        ws.Cell(row, 15).Value = c.FrontImagePath ?? "";
                        ws.Cell(row, 16).Value = c.BackImagePath ?? "";
                        ws.Cell(row, 17).Value = c.FrontOverlayImagePath ?? "";
                        ws.Cell(row, 18).Value = c.BackOverlayImagePath ?? "";

                        // Make room for thumbnails (fixed height for every card)
                        ws.Row(row).Height = ThumbnailRowHeight;

                        // --------------------
                        // Thumbnails
                        // --------------------
                        // Front thumb in column 19
                        TryAddPicture(ws, c.FrontImagePath, row, 19);

                        // Back thumb in column 20
                        TryAddPicture(ws, c.BackImagePath, row, 20);

                        row++;
                    }

                    workbook.SaveAs(fullPath);
                }

                return fullPath;
            });
        }

        /// <summary>
        /// Adds a small, scaled picture to the given worksheet/cell if the path exists.
        /// Safe no-op if path is null, empty, http(s), or file missing.
        /// </summary>
        private static void TryAddPicture(IXLWorksheet ws, string? imagePath, int row, int column)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
                return;

            // We only embed local files. Remote http(s) images are skipped.
            if (imagePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                if (!File.Exists(imagePath))
                    return;

                var cell = ws.Cell(row, column);

                // Create the picture anchored to the cell
                var pic = ws.AddPicture(imagePath).MoveTo(cell);

                // Explicit thumbnail dimensions (in pixels).
                // These are ints now, matching the property types.
                const int ThumbWidth = 70;
                const int ThumbHeight = 90;

                pic.Width = ThumbWidth;
                pic.Height = ThumbHeight;
            }
            catch
            {
                // Swallow any image issues so the export still succeeds.
                // If you want logging later, you can plug it in here.
            }
        }

    }
}
