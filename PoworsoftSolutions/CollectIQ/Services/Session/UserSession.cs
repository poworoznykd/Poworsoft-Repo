using CollectIQ.Interfaces;
using CollectIQ.Models;

namespace CollectIQ.Services.Session
{
    /// <summary>
    /// Holds only the identity for the account that actually completed authentication.
    /// The permanent UserAccount.Id is tracked independently from mutable profile fields.
    /// </summary>
    public static class UserSession
    {
        private static UserProfile? currentUser;
        private static string currentUserAccountId = string.Empty;

        public static UserProfile? CurrentUser
        {
            get => currentUser;
            set
            {
                currentUser = value;
                if (value != null && !string.IsNullOrWhiteSpace(value.UserAccountId))
                    currentUserAccountId = value.UserAccountId;
                else if (value == null)
                    currentUserAccountId = string.Empty;
            }
        }

        public static IUserRoleBehavior? CurrentRoleBehavior { get; set; }

        public static string CurrentUserAccountId => currentUserAccountId;

        public static void SetAuthenticatedUser(string userAccountId, UserProfile profile, IUserRoleBehavior roleBehavior)
        {
            if (string.IsNullOrWhiteSpace(userAccountId))
                throw new ArgumentException("A permanent UserAccount.Id is required.", nameof(userAccountId));

            profile.UserAccountId = userAccountId;
            currentUserAccountId = userAccountId;
            currentUser = profile;
            CurrentRoleBehavior = roleBehavior;
        }

        public static void Clear()
        {
            currentUser = null;
            currentUserAccountId = string.Empty;
            CurrentRoleBehavior = null;
        }

        public static string RequireCurrentUserAccountId()
        {
            if (string.IsNullOrWhiteSpace(currentUserAccountId))
                throw new InvalidOperationException("No signed-in CollectIQ account is available for this database operation.");

            return currentUserAccountId;
        }
    }
}
