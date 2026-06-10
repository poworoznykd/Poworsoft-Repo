/*
* FILE            : UserRoleLink.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Join table between user accounts and roles.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Associates a user account with an application role.
    /// </summary>
    public sealed class UserRoleLink : BaseModel
    {
        [Indexed]
        public string UserAccountId { get; set; } = string.Empty;

        [Indexed]
        public string RoleId { get; set; } = string.Empty;
    }
}
