/*
* FILE            : CollectionCard.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Represents a specific card owned inside a collection. This separates
*     the card definition from the user's owned copy for future sharing and selling.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a specific owned copy of a card inside a collection.
    /// </summary>
    public sealed class CollectionCard : BaseModel
    {
        [Indexed]
        public string CollectionId { get; set; } = string.Empty;

        [Indexed]
        public string CardId { get; set; } = string.Empty;

        [Indexed]
        public string OwnerUserAccountId { get; set; } = string.Empty;

        public int Quantity { get; set; } = 1;

        public decimal? PurchasePrice { get; set; }

        public decimal? EstimatedValue { get; set; }

        public string Condition { get; set; } = string.Empty;

        public string Notes { get; set; } = string.Empty;

        public bool IsFavorite { get; set; }

        public DateTime? AcquiredUtc { get; set; }
    }
}
