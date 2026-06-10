/*
* FILE            : CardImage.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Stores image metadata for collection cards. This supports local paths now
*     and future cloud URLs, thumbnails, hashing, and synchronization.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents an image belonging to a card in a collection.
    /// </summary>
    public sealed class CardImage : BaseModel
    {
        [Indexed]
        public string CollectionCardId { get; set; } = string.Empty;

        [Indexed]
        public string CardId { get; set; } = string.Empty;

        public string ImageType { get; set; } = CardImageTypes.Front;

        public string LocalPath { get; set; } = string.Empty;

        public string RemoteUrl { get; set; } = string.Empty;

        public string ThumbnailLocalPath { get; set; } = string.Empty;

        public string ThumbnailRemoteUrl { get; set; } = string.Empty;

        public string ContentHash { get; set; } = string.Empty;

        public int Width { get; set; }

        public int Height { get; set; }
    }

    /// <summary>
    /// Supported card image type values.
    /// </summary>
    public static class CardImageTypes
    {
        public const string Front = "Front";
        public const string Back = "Back";
        public const string FrontOverlay = "FrontOverlay";
        public const string BackOverlay = "BackOverlay";
        public const string Thumbnail = "Thumbnail";
    }
}
