// ===============================================
// FILE: ExportResult.cs
// PROJECT: CollectIQ (Mobile Application)
// PROGRAMMER: Darryl Poworoznyk
// FIRST VERSION: 2025-12-24
// DESCRIPTION:
//     Represents the outcome of an export operation (Excel, PDF, etc.).
//     Used by export services to report file paths and status back to the UI.
// ===============================================

using System;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents the result of generating an export package.
    /// </summary>
    public sealed class ExportResult
    {
        /// <summary>
        /// Root folder where the export package was created.
        /// Example: .../CollectIQ_Exports/CollectIQ_Export_20251224_103015/
        /// </summary>
        public string ExportFolderPath { get; set; } = string.Empty;

        /// <summary>
        /// Full path to the main exported file (e.g., .xlsx or .pdf).
        /// </summary>
        public string MainFilePath { get; set; } = string.Empty;

        /// <summary>
        /// File name of the main exported file.
        /// </summary>
        public string MainFileName { get; set; } = string.Empty;

        /// <summary>
        /// Indicates whether the export completed successfully.
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// Optional user-facing or log message.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Optional error details if the export failed.
        /// </summary>
        public string Error { get; set; } = string.Empty;

        /// <summary>
        /// Debug-friendly summary.
        /// </summary>
        public override string ToString()
        {
            return $"Success={Success}, File='{MainFileName}', Path='{MainFilePath}'";
        }
    }
}
