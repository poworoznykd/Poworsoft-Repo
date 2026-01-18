/*
 * FILE         : ProfileViewModel.cs
 * PROJECT      : CollectIQ (Mobile Application)
 * PROGRAMMER   : Darryl Poworoznyk
 * FIRST VERSION: 2025-12-04
 * UPDATED      : 2026-01-15
 * DESCRIPTION  :
 *   ViewModel representing the user's profile.
 *   - Persists profile fields using Preferences
 *   - Saves avatar image into AppDataDirectory/ProfileImages
 *   - Uses unique filenames to avoid image caching issues on Android
 */

using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace CollectIQ.ViewModels
{
    public class ProfileViewModel : INotifyPropertyChanged
    {
        public string CloudUserId { get; set; } = string.Empty;


        private const string PrefDisplayName = "collectiq.profile.displayName";
        private const string PrefHandle = "collectiq.profile.handle";
        private const string PrefAvatarPath = "collectiq.profile.avatarPath";
        private const string PrefIsVerified = "collectiq.profile.isVerified";
        private const string PrefRating = "collectiq.profile.rating";
        private const string PrefRatingCount = "collectiq.profile.ratingCount";
        private const string PrefLocation = "collectiq.profile.location";
        private const string PrefMemberSince = "collectiq.profile.memberSince";

        private string displayName = "Collector";
        private string handle = "@collectiq";
        private string avatarPath = string.Empty;

        private bool isVerified;
        private double rating = 5.0;
        private int ratingCount = 0;

        private string location = "Ontario, Canada";
        private string memberSince = "Member since 2026";

        private int tradesCompleted = 0;
        private int salesCompleted = 0;
        private int purchasesCompleted = 0;
        private string avgResponseTime = "—";

        private int collectionCount = 0;
        private decimal collectionValue = 0m;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ProfileViewModel()
        {
            LoadFromPreferences();
        }

        public string DisplayName
        {
            get { return displayName; }
            set
            {
                if (displayName == value) return;
                displayName = value;
                OnPropertyChanged();
                Preferences.Set(PrefDisplayName, displayName);
            }
        }

        public string Handle
        {
            get { return handle; }
            set
            {
                if (handle == value) return;
                handle = value;
                OnPropertyChanged();
                Preferences.Set(PrefHandle, handle);
            }
        }

        // Primary property used by ProfileSummaryCard + ProfilePage bindings
        public string AvatarPath
        {
            get { return avatarPath; }
            set
            {
                if (avatarPath == value) return;
                avatarPath = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AvatarImagePath)); // keep compatibility
                Preferences.Set(PrefAvatarPath, avatarPath);
            }
        }

        // Compatibility alias for older bindings (ex: AvatarImagePath)
        public string AvatarImagePath
        {
            get { return AvatarPath; }
            set { AvatarPath = value; }
        }

        public bool IsVerified
        {
            get { return isVerified; }
            set
            {
                if (isVerified == value) return;
                isVerified = value;
                OnPropertyChanged();
                Preferences.Set(PrefIsVerified, isVerified);
            }
        }

        public double Rating
        {
            get { return rating; }
            set
            {
                if (Math.Abs(rating - value) < 0.0001) return;
                rating = value;
                OnPropertyChanged();
                Preferences.Set(PrefRating, rating);
            }
        }

        public int RatingCount
        {
            get { return ratingCount; }
            set
            {
                if (ratingCount == value) return;
                ratingCount = value;
                OnPropertyChanged();
                Preferences.Set(PrefRatingCount, ratingCount);
            }
        }

        public string Location
        {
            get { return location; }
            set
            {
                if (location == value) return;
                location = value ?? string.Empty;
                OnPropertyChanged();
                Preferences.Set(PrefLocation, location);
            }
        }

        public string MemberSince
        {
            get { return memberSince; }
            set
            {
                if (memberSince == value) return;
                memberSince = value ?? string.Empty;
                OnPropertyChanged();
                Preferences.Set(PrefMemberSince, memberSince);
            }
        }

        public int TradesCompleted
        {
            get { return tradesCompleted; }
            set { if (tradesCompleted == value) return; tradesCompleted = value; OnPropertyChanged(); }
        }

        public int SalesCompleted
        {
            get { return salesCompleted; }
            set { if (salesCompleted == value) return; salesCompleted = value; OnPropertyChanged(); }
        }

        public int PurchasesCompleted
        {
            get { return purchasesCompleted; }
            set { if (purchasesCompleted == value) return; purchasesCompleted = value; OnPropertyChanged(); }
        }

        public string AvgResponseTime
        {
            get { return avgResponseTime; }
            set { if (avgResponseTime == value) return; avgResponseTime = value ?? "—"; OnPropertyChanged(); }
        }

        public int CollectionCount
        {
            get { return collectionCount; }
            set { if (collectionCount == value) return; collectionCount = value; OnPropertyChanged(); }
        }

        public decimal CollectionValue
        {
            get { return collectionValue; }
            set { if (collectionValue == value) return; collectionValue = value; OnPropertyChanged(); }
        }

        public async Task<bool> SaveAvatarFromPickerFileAsync(FileResult file)
        {
            try
            {
                if (file == null)
                {
                    return false;
                }

                string profileDir = Path.Combine(FileSystem.AppDataDirectory, "ProfileImages");
                Directory.CreateDirectory(profileDir);

                string ext = Path.GetExtension(file.FileName);
                if (string.IsNullOrWhiteSpace(ext))
                {
                    ext = ".jpg";
                }

                // IMPORTANT: unique name each time to avoid Image caching
                string fileName = $"avatar_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}{ext}";
                string destinationPath = Path.Combine(profileDir, fileName);

                await using Stream source = await file.OpenReadAsync();
                await using FileStream dest = File.OpenWrite(destinationPath);
                await source.CopyToAsync(dest);

                // Optional cleanup of previous avatar file (keep it safe)
                TryDeleteOldAvatarFile(AvatarPath, destinationPath);

                AvatarPath = destinationPath;

                // Let pages refresh if they’re listening
                MessagingCenter.Send(this, "ProfileUpdated");

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProfileViewModel] SaveAvatarFromPickerFileAsync failed: {ex.Message}");
                return false;
            }
        }

        private static void TryDeleteOldAvatarFile(string oldPath, string newPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(oldPath)) return;
                if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase)) return;

                if (File.Exists(oldPath))
                {
                    File.Delete(oldPath);
                }
            }
            catch
            {
                // Non-fatal
            }
        }

        private void LoadFromPreferences()
        {
            DisplayName = Preferences.Get(PrefDisplayName, displayName);
            Handle = Preferences.Get(PrefHandle, handle);

            string savedAvatar = Preferences.Get(PrefAvatarPath, string.Empty);
            if (!string.IsNullOrWhiteSpace(savedAvatar) && File.Exists(savedAvatar))
            {
                avatarPath = savedAvatar;
                OnPropertyChanged(nameof(AvatarPath));
                OnPropertyChanged(nameof(AvatarImagePath));
            }

            IsVerified = Preferences.Get(PrefIsVerified, false);
            Rating = Preferences.Get(PrefRating, 5.0);
            RatingCount = Preferences.Get(PrefRatingCount, 0);
            Location = Preferences.Get(PrefLocation, location);
            MemberSince = Preferences.Get(PrefMemberSince, memberSince);
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            if (propertyName == null) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
