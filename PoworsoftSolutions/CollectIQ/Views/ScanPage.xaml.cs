//
//  FILE            : ScanPage.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  LAST UPDATED    : 2025-10-26
//  DESCRIPTION     :
//      Handles photo capture, gallery import, and manual search
//      navigation to eBay Search Page.
//
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Media;
using System.IO;

namespace CollectIQ.Views
{
    public partial class ScanPage : ContentPage
    {
        public ScanPage()
        {
            InitializeComponent();
            BindingContext = this;
        }

        public Command CapturePhotoCommand => new(async () =>
        {
            try
            {
                var photo = await MediaPicker.CapturePhotoAsync();
                if (photo == null) return;

                // Save to local folder
                var newPath = Path.Combine(FileSystem.AppDataDirectory, photo.FileName);
                await using var source = await photo.OpenReadAsync();
                await using var dest = File.Create(newPath);
                await source.CopyToAsync(dest);

                // Placeholder detection step
                string cardGuess = Path.GetFileNameWithoutExtension(photo.FileName);
                await Shell.Current.GoToAsync($"{nameof(EbaySearchPage)}?query={Uri.EscapeDataString(cardGuess)}");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Unable to capture photo: {ex.Message}", "OK");
            }
        });

        public Command PickPhotoCommand => new(async () =>
        {
            try
            {
                var photo = await MediaPicker.PickPhotoAsync();
                if (photo != null)
                {
                    string cardGuess = Path.GetFileNameWithoutExtension(photo.FileName);
                    await Shell.Current.GoToAsync($"{nameof(EbaySearchPage)}?query={Uri.EscapeDataString(cardGuess)}");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Unable to select photo: {ex.Message}", "OK");
            }
        });

        public Command ManualSearchCommand => new(async () =>
        {
            var query = ManualEntry?.Text;
            if (!string.IsNullOrWhiteSpace(query))
            {
                await Shell.Current.GoToAsync($"{nameof(EbaySearchPage)}?query={Uri.EscapeDataString(query)}");
            }
            else
            {
                await DisplayAlert("Error", "Please enter a search term first.", "OK");
            }
        });
    }
}
