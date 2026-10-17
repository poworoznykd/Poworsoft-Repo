namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Synchronizes owned collections and cards between local SQLite and Supabase.
    /// SQLite remains the immediate offline source used by the UI.
    /// </summary>
    public interface ISyncService
    {
        string LastPullSummary { get; }

        Task SyncAsync();

        /// <summary>
        /// Clears the incremental cloud cursor for the current user so the next
        /// synchronization performs a complete owned-data pull.
        /// </summary>
        Task ResetPullCursorAsync();
    }
}
