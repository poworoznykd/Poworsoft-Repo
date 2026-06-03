/******************************************************************************
 *
 * FILE          : UserSession.cs
 * PROJECT       : CollectIQ
 * PROGRAMMER    : Darryl Poworoznyk
 * FIRST VERSION : 2026-06-02
 *
 * DESCRIPTION:
 *
 * This file defines the user session model used by CollectIQ.
 *
 * A UserSession represents a locally cached authenticated session. This allows
 * the application to unlock previously synchronized local data while offline
 * after the user has already authenticated online.
 *
 * Offline access is not the same as online authentication. Online login must
 * still be performed by the API. This model only stores the local session state
 * required to reopen the app in offline mode.
 *
 * CHANGE LOG:
 *
 * Date         Programmer              Description
 * ----------   --------------------    ---------------------------------------
 * 2026-06-02   Darryl Poworoznyk       Initial creation for offline session
 *                                      support.
 *
 *****************************************************************************/

using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Represents a locally cached user session.
    /// </summary>
    [Table("UserSession")]
    public sealed class UserSession : BaseModel
    {
        #region Constructor

        /******************************************************************************
         *
         * METHOD      : UserSession
         *
         * DESCRIPTION :
         *
         * Initializes a new user session with safe defaults.
         *
         *****************************************************************************/
        public UserSession()
        {
            UserProfileId = string.Empty;

            AccessToken = string.Empty;

            RefreshToken = string.Empty;

            DeviceId = string.Empty;

            DeviceName = string.Empty;

            Platform = string.Empty;

            LastSuccessfulLoginUtc = DateTime.UtcNow;

            OfflineAccessUntilUtc = DateTime.UtcNow.AddDays(30);

            ExpiresUtc = DateTime.UtcNow.AddHours(1);

            IsActive = true;

            RevokedUtc = null;
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets or sets the related user profile identifier.
        /// </summary>
        [Indexed]
        public string UserProfileId
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the API access token.
        /// </summary>
        public string AccessToken
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the API refresh token.
        /// </summary>
        public string RefreshToken
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the device identifier associated with this session.
        /// </summary>
        public string DeviceId
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the friendly device name.
        /// </summary>
        public string DeviceName
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the platform name, such as Android, iOS, or Windows.
        /// </summary>
        public string Platform
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the last successful online login time in UTC.
        /// </summary>
        public DateTime LastSuccessfulLoginUtc
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the date and time offline access expires.
        /// </summary>
        public DateTime OfflineAccessUntilUtc
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the access token expiration date and time.
        /// </summary>
        public DateTime ExpiresUtc
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets whether this session is active.
        /// </summary>
        public bool IsActive
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the date and time this session was revoked.
        /// </summary>
        public DateTime? RevokedUtc
        {
            get;
            set;
        }

        #endregion
    }
}