/*
* FILE            : CardCollection.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Represents a named card collection owned by a user. This table is the
*     foundation for multiple collections, sharing, invites, and marketplace flows.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a user-owned card collection.
    /// </summary>
    public sealed class CardCollection : BaseModel
    {
        [Indexed]
        public string OwnerUserAccountId { get; set; } = string.Empty;

        [Indexed]
        public string Name { get; set; } = "My Collection";

        public string Description { get; set; } = string.Empty;

        public string Visibility { get; set; } = CollectionVisibility.Private;

        public bool IsDefault { get; set; }
    }

    /// <summary>
    /// Supported collection visibility values.
    /// </summary>
    public static class CollectionVisibility
    {
        public const string Private = "Private";
        public const string Shared = "Shared";
        public const string Public = "Public";
    }
}
