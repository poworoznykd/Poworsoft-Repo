/*
* FILE: Card.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-28
* UPDATED: 2025-12-01
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
        public string Title { get; set; } = string.Empty;

        [Indexed]
        public string Name { get; set; } = string.Empty;

        [Indexed]
        public string Team { get; set; } = string.Empty;

        public int? Year { get; set; }

        public string Set { get; set; } = string.Empty;

        public string Number { get; set; } = string.Empty;

        // === Grading ===
        public string GradeCompany { get; set; } = "None";

        public double? Grade { get; set; } = null;

        // === Financial ===
        public decimal? PurchasePrice { get; set; }
        public decimal? EstimatedValue { get; set; }

        // === Images ===
        public string FrontImagePath { get; set; } = string.Empty;

        public string BackImagePath { get; set; } = string.Empty;

        // === Insights (JSON serialized) ===
        public string InsightsJson { get; set; } = "{}";


        // ============================================================
        //          NEW OPTIONAL FIELDS (NON-BREAKING)
        // ============================================================

        // --- Sport (Football, Hockey, Basketball, Pokémon, etc.) ---
        public string Sport { get; set; } = string.Empty;

        // --- Parallels & Inserts ---
        public string Parallel { get; set; } = string.Empty;             // Refractor, Pulsar, Silver Prizm, etc.
        public string Subset { get; set; } = string.Empty;               // Fireworks, My House, etc.

        // --- Serial Number (#/99, 10/25) ---
        public string SerialNumber { get; set; } = string.Empty;

        // --- Advanced Grading Details ---
        public string Grader { get; set; } = string.Empty;               // PSA, BGS, CGC, SGC, TAG

        public double? SubgradeCorners { get; set; }
        public double? SubgradeEdges { get; set; }
        public double? SubgradeSurface { get; set; }
        public double? SubgradeCentering { get; set; }
    }
}
