//
//  FILE            : IAlertService.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-12-04
//  DESCRIPTION     :
//      Abstraction for showing alerts from view models
//      without referencing Page / DisplayAlert directly.
//

using System.Threading.Tasks;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Abstraction over user alert dialogs.
    /// </summary>
    public interface IAlertService
    {
        /// <summary>
        /// Shows a simple alert dialog with a single dismiss button.
        /// </summary>
        /// <param name="title">Alert title.</param>
        /// <param name="message">Alert message.</param>
        /// <param name="cancel">Button text.</param>
        Task ShowMessageAsync(string title, string message, string cancel);
    }
}
