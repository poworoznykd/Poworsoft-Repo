//
//  FILE            : ICameraCaptureService.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-12-05
//  DESCRIPTION     :
//      Abstraction over camera capture for ScanPage.
//

using System;
using System.IO;
using System.Threading.Tasks;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Contract for capturing a single image frame from the camera.
    /// </summary>
    public interface ICameraCaptureService
    {
        /// <summary>
        /// Captures a still image from the camera.
        /// </summary>
        /// <param name="timeout">
        /// Maximum amount of time to wait for the capture before cancelling.
        /// </param>
        /// <returns>
        /// Stream containing the captured image data, or null if capture failed.
        /// </returns>
        Task<Stream?> CaptureImageAsync(TimeSpan timeout);
    }
}
