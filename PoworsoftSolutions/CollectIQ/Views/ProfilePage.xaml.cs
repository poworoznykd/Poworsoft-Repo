//
//  FILE            : ProfilePage.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-11-23
//  UPDATED         : 2026-01-15
//  DESCRIPTION     :
//      Full Profile page for CollectIQ.
//      Uses the singleton ProfileViewModel from IProfileService so avatar updates
//      propagate across the app (Dashboard + Profile page share the same instance).
//

using CollectIQ.Helpers;
using CollectIQ.Interfaces;
using CollectIQ.Services;
using CollectIQ.ViewModels;
using Microsoft.Maui.Storage;
using System;
using System.IO;
using System.Threading.Tasks;

namespace CollectIQ.Views
{
    public partial class ProfilePage : ContentPage
    {
        private readonly IProfileService profileService;

        // DI constructor
        public ProfilePage(IProfileService profileServiceParam)
        {
            InitializeComponent();
            profileService = profileServiceParam;

            BindingContext = ServiceHelper.GetService<IProfileService>().Profile;
        }

        // XAML/Shell-safe constructor
        public ProfilePage() : this(CollectIQ.Utilities.ServiceHelper.GetService<IProfileService>() ?? new ProfileService())
        {
        }

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
            }
        }

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

        private async void OnTakeAvatarPhoto(object sender, EventArgs e)
        {
            await TakeAvatarPhotoAsync();
        }

        private async void OnPickAvatarPhotoClicked(object sender, EventArgs e)
        {
            await PickAvatarPhotoAsync();
        }

        private async void OnUnderConstructionClicked(object sender, EventArgs e)
        {
            await DisplayAlert("Under Construction", "This section is coming soon.", "OK");
        }

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

        private void SetAvatarPath(string path)
        {
            if (BindingContext is ProfileViewModel vm)
            {
                vm.AvatarPath = path;

                // Persist so it survives restarts AND is available app-wide
                ProfileService.PersistAvatarPath(path);
            }
        }
    }
}
