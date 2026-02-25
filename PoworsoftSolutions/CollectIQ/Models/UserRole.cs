/*
* FILE: UserRole.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2026-02-24
* DESCRIPTION:
*     Defines the supported authorization roles for CollectIQ.
*     This is used by IUserRoleBehavior and feature gating.
*/

namespace CollectIQ.Models
{
    public enum UserRole
    {
        Guest = 0,
        Regular = 1,
        Admin = 2
    }
}
