namespace CollectIQ.Models.Cloud
{
    /// <summary>
    /// Result of the controlled one-collection cloud sync test.
    /// </summary>
    public sealed class CloudCollectionSyncResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public string CollectionId { get; set; } = string.Empty;
        public string CollectionName { get; set; } = string.Empty;
        public string OwnerUserId { get; set; } = string.Empty;
    }
}
