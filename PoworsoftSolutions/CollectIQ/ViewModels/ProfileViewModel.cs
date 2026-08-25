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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using CollectIQ.Models;

namespace CollectIQ.ViewModels
{
    public class ProfileViewModel : INotifyPropertyChanged
    {
        public string CloudUserId { get; private set; } = string.Empty;
        private string activeAccountId = string.Empty;


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
        }

        public void ActivateAccount(UserProfile? userProfile)
        {
            string accountId = userProfile?.UserAccountId ?? string.Empty;
            if (string.Equals(activeAccountId, accountId, StringComparison.Ordinal))
            {
                return;
            }

            activeAccountId = accountId;
            CloudUserId = accountId;

            if (string.IsNullOrWhiteSpace(accountId))
            {
                displayName = "Collector";
                handle = "@collectiq";
                avatarPath = string.Empty;
                location = string.Empty;
                memberSince = string.Empty;
                RaiseAllProfileProperties();
                return;
            }

            string fallbackName = string.IsNullOrWhiteSpace(userProfile?.DisplayName)
                ? (userProfile?.Email ?? "Collector")
                : userProfile.DisplayName;

            displayName = Preferences.Get(Key(PrefDisplayName), fallbackName);
            handle = Preferences.Get(Key(PrefHandle), BuildDefaultHandle(fallbackName));
            string savedAvatar = Preferences.Get(Key(PrefAvatarPath), userProfile?.AvatarImageId ?? string.Empty);
            avatarPath = !string.IsNullOrWhiteSpace(savedAvatar) && File.Exists(savedAvatar) ? savedAvatar : string.Empty;
            isVerified = Preferences.Get(Key(PrefIsVerified), false);
            rating = Preferences.Get(Key(PrefRating), 5.0);
            ratingCount = Preferences.Get(Key(PrefRatingCount), 0);
            location = Preferences.Get(Key(PrefLocation), userProfile?.LocationDisplay ?? string.Empty);
            memberSince = Preferences.Get(Key(PrefMemberSince), $"Member since {(userProfile?.CreatedUtc ?? DateTime.UtcNow):yyyy}");
            RaiseAllProfileProperties();
        }

        public string DisplayName
        {
            get { return displayName; }
            set
            {
                if (displayName == value) return;
                displayName = value;
                OnPropertyChanged();
                SetPreference(PrefDisplayName, displayName);
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
                SetPreference(PrefHandle, handle);
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
                SetPreference(PrefAvatarPath, avatarPath);
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
                SetPreference(PrefIsVerified, isVerified);
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
                SetPreference(PrefRating, rating);
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
                SetPreference(PrefRatingCount, ratingCount);
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
                SetPreference(PrefLocation, location);
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
                SetPreference(PrefMemberSince, memberSince);
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

                string profileDir = Path.Combine(FileSystem.AppDataDirectory, "ProfileImages", string.IsNullOrWhiteSpace(activeAccountId) ? "unassigned" : activeAccountId);
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

        private string Key(string baseKey)
        {
            return string.IsNullOrWhiteSpace(activeAccountId)
                ? $"{baseKey}.signedout"
                : $"{baseKey}.{activeAccountId}";
        }

        private void SetPreference(string baseKey, string value)
        {
            if (!string.IsNullOrWhiteSpace(activeAccountId)) Preferences.Set(Key(baseKey), value ?? string.Empty);
        }

        private void SetPreference(string baseKey, bool value)
        {
            if (!string.IsNullOrWhiteSpace(activeAccountId)) Preferences.Set(Key(baseKey), value);
        }

        private void SetPreference(string baseKey, double value)
        {
            if (!string.IsNullOrWhiteSpace(activeAccountId)) Preferences.Set(Key(baseKey), value);
        }

        private void SetPreference(string baseKey, int value)
        {
            if (!string.IsNullOrWhiteSpace(activeAccountId)) Preferences.Set(Key(baseKey), value);
        }

        private static string BuildDefaultHandle(string displayName)
        {
            string clean = new string((displayName ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .ToArray());
            return string.IsNullOrWhiteSpace(clean) ? "@collectiq" : "@" + clean.ToLowerInvariant();
        }

        private void RaiseAllProfileProperties()
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(Handle));
            OnPropertyChanged(nameof(AvatarPath));
            OnPropertyChanged(nameof(AvatarImagePath));
            OnPropertyChanged(nameof(IsVerified));
            OnPropertyChanged(nameof(Rating));
            OnPropertyChanged(nameof(RatingCount));
            OnPropertyChanged(nameof(Location));
            OnPropertyChanged(nameof(MemberSince));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            if (propertyName == null) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
