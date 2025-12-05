/*
 * FILE         : BrowserService.cs
 * PROJECT      : CollectIQ (Mobile Application)
 * PROGRAMMER   : Darryl Poworoznyk
 * FIRST VERSION: 2025-12-04
 * DESCRIPTION  :
 *   Concrete implementation of IBrowserService using the
 *   MAUI Essentials Browser API.
 */

using System;
using System.Threading.Tasks;
using CollectIQ.Interfaces;

namespace CollectIQ.Services
{
    /// <summary>
    /// Opens external URLs using the system browser.
    /// </summary>
    public class BrowserService : IBrowserService
    {
        /// <inheritdoc />
        public async Task OpenAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                await Microsoft.Maui.ApplicationModel.Browser.Default.OpenAsync(
                    url,
                    Microsoft.Maui.ApplicationModel.BrowserLaunchMode.SystemPreferred);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[BrowserService] Failed to open URL '{url}': {ex.Message}");
            }
        }
    }
}
