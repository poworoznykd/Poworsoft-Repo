/*
* FILE            : RolePermissionLink.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Join table between roles and permissions.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Associates a role with a permission.
    /// </summary>
    public sealed class RolePermissionLink : BaseModel
    {
        [Indexed]
        public string RoleId { get; set; } = string.Empty;

        [Indexed]
        public string PermissionId { get; set; } = string.Empty;
    }
}
