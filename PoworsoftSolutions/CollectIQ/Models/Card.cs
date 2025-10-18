/*
 *  FILE            : Card.cs
 *  PROJECT         : CollectIQ (Mobile)
 *  PROGRAMMER      : <your name>
 *  FIRST VERSION   : 2025-10-18
 *  DESCRIPTION     :
 *    Data model representing a single sports card in the user's inventory.
 *    Fields cover common identity attributes (player, set, year, number),
 *    optional grading info, pricing, and timestamps for auditing.
 */

namespace CollectIQ.Models
{
    /*
     *  NAME          : Card
     *  PURPOSE       :
     *    Represents a sports card item persisted in the local SQLite database.
     *    Instances of this class are stored using sqlite-net-pcl.
     */
    public class Card
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public int Id { get; set; }

        // Identity
        public string Player { get; set; } = string.Empty;
        public string Year { get; set; } = string.Empty;
        public string Set { get; set; } = string.Empty;
        public string Number { get; set; } = string.Empty;  // e.g., "#307"

        // Optional details
        public string Team { get; set; } = string.Empty;
        public string GradeCompany { get; set; } = string.Empty;  // PSA/BGS/SGC/RAW
        public string Grade { get; set; } = string.Empty;         // e.g., 10, 9.5, AUTH
        public decimal PurchasePrice { get; set; } = 0m;

        // Optional media (future use: photo path)
        public string? PhotoPath { get; set; }

        // Audit
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
