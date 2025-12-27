// ===============================================
// FILE: ExportAsset.cs
// PROJECT: CollectIQ (Mobile Application)
// PROGRAMMER: Darryl Poworoznyk
// FIRST VERSION: 2025-12-24
// DESCRIPTION:
//     Represents all exported file paths for a single card during an export.
//     This model is used by Excel/PDF exporters to reference images, overlays,
//     thumbnails, and relative links inside the export package.
// ===============================================

namespace CollectIQ.Models
{
    /// <summary>
    /// Holds exported asset paths for a single card.
    /// </summary>
    public sealed class ExportAsset
    {
        /// <summary>
        /// Card identifier (matches Card.Id).
        /// </summary>
        public string CardId { get; set; } = string.Empty;

        // ===== Absolute paths inside export package =====

        public string? FrontImagePath { get; set; }
        public string? BackImagePath { get; set; }
        public string? FrontOverlayImagePath { get; set; }
        public string? BackOverlayImagePath { get; set; }
        public string? ThumbnailImagePath { get; set; }

        // ===== Relative paths (used for Excel/PDF links) =====

        public string? FrontImageRelativePath { get; set; }
        public string? BackImageRelativePath { get; set; }
        public string? FrontOverlayRelativePath { get; set; }
        public string? BackOverlayRelativePath { get; set; }
        public string? ThumbnailRelativePath { get; set; }

        /// <summary>
        /// True if at least one visual asset exists for the card.
        /// </summary>
        public bool HasAnyImage =>
            !string.IsNullOrWhiteSpace(FrontImagePath) ||
            !string.IsNullOrWhiteSpace(BackImagePath) ||
            !string.IsNullOrWhiteSpace(FrontOverlayImagePath) ||
            !string.IsNullOrWhiteSpace(BackOverlayImagePath);
    }
}
