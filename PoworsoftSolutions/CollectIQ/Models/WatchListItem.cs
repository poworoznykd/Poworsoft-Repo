/*
* FILE            : WatchListItem.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Stores marketplace watch-list entries for future buying features.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a user's watched marketplace listing.
    /// </summary>
    public sealed class WatchListItem : BaseModel
    {
        [Indexed]
        public string UserAccountId { get; set; } = string.Empty;

        [Indexed]
        public string MarketplaceListingId { get; set; } = string.Empty;
    }
}
