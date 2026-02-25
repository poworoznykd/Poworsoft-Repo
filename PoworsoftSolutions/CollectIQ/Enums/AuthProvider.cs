/*
* FILE: AuthProvider.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2026-02-24
* DESCRIPTION:
*     Represents external authentication providers used for social sign-in.
*     This is intentionally small and stable so it can be referenced across
*     services and UI without pulling in provider-specific SDK code.
*/

namespace CollectIQ.Enums
{
    public enum AuthProvider
    {
        Unknown = 0,
        Local = 1,
        Guest = 2,
        Google = 3,
        Facebook = 4,
        Apple = 5
    }
}
