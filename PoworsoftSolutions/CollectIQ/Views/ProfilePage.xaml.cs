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
using CollectIQ.Services.Session;
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

            BindingContext = profileService.Profile;
        }

        // XAML/Shell-safe constructor
        public ProfilePage() : this(ServiceHelper.GetService<IProfileService>() ?? throw new InvalidOperationException("IProfileService is not registered."))
        {
        }


        protected override void OnAppearing()
        {
            base.OnAppearing();
            BindingContext = profileService.Profile;
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
                await SetAvatarPathAsync(savedPath);
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
                await SetAvatarPathAsync(savedPath);
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

        private async Task SetAvatarPathAsync(string path)
        {
            if (BindingContext is ProfileViewModel vm)
            {
                vm.AvatarPath = path;
            }

            if (UserSession.CurrentUser != null)
            {
                UserSession.CurrentUser.AvatarImageId = path;
                UserSession.CurrentUser.UpdatedUtc = DateTime.UtcNow;
                await App.Database.UpsertUserProfileAsync(UserSession.CurrentUser);
            }
        }

        private async void OnAccountCenterClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new AccountPage());
        }

        /// <summary>
        /// Opens the local developer database view.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private async void OnDeveloperDatabaseClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new DeveloperDatabasePage());
        }

        private async void OnLogoutClicked(object sender, EventArgs e)
        {
            bool confirm = await DisplayAlert("Logout", "Log out of CollectIQ?", "Logout", "Cancel");
            if (!confirm)
            {
                return;
            }

            IAuthService auth = ServiceHelper.GetService<IAuthService>()
                ?? throw new InvalidOperationException("IAuthService is not registered.");
            await auth.SignOutAsync();

            // Bounce back to the login screen, same styling as App.OnStart()
            Application.Current.MainPage = new NavigationPage(new AuthSheet(auth))
            {
                BarBackgroundColor = Color.FromArgb("#0B0B0D"),
                BarTextColor = Color.FromArgb("#00B4FF")
            };
        }
    }
}
