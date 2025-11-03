/*
* FILE: Card.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-28
* UPDATED: 2025-10-29
* DESCRIPTION:
*     Represents a collectible card record within the user’s collection,
*     including identifiers, grading details, and image paths.
*     Implements SQLite indexing and adheres to SET Coding Standards Rev 1.11.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a single collectible card within a collection.
    /// </summary>
    public sealed class Card : BaseEntity
    {
        // === Collection Metadata ===
        [Indexed]
        public string CollectionId { get; set; } = string.Empty;

        // === Identification ===
        [Indexed]
        public string Name { get; set; } = string.Empty;

        [Indexed]
        public string Player { get; set; } = string.Empty;

        [Indexed]
        public string Team { get; set; } = string.Empty;

        public int Year { get; set; }

        public string Set { get; set; } = string.Empty;

        public string Number { get; set; } = string.Empty;

        // === Grading ===
        public string GradeCompany { get; set; } = "Raw";

        public double? Grade { get; set; }

        // === Financial ===
        public decimal? PurchasePrice { get; set; }   // currency-safe
        public decimal? EstimatedValue { get; set; }  // currency-safe

        // === Images ===
        /// <summary>
        /// Path to the front image of the card (primary photo).
        /// </summary>
        public string FrontImagePath { get; set; } = string.Empty;

        /// <summary>
        /// Path to the back image of the card (secondary photo).
        /// </summary>
        public string BackImagePath { get; set; } = string.Empty;
    }
}
