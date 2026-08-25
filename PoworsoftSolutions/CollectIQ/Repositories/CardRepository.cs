/*
* FILE            : CardRepository.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-02-10
* UPDATED         : 2026-06-08
* DESCRIPTION     :
*     Repository for card records. This now depends on IDatabase instead of a
*     raw SQLite connection so it uses the same initialized database/migration path
*     as the rest of CollectIQ.
*/

using CollectIQ.Interfaces;
using CollectIQ.Models;
using System.Linq;
using System.Collections.Generic;

namespace CollectIQ.Repositories
{
    /// <summary>
    /// Provides repository methods for card records.
    /// </summary>
    public sealed class CardRepository : ICardRepository
    {
        private readonly IDatabase database;

        /// <summary>
        /// Initializes a new instance of the CardRepository class.
        /// </summary>
        /// <param name="database">The local database abstraction.</param>
        public CardRepository(IDatabase database)
        {
            this.database = database;
        }

        /// <summary>
        /// Saves a card record.
        /// </summary>
        /// <param name="card">The card to save.</param>
        public async Task SaveAsync(Card card)
        {
            if (card == null)
            {
                throw new ArgumentNullException(nameof(card));
            }

            card.UpdatedUtc = DateTime.UtcNow;
            List<Card> visibleCards = await database.GetAllCardsAsync();
            bool exists = visibleCards.Any(item => item.Id == card.Id);

            if (exists)
            {
                await database.UpdateCardAsync(card);
            }
            else
            {
                await database.AddCardAsync(card);
            }
        }

        /// <summary>
        /// Gets a card by ID.
        /// </summary>
        /// <param name="id">The card ID.</param>
        /// <returns>The matching card, or null when none exists.</returns>
        public async Task<Card?> GetByIdAsync(string id)
        {
            List<Card> visibleCards = await database.GetAllCardsAsync();
            return visibleCards.FirstOrDefault(card => card.Id == id);
        }

        /// <summary>
        /// Gets all cards.
        /// </summary>
        /// <returns>A list of cards.</returns>
        public Task<List<Card>> GetAllAsync()
        {
            return database.GetAllCardsAsync();
        }
    }
}
