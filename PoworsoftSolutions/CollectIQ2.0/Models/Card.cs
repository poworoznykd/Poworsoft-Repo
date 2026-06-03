/******************************************************************************
 *
 * FILE          : Card.cs
 * PROJECT       : CollectIQ
 * PROGRAMMER    : Darryl Poworoznyk
 * FIRST VERSION : 2026-06-02
 *
 * DESCRIPTION:
 *
 * Represents a collectible sports card or trading card.
 *
 * This is the central model used by CollectIQ for local storage, offline
 * card management, synchronization, and future online marketplace features.
 *
 *****************************************************************************/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a collectible card.
    /// </summary>
    [Table("Card")]
    public class Card
    {
        /// <summary>
        /// Gets or sets the local SQLite card identifier.
        /// </summary>
        [PrimaryKey, AutoIncrement]
        public int LocalId { get; set; }

        /// <summary>
        /// Gets or sets the server-side card identifier.
        /// </summary>
        public string ServerId { get; set; }

        /// <summary>
        /// Gets or sets the local collection identifier.
        /// </summary>
        public int? CollectionLocalId { get; set; }

        /// <summary>
        /// Gets or sets the card title.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the player name.
        /// </summary>
        public string PlayerName { get; set; }

        /// <summary>
        /// Gets or sets the card year.
        /// </summary>
        public string Year { get; set; }

        /// <summary>
        /// Gets or sets the card brand.
        /// </summary>
        public string Brand { get; set; }

        /// <summary>
        /// Gets or sets the set name.
        /// </summary>
        public string SetName { get; set; }

        /// <summary>
        /// Gets or sets the card number.
        /// </summary>
        public string CardNumber { get; set; }

        /// <summary>
        /// Gets or sets the sport.
        /// </summary>
        public string Sport { get; set; }

        /// <summary>
        /// Gets or sets the team.
        /// </summary>
        public string Team { get; set; }

        /// <summary>
        /// Gets or sets the league.
        /// </summary>
        public string League { get; set; }

        /// <summary>
        /// Gets or sets the grading company.
        /// </summary>
        public string GradingCompany { get; set; }

        /// <summary>
        /// Gets or sets the card grade.
        /// </summary>
        public string Grade { get; set; }

        /// <summary>
        /// Gets or sets the certification number.
        /// </summary>
        public string CertificationNumber { get; set; }

        /// <summary>
        /// Gets or sets the front image path.
        /// </summary>
        public string FrontImagePath { get; set; }

        /// <summary>
        /// Gets or sets the back image path.
        /// </summary>
        public string BackImagePath { get; set; }

        /// <summary>
        /// Gets or sets the estimated card value.
        /// </summary>
        public decimal? EstimatedValue { get; set; }

        /// <summary>
        /// Gets or sets the purchase price.
        /// </summary>
        public decimal? PurchasePrice { get; set; }

        /// <summary>
        /// Gets or sets the asking price if the card is listed for sale.
        /// </summary>
        public decimal? AskingPrice { get; set; }

        /// <summary>
        /// Gets or sets the sold price.
        /// </summary>
        public decimal? SoldPrice { get; set; }

        /// <summary>
        /// Gets or sets card condition notes.
        /// </summary>
        public string ConditionNotes { get; set; }

        /// <summary>
        /// Gets or sets general notes.
        /// </summary>
        public string Notes { get; set; }

        /// <summary>
        /// Gets or sets whether this card is favourited.
        /// </summary>
        public bool IsFavorite { get; set; }

        /// <summary>
        /// Gets or sets whether this card is for sale.
        /// </summary>
        public bool IsForSale { get; set; }

        /// <summary>
        /// Gets or sets whether this card has sold.
        /// </summary>
        public bool IsSold { get; set; }

        /// <summary>
        /// Gets or sets whether this card is archived.
        /// </summary>
        public bool IsArchived { get; set; }

        /// <summary>
        /// Gets or sets the current synchronization status.
        /// </summary>
        public string SyncStatus { get; set; }

        /// <summary>
        /// Gets or sets the last synchronized date.
        /// </summary>
        public string LastSyncedAt { get; set; }

        /// <summary>
        /// Gets or sets whether this card has been deleted locally.
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// Gets or sets the record creation date.
        /// </summary>
        public string CreatedAt { get; set; }

        /// <summary>
        /// Gets or sets the record update date.
        /// </summary>
        public string UpdatedAt { get; set; }
    }
}