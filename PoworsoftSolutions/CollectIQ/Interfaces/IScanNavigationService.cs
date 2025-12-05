//
//  FILE            : IScanNavigationService.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-12-05
//  DESCRIPTION     :
//      Navigation abstraction for ScanPage workflows.
//

using System.Threading.Tasks;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Encapsulates navigation targets reachable from ScanPage.
    /// </summary>
    public interface IScanNavigationService
    {
        /// <summary>
        /// Navigates to the CardPage for manual entry.
        /// </summary>
        Task NavigateToCardPageAsync();

        /// <summary>
        /// Navigates back to CardPage and supplies captured front/back paths.
        /// </summary>
        Task NavigateToCardPageWithImagesAsync(string frontPath, string backPath);

        /// <summary>
        /// Navigates to the eBay search workflow with the front image path.
        /// </summary>
        Task NavigateToEbaySearchWithImageAsync(string frontPath);
    }
}
