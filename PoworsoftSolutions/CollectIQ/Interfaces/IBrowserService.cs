//
//  FILE            : IBrowserService.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-12-04
//  DESCRIPTION     :
//      Abstraction for launching external browser URLs from
//      view models without depending on platform APIs directly.
//

using System.Threading.Tasks;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Abstraction over external browser navigation.
    /// </summary>
    public interface IBrowserService
    {
        /// <summary>
        /// Opens the specified URL in the system browser.
        /// </summary>
        /// <param name="url">The URL to open.</param>
        Task OpenAsync(string url);
    }
}
