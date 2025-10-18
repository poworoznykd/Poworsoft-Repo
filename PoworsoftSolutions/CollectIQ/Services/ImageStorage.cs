//
//  FILE            : ImageStorage.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : <Your Name>
//  FIRST VERSION   : 2025-10-18
//  DESCRIPTION     :
//      Saves images to the app's local storage directory.
//

using System;
using System.IO;
using System.Threading.Tasks;
using CollectIQ.Interfaces;
using Microsoft.Maui.Storage;

namespace CollectIQ.Services
{
    /// <summary>
    /// Stores image files under AppDataDirectory.
    /// </summary>
    public class ImageStorage : IImageStorage
    {
        /// <summary>
        /// Saves a stream to a local file and returns the absolute path.
        /// </summary>
        /// <param name="input">The input stream to write.</param>
        /// <param name="extension">Desired file extension.</param>
        /// <returns>Absolute file path of the saved image.</returns>
        public async Task<string> SaveAsync(Stream input, string extension = ".jpg")
        {
            string filename = $"{Guid.NewGuid():N}{extension}";
            string path = Path.Combine(FileSystem.AppDataDirectory, filename);

            using FileStream output = File.OpenWrite(path);
            await input.CopyToAsync(output);
            await output.FlushAsync();

            return path;
        }
    }
}
