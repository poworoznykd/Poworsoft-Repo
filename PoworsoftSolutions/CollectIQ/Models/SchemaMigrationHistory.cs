/*
* FILE            : SchemaMigrationHistory.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2026-06-08
* DESCRIPTION     :
*     Tracks local database schema migrations applied to the CollectIQ database.
*/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents an applied database migration.
    /// </summary>
    public sealed class SchemaMigrationHistory : BaseModel
    {
        [Indexed(Unique = true)]
        public string MigrationName { get; set; } = string.Empty;

        public string AppVersion { get; set; } = string.Empty;

        public int DatabaseVersion { get; set; }

        public DateTime AppliedUtc { get; set; } = DateTime.UtcNow;
    }
}
