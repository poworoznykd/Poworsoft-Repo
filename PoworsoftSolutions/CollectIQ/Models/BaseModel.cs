using SQLite;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CollectIQ.Models
{
    /// <summary>
    /// Common base for all persisted entities in the local SQLite database.
    /// </summary>
    public abstract class BaseModel : INotifyPropertyChanged
    {
        [PrimaryKey]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public bool IsDeleted { get; set; } = false;

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Event raised whenever a bindable property changes.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Helper used by property setters to raise PropertyChanged.
        /// CallerMemberName allows us to omit the property name.
        /// </summary>
        /// <param name="name">The name of the property that changed.</param>
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
