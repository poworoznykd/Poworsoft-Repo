/*
* FILE            : Permission.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Represents a granular application permission for future feature gating.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a permission that can be assigned to a role.
    /// </summary>
    public sealed class Permission : BaseModel
    {
        [Indexed(Unique = true)]
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;
    }
}
