namespace CollectIQ.Interfaces
{
    /// <summary>
    /// Provides access to the signed-in Supabase session already stored by CollectIQ authentication.
    /// </summary>
    public interface ISupabaseSessionService
    {
        Task<string?> GetAccessTokenAsync();
        Task<string?> RefreshAccessTokenAsync();
    }
}
