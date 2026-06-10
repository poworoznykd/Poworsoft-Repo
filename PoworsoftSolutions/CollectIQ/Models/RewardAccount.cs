/*
* FILE            : RewardAccount.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Stores reward balances for future CollectIQ engagement features.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a user's rewards account.
    /// </summary>
    public sealed class RewardAccount : BaseModel
    {
        [Indexed(Unique = true)]
        public string UserAccountId { get; set; } = string.Empty;

        public int CurrentPoints { get; set; }

        public int LifetimePoints { get; set; }
    }
}
