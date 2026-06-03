/******************************************************************************
 *
 * FILE          : AppSetting.cs
 * PROJECT       : CollectIQ
 * PROGRAMMER    : Darryl Poworoznyk
 * FIRST VERSION : 2026-06-02
 *
 * DESCRIPTION:
 *
 * Represents a local application setting.
 *
 *****************************************************************************/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents an application setting.
    /// </summary>
    [Table("AppSetting")]
    public class AppSetting
    {
        /// <summary>
        /// Gets or sets the setting identifier.
        /// </summary>
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the setting key.
        /// </summary>
        public string SettingKey { get; set; }

        /// <summary>
        /// Gets or sets the setting value.
        /// </summary>
        public string SettingValue { get; set; }

        /// <summary>
        /// Gets or sets the date this setting was last updated.
        /// </summary>
        public string UpdatedAt { get; set; }
    }
}