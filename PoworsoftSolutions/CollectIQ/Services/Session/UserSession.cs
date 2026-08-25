using CollectIQ.Interfaces;
using CollectIQ.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CollectIQ.Services.Session
{
    public static class UserSession
    {
        public static UserProfile? CurrentUser { get; set; }
        public static IUserRoleBehavior? CurrentRoleBehavior { get; set; }

        public static string CurrentUserAccountId => CurrentUser?.UserAccountId ?? string.Empty;

        public static string RequireCurrentUserAccountId()
        {
            string accountId = CurrentUserAccountId;
            if (string.IsNullOrWhiteSpace(accountId))
            {
                throw new InvalidOperationException("No signed-in CollectIQ account is available for this database operation.");
            }

            return accountId;
        }
    }
}

