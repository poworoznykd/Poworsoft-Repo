using CollectIQ.Models;
using CollectIQ.Interfaces;
using SQLite;

namespace CollectIQ.Repositories
{
    public sealed class CardRepository : ICardRepository
    {
        private readonly SQLiteAsyncConnection _db;

        public CardRepository(SQLiteAsyncConnection db)
        {
            _db = db;
        }

        public async Task SaveAsync(Card card)
        {
            card.UpdatedUtc = DateTime.UtcNow;
            await _db.InsertOrReplaceAsync(card);
        }

        public async Task<Card?> GetByIdAsync(string id)
        {
            return await _db.Table<Card>()
                            .Where(c => c.Id == id)
                            .FirstOrDefaultAsync();
        }

        public async Task<List<Card>> GetAllAsync()
        {
            return await _db.Table<Card>()
                            .OrderByDescending(c => c.CreatedUtc)
                            .ToListAsync();
        }
    }
}
