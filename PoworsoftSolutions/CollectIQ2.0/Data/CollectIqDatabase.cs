/******************************************************************************
 *
 * FILE          : CollectIqDatabase.cs
 * PROJECT       : CollectIQ
 * PROGRAMMER    : Darryl Poworoznyk
 * FIRST VERSION : 2026-06-02
 *
 * DESCRIPTION:
 *
 * This class is responsible for creating and managing the local SQLite
 * database used by CollectIQ.
 *
 * The local database serves as the application's offline working copy.
 *
 * Responsibilities:
 *
 * - Create SQLite connection
 * - Initialize schema
 * - Enable foreign key support
 * - Expose database connection
 *
 * CHANGE LOG:
 *
 * Date         Programmer              Description
 * ----------   --------------------    ---------------------------------------
 * 2026-06-02   Darryl Poworoznyk       Initial creation.
 *
 *****************************************************************************/

using CollectIQ.Constants;
using SQLite;

namespace CollectIQ.Data
{
    /// <summary>
    /// Manages the local SQLite database.
    /// </summary>
    public class CollectIqDatabase
    {
        #region Private Members

        /// <summary>
        /// SQLite asynchronous database connection.
        /// </summary>
        private readonly SQLiteAsyncConnection database;

        #endregion

        #region Constructors

        /******************************************************************************
         *
         * METHOD      : CollectIqDatabase
         *
         * DESCRIPTION :
         *
         * Creates the SQLite database connection using the application data
         * directory provided by the MAUI platform.
         *
         *****************************************************************************/
        public CollectIqDatabase()
        {
            string databasePath = Path.Combine(
                FileSystem.AppDataDirectory,
                AppConstants.DatabaseFileName);

            database = new SQLiteAsyncConnection(databasePath);
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets the SQLite database connection.
        /// </summary>
        public SQLiteAsyncConnection Connection
        {
            get
            {
                return database;
            }
        }

        #endregion

        #region Public Methods

        /******************************************************************************
         *
         * METHOD      : InitializeAsync
         *
         * DESCRIPTION :
         *
         * Initializes the SQLite database by enabling foreign key support
         * and executing all schema creation statements.
         *
         * RETURNS:
         *
         * Task
         *
         *****************************************************************************/
        public async Task InitializeAsync()
        {
            await database.ExecuteAsync("PRAGMA foreign_keys = ON;");

            foreach (string statement in DatabaseSchema.CreateStatements)
            {
                await database.ExecuteAsync(statement);
            }
        }

        #endregion
    }
}