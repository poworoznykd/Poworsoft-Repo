/*
* FILE: UserRoles.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2026-02-24
* DESCRIPTION:
*     Common role constants and helpers to reduce stringly-typed logic.
*     If you later add "Pro", add it here and update RoleBehaviorResolver.
*/

using System;

namespace CollectIQ.Models
{
    public static class UserRoles
    {
        public const string Guest = "Guest";
        public const string Regular = "Regular";
        public const string Admin = "Admin";

        public static bool TryParse(string? roleName, out UserRole role)
        {
            role = UserRole.Guest;

            if (string.IsNullOrWhiteSpace(roleName))
            {
                return false;
            }

            if (roleName.Equals(Guest, StringComparison.OrdinalIgnoreCase))
            {
                role = UserRole.Guest;
                return true;
            }

            if (roleName.Equals(Regular, StringComparison.OrdinalIgnoreCase))
            {
                role = UserRole.Regular;
                return true;
            }

            if (roleName.Equals(Admin, StringComparison.OrdinalIgnoreCase))
            {
                role = UserRole.Admin;
                return true;
            }

            return false;
        }

        public static string Normalize(string? roleName)
        {
            if (TryParse(roleName, out UserRole role))
            {
                return role switch
                {
                    UserRole.Admin => Admin,
                    UserRole.Regular => Regular,
                    _ => Guest
                };
            }

            return Guest;
        }
    }
}
