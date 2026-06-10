/*
* FILE            : FavoriteItem.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Stores user favorites across collections, cards, and marketplace listings.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a user favorite for any supported CollectIQ entity.
    /// </summary>
    public sealed class FavoriteItem : BaseModel
    {
        [Indexed]
        public string UserAccountId { get; set; } = string.Empty;

        public string EntityType { get; set; } = string.Empty;

        [Indexed]
        public string EntityId { get; set; } = string.Empty;
    }
}
