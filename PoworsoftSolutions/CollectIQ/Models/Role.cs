/*
* FILE            : Role.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Represents an authorization role used by CollectIQ.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a named application role such as Admin, Regular, or Guest.
    /// </summary>
    public sealed class Role : BaseModel
    {
        [Indexed(Unique = true)]
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;
    }
}
