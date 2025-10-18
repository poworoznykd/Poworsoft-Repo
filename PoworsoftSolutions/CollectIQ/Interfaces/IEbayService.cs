//
//  FILE            : IEbayService.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : <Your Name>
//  FIRST VERSION   : 2025-10-18
//  DESCRIPTION     :
//      Abstraction for querying marketplace data (eBay).
//

using System.Threading.Tasks;
using CollectIQ.Models;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Contract for marketplace search capabilities (eBay).
    /// </summary>
    public interface IEbayService
    {
        /// <summary>
        /// Finds the best-matching listing on eBay for a free-text description.
        /// </summary>
        /// <param name="query">User-entered description (player, year, set, number).</param>
        /// <returns>The best match listing if found; otherwise null.</returns>
        Task<EbayListing?> GetBestMatchAsync(string query);
    }
}
