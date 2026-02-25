/*
* FILE: RoleBehaviorResolver.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2026-02-24
* DESCRIPTION:
*     Central place to map a user's role to the correct behavior class.
*     Keeps role-switching logic out of UI code.
*/

using System;
using CollectIQ.Interfaces;
using CollectIQ.Models;

namespace CollectIQ.Services.Roles
{
    public static class RoleBehaviorResolver
    {
        public static IUserRoleBehavior Resolve(string? roleName)
        {
            string normalized = UserRoles.Normalize(roleName);

            if (normalized.Equals(UserRoles.Admin, StringComparison.OrdinalIgnoreCase))
            {
                return new AdminRoleBehavior();
            }

            if (normalized.Equals(UserRoles.Regular, StringComparison.OrdinalIgnoreCase))
            {
                return new RegularRoleBehavior();
            }

            return new GuestRoleBehavior();
        }
    }
}
