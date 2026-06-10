/*
* FILE            : UserRepository.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Repository for account and profile data.
*/

using CollectIQ.Interfaces;
using CollectIQ.Models;

namespace CollectIQ.Repositories
{
    /// <summary>
    /// Provides repository methods for user accounts and profiles.
    /// </summary>
    public sealed class UserRepository : IUserRepository
    {
        private readonly IDatabase database;

        /// <summary>
        /// Initializes a new instance of the UserRepository class.
        /// </summary>
        /// <param name="database">The local database abstraction.</param>
        public UserRepository(IDatabase database)
        {
            this.database = database;
        }

        /// <summary>
        /// Gets a user account by email address.
        /// </summary>
        /// <param name="email">The email address to search for.</param>
        /// <returns>The matching account, or null when none exists.</returns>
        public Task<UserAccount?> GetAccountByEmailAsync(string email)
        {
            return database.GetUserAccountByEmailAsync(email);
        }

        /// <summary>
        /// Gets a user profile by email address.
        /// </summary>
        /// <param name="email">The email address to search for.</param>
        /// <returns>The matching profile, or null when none exists.</returns>
        public Task<UserProfile?> GetProfileByEmailAsync(string email)
        {
            return database.GetUserProfileByEmailAsync(email);
        }

        /// <summary>
        /// Saves a user account.
        /// </summary>
        /// <param name="account">The account to save.</param>
        public async Task SaveAccountAsync(UserAccount account)
        {
            await database.UpsertUserAccountAsync(account);
        }

        /// <summary>
        /// Saves a user profile.
        /// </summary>
        /// <param name="profile">The profile to save.</param>
        public Task SaveProfileAsync(UserProfile profile)
        {
            return database.UpsertUserProfileAsync(profile);
        }
    }
}
