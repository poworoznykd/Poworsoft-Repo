using CollectIQ.Interfaces;
using CollectIQ.ViewModels;
using Microsoft.Maui.Storage;
using System.IO;

namespace CollectIQ.Services
{
    public class ProfileService : IProfileService
    {
        private const string AvatarPathPreferenceKey = "CollectIQ.Profile.AvatarPath";

        public ProfileViewModel Profile { get; } = new ProfileViewModel();

        public ProfileService()
        {
            // Load persisted avatar path once at app startup
            try
            {
                string savedPath = Preferences.Default.Get(AvatarPathPreferenceKey, string.Empty);

                if (!string.IsNullOrWhiteSpace(savedPath) && File.Exists(savedPath))
                {
                    Profile.AvatarPath = savedPath;
                }
            }
            catch
            {
                // Never crash app startup because of a preference read or file check
            }
        }

        public static void PersistAvatarPath(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    Preferences.Default.Set(AvatarPathPreferenceKey, path);
                }
            }
            catch
            {
                // Ignore persistence errors (device/storage edge cases)
            }
        }
    }
}
