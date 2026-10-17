namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Provides access to and maintenance of the signed-in Supabase session.
    /// </summary>
    public interface ISupabaseSessionService
    {
        Task<string?> GetAccessTokenAsync();

        /// <summary>
        /// Refreshes the session using the stored refresh token and persists the
        /// rotated access/refresh token pair returned by Supabase.
        /// </summary>
        Task<string?> RefreshAccessTokenAsync();

        /// <summary>
        /// Returns a token that has been validated against Supabase Auth.
        /// A 401 or 403 from /auth/v1/user triggers one refresh-and-retry cycle.
        /// </summary>
        Task<string?> GetValidAccessTokenAsync();

        /// <summary>
        /// Returns the authenticated Supabase user ID after validating/refreshing
        /// the current session. Returns null when the session cannot be recovered.
        /// </summary>
        Task<string?> GetAuthenticatedUserIdAsync();
    }
}
