// ===============================================
// FILE: CollectionExportServiceBase.cs
// PROJECT: CollectIQ (Mobile Application)
// PROGRAMMER: Darryl Poworoznyk
// FIRST VERSION: 2025-12-24
// DESCRIPTION:
//     Abstract base class for all collection export services
//     (Excel, PDF, future formats).
//
//     Responsibilities:
//     - Create the export folder/package
//     - Copy card images & overlays into the package
//     - Generate thumbnails
//     - Build a per-card ExportAsset map used by concrete exporters
// ===============================================

using CollectIQ.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace CollectIQ.Services
{
    public abstract class CollectionExportServiceBase
    {
        protected CollectionExportServiceBase()
        {
        }

        /// <summary>
        /// Creates the root export folder and standard subfolders.
        /// </summary>
        protected string CreateExportFolder()
        {
            string root = Path.Combine(FileSystem.CacheDirectory, "CollectIQ_Exports");
            string folderName = $"CollectIQ_Export_{DateTime.Now:yyyyMMdd_HHmmss}";
            string exportFolder = Path.Combine(root, folderName);

            Directory.CreateDirectory(exportFolder);
            Directory.CreateDirectory(Path.Combine(exportFolder, "images"));
            Directory.CreateDirectory(Path.Combine(exportFolder, "thumbs"));

            return exportFolder;
        }

        /// <summary>
        /// Copies images/overlays for each card into the export folder
        /// and builds a lookup of ExportAsset objects keyed by Card.Id.
        /// </summary>
        protected async Task<Dictionary<string, ExportAsset>> BuildExportAssetsAsync(
            string exportFolder,
            IList<Card> cards)
        {
            var assets = new Dictionary<string, ExportAsset>();

            string imagesFolder = Path.Combine(exportFolder, "images");
            string thumbsFolder = Path.Combine(exportFolder, "thumbs");

            foreach (Card card in cards)
            {
                string cardId = string.IsNullOrWhiteSpace(card.Id)
                    ? Guid.NewGuid().ToString()
                    : card.Id;

                var asset = new ExportAsset
                {
                    CardId = cardId
                };

                asset.FrontImagePath = CopyIfExists(card.FrontImagePath, imagesFolder, $"{cardId}_front");
                asset.BackImagePath = CopyIfExists(card.BackImagePath, imagesFolder, $"{cardId}_back");
                asset.FrontOverlayImagePath = CopyIfExists(card.FrontOverlayImagePath, imagesFolder, $"{cardId}_front_overlay");
                asset.BackOverlayImagePath = CopyIfExists(card.BackOverlayImagePath, imagesFolder, $"{cardId}_back_overlay");

                // Generate thumbnail (prefer front image, fallback to back)
                string? thumbSource = asset.FrontImagePath ?? asset.BackImagePath;
                if (!string.IsNullOrWhiteSpace(thumbSource) && File.Exists(thumbSource))
                {
                    string thumbPath = Path.Combine(thumbsFolder, $"{cardId}_thumb.jpg");
                    asset.ThumbnailImagePath = await CreateThumbnailAsync(thumbSource, thumbPath);
                }

                // Relative paths for hyperlinks
                asset.FrontImageRelativePath = ToRelative(exportFolder, asset.FrontImagePath);
                asset.BackImageRelativePath = ToRelative(exportFolder, asset.BackImagePath);
                asset.FrontOverlayRelativePath = ToRelative(exportFolder, asset.FrontOverlayImagePath);
                asset.BackOverlayRelativePath = ToRelative(exportFolder, asset.BackOverlayImagePath);
                asset.ThumbnailRelativePath = ToRelative(exportFolder, asset.ThumbnailImagePath);

                assets[cardId] = asset;
            }

            return assets;
        }

        /// <summary>
        /// Concrete exporters must implement thumbnail creation.
        /// Keeps image tech (SkiaSharp, ImageSharp, etc.) out of the base class.
        /// </summary>
        protected abstract Task<string?> CreateThumbnailAsync(string sourcePath, string destPath);

        private static string? CopyIfExists(string? sourcePath, string destFolder, string baseName)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return null;
            }

            try
            {
                string ext = Path.GetExtension(sourcePath);
                if (string.IsNullOrWhiteSpace(ext))
                {
                    ext = ".jpg";
                }

                string destPath = Path.Combine(destFolder, baseName + ext);
                File.Copy(sourcePath, destPath, true);

                return destPath;
            }
            catch
            {
                return null;
            }
        }

        private static string? ToRelative(string exportFolder, string? fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return null;
            }

            try
            {
                return Path.GetRelativePath(exportFolder, fullPath)
                           .Replace('\\', '/');
            }
            catch
            {
                return null;
            }
        }
    }
}
