/******************************************************************************
 *
 * FILE          : AppConstants.cs
 * PROJECT       : CollectIQ
 * PROGRAMMER    : Darryl Poworoznyk
 * FIRST VERSION : 2026-06-02
 *
 * DESCRIPTION:
 *
 * This file contains application-wide constants used throughout CollectIQ.
 *
 * These constants centralize commonly used values including:
 *
 * - Database settings
 * - Offline access settings
 * - Synchronization status values
 * - Synchronization operation types
 *
 * CHANGE LOG:
 *
 * Date         Programmer              Description
 * ----------   --------------------    ---------------------------------------
 * 2026-06-02   Darryl Poworoznyk       Initial creation.
 *
 *****************************************************************************/

namespace CollectIQ.Constants
{
    /// <summary>
    /// Contains application-wide constant values.
    /// </summary>
    public static class AppConstants
    {
        #region Database

        /// <summary>
        /// Local SQLite database file name.
        /// </summary>
        public const string DatabaseFileName = "collectiq.db3";

        #endregion

        #region Offline Access

        /// <summary>
        /// Number of days a user may continue using the application offline
        /// after a successful online authentication.
        /// </summary>
        public const int OfflineAccessDays = 30;

        #endregion

        #region Synchronization Status

        /// <summary>
        /// Record has not yet been synchronized.
        /// </summary>
        public const string SyncStatusPending = "Pending";

        /// <summary>
        /// Record has been synchronized successfully.
        /// </summary>
        public const string SyncStatusSynced = "Synced";

        /// <summary>
        /// Synchronization failed.
        /// </summary>
        public const string SyncStatusFailed = "Failed";

        /// <summary>
        /// Synchronization conflict detected.
        /// </summary>
        public const string SyncStatusConflict = "Conflict";

        #endregion

        #region Synchronization Operations

        /// <summary>
        /// Insert operation.
        /// </summary>
        public const string OperationInsert = "Insert";

        /// <summary>
        /// Update operation.
        /// </summary>
        public const string OperationUpdate = "Update";

        /// <summary>
        /// Delete operation.
        /// </summary>
        public const string OperationDelete = "Delete";

        #endregion
    }
}