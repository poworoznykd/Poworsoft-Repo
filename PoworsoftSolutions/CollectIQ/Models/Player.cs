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

using static CollectIQ.Enums.Enums;

namespace CollectIQ.Models
{
    public sealed class Player
    {
        public string FirstName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;

        public string FullName
        {
            get
            {
                string first = (FirstName ?? string.Empty).Trim();
                string last = (LastName ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(first)) return last;
                if (string.IsNullOrWhiteSpace(last)) return first;

                return $"{first} {last}";
            }
            set
            {
                string name = (value ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(name))
                {
                    FirstName = string.Empty;
                    LastName = string.Empty;
                    return;
                }

                // Split on whitespace, remove empty parts
                string[] parts = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length == 1)
                {
                    FirstName = parts[0];
                    LastName = string.Empty;
                    return;
                }

                // First token as FirstName, everything else as LastName (supports middle names)
                FirstName = parts[0];
                LastName = string.Join(" ", parts.Skip(1));
            }
        }

        public string Position { get; set; } = string.Empty;


        // Correct ownership: Player owns highlight content.
        public HighlightReel HighlightReel { get; set; } = new HighlightReel();
    }
}
