using CollectIQ.Models;

public interface IDatabase
{
    Task InitializeAsync();

    Task UpsertAsync<T>(T entity) where T : BaseEntity, new();
    Task DeleteAsync<T>(string id) where T : BaseEntity, new();

    Task UpsertUserProfileAsync(UserProfile profile);
    Task<UserProfile?> GetUserProfileAsync();

    Task CreateCollectionAsync(Collection collection);
    Task<IReadOnlyList<Collection>> GetCollectionsAsync(string ownerUserId);

    // If you chose Card (not CardItem):
    Task AddCardAsync(Card card);
    Task<IReadOnlyList<Card>> GetCardsByCollectionAsync(string collectionId);

    Task AddPriceSnapshotAsync(PriceSnapshot snapshot);
    Task AddCardImageAsync(CardImage image);

    Task AddShareAsync(CollectionShare share);
    Task<IReadOnlyList<CollectionShare>> GetSharesAsync(string collectionId);
}
