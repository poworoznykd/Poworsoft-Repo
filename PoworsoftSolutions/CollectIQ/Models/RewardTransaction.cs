/*
* FILE            : RewardTransaction.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Stores individual reward transactions for future rewards functionality.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a rewards point transaction.
    /// </summary>
    public sealed class RewardTransaction : BaseModel
    {
        [Indexed]
        public string RewardAccountId { get; set; } = string.Empty;

        public int Points { get; set; }

        public string TransactionType { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string RelatedEntityType { get; set; } = string.Empty;

        public string RelatedEntityId { get; set; } = string.Empty;
    }
}
