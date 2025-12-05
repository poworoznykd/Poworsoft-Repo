/*
* FILE            : Grading.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2025-12-09
* DESCRIPTION     :
*     Represents grading information for a card, including optional subgrades.
*/

namespace CollectIQ.Models.Domain.Entities
{
    /// <summary>
    /// Represents a grading company’s evaluation of a collectible card.
    /// </summary>
    public sealed class Grading
    {
        public string Company { get; set; } = string.Empty;     // PSA, BGS, TAG, etc.
        public double? Grade { get; set; }

        // Optional subgrades
        public double? Corners { get; set; }
        public double? Edges { get; set; }
        public double? Surface { get; set; }
        public double? Centering { get; set; }
    }
}
