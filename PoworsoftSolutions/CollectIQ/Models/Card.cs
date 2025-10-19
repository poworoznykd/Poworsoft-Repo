using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a single collectible card within a collection.
    /// </summary>
    public sealed class Card : BaseEntity
    {
        [Indexed] public string CollectionId { get; set; } = string.Empty;

        [Indexed] public string Name { get; set; } = string.Empty;

        [Indexed] public string Player { get; set; } = string.Empty;

        [Indexed] public string Team { get; set; } = string.Empty;

        public int Year { get; set; }

        public string Set { get; set; } = string.Empty;

        public string Number { get; set; } = string.Empty;

        public string GradeCompany { get; set; } = "Raw";

        public double? Grade { get; set; }

        public decimal? PurchasePrice { get; set; }  // ✅ currency-safe

        public decimal? EstimatedValue { get; set; }  // ✅ currency-safe

        public string PhotoPath { get; set; } = string.Empty;
    }
}
