/*
* FILE            : IUserRepository.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Defines user/account repository operations.
*/

using CollectIQ.Models;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Defines user account and profile repository operations.
    /// </summary>
    public interface IUserRepository
    {
        Task<UserAccount?> GetAccountByEmailAsync(string email);
        Task<UserProfile?> GetProfileByEmailAsync(string email);
        Task SaveAccountAsync(UserAccount account);
        Task SaveProfileAsync(UserProfile profile);
    }
}
