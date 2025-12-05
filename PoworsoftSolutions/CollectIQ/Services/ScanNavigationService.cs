//
//  FILE            : ScanNavigationService.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-12-05
//  DESCRIPTION     :
//      Concrete navigation service for ScanPage workflows.
//

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CollectIQ.Interfaces;
using CollectIQ.Utilities;
using CollectIQ.Views;
using Microsoft.Maui.Controls;

namespace CollectIQ.Services
{
    public class ScanNavigationService : IScanNavigationService
    {
        public async Task NavigateToCardPageAsync()
        {
            // CardPage is a global route, not a Shell item – use relative navigation.
            await Shell.Current.GoToAsync(nameof(CardPage));
        }


        public async Task NavigateToCardPageWithImagesAsync(string frontPath, string backPath)
        {
            var resultData = new Dictionary<string, string>
            {
                { "FrontPath", frontPath },
                { "BackPath", backPath }
            };

            NavigationCache.Set(nameof(CardPage), resultData);

            await Shell.Current.GoToAsync("..");
        }

        public async Task NavigateToEbaySearchWithImageAsync(string frontPath)
        {
            string encodedPath = Uri.EscapeDataString(frontPath);

            await Shell.Current.GoToAsync(
                $"//{nameof(EbaySearchPage)}?frontPath={encodedPath}");
        }
    }
}
