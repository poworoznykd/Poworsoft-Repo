/******************************************************************************
 *
 * FILE          : BaseModel.cs
 * PROJECT       : CollectIQ
 * PROGRAMMER    : Darryl Poworoznyk
 * FIRST VERSION : 2026-06-02
 *
 * DESCRIPTION:
 *
 * This file contains the base model used by persisted CollectIQ entities.
 *
 * The BaseModel class provides common fields required by most application
 * records, including:
 *
 * - A globally unique string identifier
 * - Soft-delete support
 * - Created timestamp
 * - Updated timestamp
 * - Property change notification support
 *
 * The Id field is intentionally stored as a string GUID rather than an
 * auto-incrementing integer. This makes the model easier to synchronize with
 * a future online API and central database because records can be created
 * offline and later uploaded without requiring the server to assign the first
 * identifier.
 *
 * CHANGE LOG:
 *
 * Date         Programmer              Description
 * ----------   --------------------    ---------------------------------------
 * 2026-06-02   Darryl Poworoznyk       Rebuilt model for new CollectIQ
 *                                      architecture using existing GUID-based
 *                                      identifier strategy.
 *
 *****************************************************************************/

using SQLite;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CollectIQ.Models
{
    /// <summary>
    /// Provides common persistence and notification functionality for models.
    /// </summary>
    public abstract class BaseModel : INotifyPropertyChanged
    {
        #region Private Members

        /// <summary>
        /// Backing field for the soft-delete flag.
        /// </summary>
        private bool isDeleted;

        /// <summary>
        /// Backing field for the created timestamp.
        /// </summary>
        private DateTime createdUtc;

        /// <summary>
        /// Backing field for the updated timestamp.
        /// </summary>
        private DateTime updatedUtc;

        #endregion

        #region Public Events

        /// <summary>
        /// Raised when a bindable property changes.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        #endregion

        #region Constructor

        /******************************************************************************
         *
         * METHOD      : BaseModel
         *
         * DESCRIPTION :
         *
         * Initializes common model fields used by persisted CollectIQ records.
         *
         *****************************************************************************/
        protected BaseModel()
        {
            Id = Guid.NewGuid().ToString();

            isDeleted = false;

            createdUtc = DateTime.UtcNow;

            updatedUtc = DateTime.UtcNow;
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets or sets the unique identifier for the model.
        /// </summary>
        /// <remarks>
        /// This identifier is generated locally as a GUID so records can be
        /// created offline and synchronized to the online system later.
        /// </remarks>
        [PrimaryKey]
        public string Id
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets whether the record has been soft deleted.
        /// </summary>
        /// <remarks>
        /// Records should usually be soft deleted so they can be synchronized
        /// properly with the future online API before being permanently removed.
        /// </remarks>
        public bool IsDeleted
        {
            get
            {
                return isDeleted;
            }

            set
            {
                if (isDeleted != value)
                {
                    isDeleted = value;

                    Touch();

                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the UTC date and time when the record was created.
        /// </summary>
        public DateTime CreatedUtc
        {
            get
            {
                return createdUtc;
            }

            set
            {
                if (createdUtc != value)
                {
                    createdUtc = value;

                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the UTC date and time when the record was last updated.
        /// </summary>
        public DateTime UpdatedUtc
        {
            get
            {
                return updatedUtc;
            }

            set
            {
                if (updatedUtc != value)
                {
                    updatedUtc = value;

                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region Public Methods

        /******************************************************************************
         *
         * METHOD      : Touch
         *
         * DESCRIPTION :
         *
         * Updates the UpdatedUtc timestamp to the current UTC date and time.
         *
         * This method should be called whenever a model is changed and saved.
         *
         *****************************************************************************/
        public void Touch()
        {
            updatedUtc = DateTime.UtcNow;

            OnPropertyChanged(nameof(UpdatedUtc));
        }

        #endregion

        #region Protected Methods

        /******************************************************************************
         *
         * METHOD      : OnPropertyChanged
         *
         * DESCRIPTION :
         *
         * Raises the PropertyChanged event for data binding.
         *
         * PARAMETERS  :
         *
         * propertyName - The name of the property that changed.
         *
         *****************************************************************************/
        protected void OnPropertyChanged(
            [CallerMemberName] string? propertyName = null)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return;
            }

            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}