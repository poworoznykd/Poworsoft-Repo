namespace CollectIQ.Models.Cloud
{
    /// <summary>
    /// Result of the controlled card upload/read-back verification.
    /// </summary>
    public sealed class CloudCardSyncResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public string CollectionId { get; set; } = string.Empty;
        public int LocalCardCount { get; set; }
        public int CloudCardCount { get; set; }
    }
}
