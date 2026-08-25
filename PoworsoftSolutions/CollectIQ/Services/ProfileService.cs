using CollectIQ.Interfaces;
using CollectIQ.Services.Session;
using CollectIQ.ViewModels;

namespace CollectIQ.Services
{
    /// <summary>
    /// Provides the UI profile for the currently authenticated account.
    /// The view model is shared for UI binding, but its persisted preferences
    /// are scoped by UserAccount.Id so accounts cannot inherit each other's UI profile.
    /// </summary>
    public sealed class ProfileService : IProfileService
    {
        private readonly ProfileViewModel profile = new ProfileViewModel();

        public ProfileViewModel Profile
        {
            get
            {
                profile.ActivateAccount(UserSession.CurrentUser);
                return profile;
            }
        }

        [Obsolete("Avatar persistence is account-scoped by ProfileViewModel.AvatarPath.")]
        public static void PersistAvatarPath(string path)
        {
            // Intentionally empty. Older callers are harmless; ProfileViewModel
            // now persists the value under the active UserAccount.Id.
        }
    }
}
