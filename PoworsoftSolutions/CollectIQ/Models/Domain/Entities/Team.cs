/*
* FILE            : Team.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2025-12-09
* DESCRIPTION     :
*     Represents the professional team associated with a card.
*/

namespace CollectIQ.Models.Domain.Entities
{
    /// <summary>
    /// Represents a sports team associated with a player/card.
    /// </summary>
    public sealed class Team
    {
        public string Name { get; set; } = string.Empty;      // e.g., Toronto Maple Leafs
        public string City { get; set; } = string.Empty;      // e.g., Toronto
        public string League { get; set; } = string.Empty;    // e.g., NHL, NBA

        public override string ToString() => $"{City} {Name}";
    }
}
