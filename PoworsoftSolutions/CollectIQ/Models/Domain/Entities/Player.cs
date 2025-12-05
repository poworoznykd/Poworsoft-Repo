//
//  FILE            : Player.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-12-05
//  UPDATED         : 2025-12-12
//  DESCRIPTION     :
//      Represents an athlete associated with collectible cards.
//      Includes a HighlightReel which stores structured highlight clips.
//      This is owned by the Player entity, not the Card.
//
//

using CollectIQ.Models.Domain.Entities;

namespace CollectIQ.Domain.Entities
{
    public sealed class Player
    {
        public string FullName { get; set; } = string.Empty;

        public string Sport { get; set; } = string.Empty;

        public string Position { get; set; } = string.Empty;

        public int? HeightCm { get; set; }

        public int? WeightKg { get; set; }

        public string Nationality { get; set; } = string.Empty;

        // Correct ownership: Player owns highlight content.
        public HighlightReel HighlightReel { get; set; } = new HighlightReel();
    }
}
