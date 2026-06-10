/*
* FILE            : CollectionInvite.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Stores pending and completed collection sharing invitations.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents an invitation to join a card collection.
    /// </summary>
    public sealed class CollectionInvite : BaseModel
    {
        [Indexed]
        public string CollectionId { get; set; } = string.Empty;

        [Indexed]
        public string InvitedEmailNormalized { get; set; } = string.Empty;

        public string InvitedEmail { get; set; } = string.Empty;

        public string InvitedByUserAccountId { get; set; } = string.Empty;

        public string InviteTokenHash { get; set; } = string.Empty;

        public string Status { get; set; } = "Pending";

        public DateTime? ExpiresUtc { get; set; }

        public DateTime? AcceptedUtc { get; set; }
    }
}
