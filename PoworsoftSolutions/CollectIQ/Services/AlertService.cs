/*
 * FILE         : AlertService.cs
 * PROJECT      : CollectIQ (Mobile Application)
 * PROGRAMMER   : Darryl Poworoznyk
 * FIRST VERSION: 2025-12-04
 * DESCRIPTION  :
 *   Concrete implementation of IAlertService using MAUI
 *   DisplayAlert on the current MainPage.
 */

using System.Threading.Tasks;
using CollectIQ.Interfaces;
using Microsoft.Maui.Controls;

namespace CollectIQ.Services
{
    /// <summary>
    /// Shows simple message dialogs using the current MainPage.
    /// </summary>
    public class AlertService : IAlertService
    {
        /// <inheritdoc />
        public Task ShowMessageAsync(string title, string message, string cancel)
        {
            Page? page = Application.Current?.MainPage;
            if (page == null)
            {
                return Task.CompletedTask;
            }

            return page.DisplayAlert(title, message, cancel);
        }
    }
}
