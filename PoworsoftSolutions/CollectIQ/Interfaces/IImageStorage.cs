//
//  FILE            : IImageStorage.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : <Your Name>
//  FIRST VERSION   : 2025-10-18
//  DESCRIPTION     :
//      Abstraction for saving and retrieving local image files.
//

using System.IO;
using System.Threading.Tasks;

namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Contract for image file persistence.
    /// </summary>
    public interface IImageStorage
    {
        /// <summary>
        /// Saves a stream to a local file and returns the absolute file path.
        /// </summary>
        /// <param name="input">Input data stream.</param>
        /// <param name="extension">File extension (e.g., ".jpg").</param>
        /// <returns>Absolute file path of the saved image.</returns>
        Task<string> SaveAsync(Stream input, string extension = ".jpg");
    }
}
