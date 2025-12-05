using CollectIQ.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CollectIQ.Interfaces
{
    public interface ICardRepository
    {
        Task SaveAsync(Card card);
        Task<Card?> GetByIdAsync(string id);
        Task<List<Card>> GetAllAsync();
    }
}
