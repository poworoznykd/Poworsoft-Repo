/*
* FILE            : CollectionMember.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Represents a user's membership and permissions inside a shared collection.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a user member of a collection.
    /// </summary>
    public sealed class CollectionMember : BaseModel
    {
        [Indexed]
        public string CollectionId { get; set; } = string.Empty;

        [Indexed]
        public string UserAccountId { get; set; } = string.Empty;

        public string CollectionRole { get; set; } = "Owner";

        public bool CanView { get; set; } = true;

        public bool CanAddCards { get; set; } = true;

        public bool CanEditCards { get; set; } = true;

        public bool CanDeleteCards { get; set; } = true;

        public bool CanInvite { get; set; } = true;
    }
}
