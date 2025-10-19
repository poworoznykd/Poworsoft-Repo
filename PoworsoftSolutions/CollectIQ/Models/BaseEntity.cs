using SQLite;

namespace CollectIQ.Models
{
    /// <summary>
    /// Common base for all persisted entities in the local SQLite database.
    /// </summary>
    public abstract class BaseEntity
    {
        [PrimaryKey]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public bool IsDeleted { get; set; } = false;

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
