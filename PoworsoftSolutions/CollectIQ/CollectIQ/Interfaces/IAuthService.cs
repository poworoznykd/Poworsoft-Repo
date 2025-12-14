//
//  FILE            : IAuthService.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-19
//  DESCRIPTION     :
//      Defines the contract for all authentication service implementations
//      within the CollectIQ mobile application. Supports registration,
//      login, sign-out, and user session verification.
//
using System.Threading.Tasks;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Defines authentication operations used by UI components.
    /// </summary>
    public interface IAuthService
    {
        Task<bool> RegisterAsync(string email, string password);
        Task<bool> LoginAsync(string email, string password);
        Task<bool> SignOutAsync();
        Task<bool> IsSignedInAsync();
        Task<string?> GetCurrentUserEmailAsync();
    }
}
