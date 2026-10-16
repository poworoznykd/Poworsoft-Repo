namespace CollectIQ.Models.Cloud
{
    /// <summary>
    /// Describes the result of an authenticated CollectIQ cloud connection test.
    /// </summary>
    public sealed class CloudConnectionResult
    {
        public bool IsSuccess { get; init; }
        public string Message { get; init; } = string.Empty;
        public string AuthUserId { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
    }
}
