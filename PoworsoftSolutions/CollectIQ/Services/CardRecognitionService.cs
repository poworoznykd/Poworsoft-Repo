/*
* FILE: CardRecognitionService.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-10-25
* DESCRIPTION:
*     Mock card-recognition service used during early CollectIQ
*     development.  In production, this will be replaced by a
*     machine-vision component capable of identifying cards from
*     captured images and matching them against online listings.
*/

using System;
using System.Threading.Tasks;
using CollectIQ.Models;

namespace CollectIQ.Services
{
    /// <summary>
    /// Simulates the process of identifying a sports card from an image.
    /// </summary>
    public sealed class CardRecognitionService
    {
        /// <summary>
        /// Performs mock recognition and returns a populated <see cref="Card"/>.
        /// </summary>
        /// <param name="imagePath">Local file path of the captured image.</param>
        /// <returns>Recognized card object (mock data).</returns>
        public async Task<Card> RecognizeCardAsync(string imagePath)
        {
            await Task.Delay(1000);   // simulate processing latency

            return new Card
            {
                Id = Guid.NewGuid().ToString(),
                Name = "2020 Select Silver Prizm #66 – Patrick Mahomes II",
                Player = "Patrick Mahomes II",
                Team = "Kansas City Chiefs",
                Year = 2020,
                Set = "Select Silver Prizm",
                Number = "66",
                GradeCompany = "Raw",
                Grade = null,
                PurchasePrice = 0,
                EstimatedValue = 285.50m,
                PhotoPath = imagePath
            };
        }
    }
}
