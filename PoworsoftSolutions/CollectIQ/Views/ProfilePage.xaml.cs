//
//  FILE            : ProfilePage.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-11-23
//  UPDATED         : 2026-01-02
//  DESCRIPTION     :
//      Full Profile page for CollectIQ.
//      Provides a futuristic-styled profile layout and supports updating
//      the user's avatar image from either the camera or the photo library.
//

using CollectIQ.ViewModels;
using Microsoft.Maui.Storage;
using System;
using System.IO;
using System.Threading.Tasks;

namespace CollectIQ.Views
{
    /// <summary>
    /// Interaction logic for the Profile page.
    /// </summary>
    public partial class ProfilePage : ContentPage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProfilePage"/> class.
        /// </summary>
        public ProfilePage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Handles the back tap.
        /// </summary>
        private async void OnBackClicked(object sender, TappedEventArgs e)
        {
            try
            {
                if (Navigation?.NavigationStack?.Count > 1)
                {
                    await Navigation.PopAsync();
                }
                else
                {
                    await Shell.Current.GoToAsync("..");
                }
            }
            catch
            {
                // Avoid crashing if navigation is not available in current hosting mode.
            }
        }

        /// <summary>
        /// Tap on avatar preview = show quick options.
        /// </summary>
        private async void OnAvatarTapped(object sender, TappedEventArgs e)
        {
            string choice = await DisplayActionSheet(
                "Profile Photo",
                "Cancel",
                null,
                "Take Photo",
                "Choose From Photos");

            if (choice == "Take Photo")
            {
                await TakeAvatarPhotoAsync();
            }
            else if (choice == "Choose From Photos")
            {
                await PickAvatarPhotoAsync();
            }
        }

        /// <summary>
        /// Camera icon button click.
        /// </summary>
        private async void OnTakeAvatarPhoto(object sender, EventArgs e)
        {
            await TakeAvatarPhotoAsync();
        }

        /// <summary>
        /// Folder icon button click.
        /// </summary>
        private async void OnPickAvatarPhotoClicked(object sender, EventArgs e)
        {
            await PickAvatarPhotoAsync();
        }

        /// <summary>
        /// Temporary placeholder for future profile features.
        /// </summary>
        private async void OnUnderConstructionClicked(object sender, EventArgs e)
        {
            await DisplayAlert("Under Construction", "This section is coming soon.", "OK");
        }

        /// <summary>
        /// Captures a new avatar photo using the device camera.
        /// </summary>
        private async Task TakeAvatarPhotoAsync()
        {
            try
            {
                if (!MediaPicker.Default.IsCaptureSupported)
                {
                    await DisplayAlert("Camera", "Camera capture is not supported on this device.", "OK");
                    return;
                }

                FileResult? photo = await MediaPicker.Default.CapturePhotoAsync();

                if (photo == null)
                {
                    return;
                }

                string savedPath = await SaveProfileImageAsync(photo);
                SetAvatarPath(savedPath);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Avatar", $"Unable to take photo: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Picks an avatar photo from the device photo library.
        /// </summary>
        private async Task PickAvatarPhotoAsync()
        {
            try
            {
                FileResult? photo = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "Select a profile photo",
                    FileTypes = FilePickerFileType.Images
                });

                if (photo == null)
                {
                    return;
                }

                string savedPath = await SaveProfileImageAsync(photo);
                SetAvatarPath(savedPath);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Avatar", $"Unable to pick photo: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Saves the selected/captured image to app storage and returns the saved path.
        /// </summary>
        private static async Task<string> SaveProfileImageAsync(FileResult photo)
        {
            string profileDir = Path.Combine(FileSystem.AppDataDirectory, "ProfileImages");
            Directory.CreateDirectory(profileDir);

            string ext = Path.GetExtension(photo.FileName);
            if (string.IsNullOrWhiteSpace(ext))
            {
                ext = ".jpg";
            }

            string fileName = $"avatar_{DateTime.UtcNow:yyyyMMdd_HHmmss}{ext}";
            string destinationPath = Path.Combine(profileDir, fileName);

            await using Stream sourceStream = await photo.OpenReadAsync();
            await using FileStream localFileStream = File.OpenWrite(destinationPath);

            await sourceStream.CopyToAsync(localFileStream);

            return destinationPath;
        }

        /// <summary>
        /// Applies the avatar path to the active ProfileViewModel (if present).
        /// </summary>
        private void SetAvatarPath(string path)
        {
            if (BindingContext is ProfileViewModel vm)
            {
                vm.AvatarPath = path;
            }
        }
    }
}
