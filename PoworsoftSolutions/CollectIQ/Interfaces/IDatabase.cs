//
//  FILE            : IDatabase.cs
//  PROJECT         : CollectIQ (Mobile)
//  PROGRAMMER      : <your name>
//  FIRST VERSION   : 2025-10-18
//  DESCRIPTION     :
//      Interface contract for the CollectIQ data access layer.
//

using System.Collections.Generic;
using System.Threading.Tasks;
using CollectIQ.Models;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Contract for Card persistence operations.
    /// </summary>
    public interface IDatabase
    {
        /// <summary>
        /// Ensures the underlying storage is initialized and ready for use.
        /// </summary>
        /// <returns>Task that completes when initialization is finished.</returns>
        Task InitAsync();

        /// <summary>
        /// Retrieves cards ordered by most recent update, optionally filtered.
        /// </summary>
        /// <param name="query">Optional search text for Player, Set, Team, or Number.</param>
        /// <returns>List of matching cards.</returns>
        Task<List<Card>> GetCardsAsync(string query = null);

        /// <summary>
        /// Inserts a new card when Id == 0; otherwise updates the existing row.
        /// </summary>
        /// <param name="card">Card entity to insert or update.</param>
        /// <returns>The saved card (with Id populated if newly inserted).</returns>
        Task<Card> UpsertAsync(Card card);

        /// <summary>
        /// Deletes the specified card.
        /// </summary>
        /// <param name="card">Card entity to remove.</param>
        /// <returns>Number of rows affected.</returns>
        Task<int> DeleteAsync(Card card);

        /// <summary>
        /// Absolute path to the backing store (for diagnostics).
        /// </summary>
        string DbPath { get; }
    }
}
