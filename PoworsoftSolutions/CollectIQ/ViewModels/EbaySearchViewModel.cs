//
//  FILE            : EbaySearchViewModel.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-12-06
//  DESCRIPTION     :
//      View model for the EbaySearchPage. Exposes the listings
//      collection, currently selected listing, status text, and
//      a computed display value for the "Est." pill.
//

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CollectIQ.Models;

namespace CollectIQ.ViewModels
{
    /// <summary>
    /// View model backing the eBay search experience.
    /// </summary>
    public class EbaySearchViewModel : INotifyPropertyChanged
    {
        private EbayListing? selectedListing;
        private string statusText = "READY";

        /// <summary>
        /// Raised when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Creates a new instance of the view model.
        /// </summary>
        /// <param name="listingsSource">
        /// The observable collection that the page uses for its results list.
        /// This is passed in from the page so we do not duplicate state.
        /// </param>
        public EbaySearchViewModel(ObservableCollection<EbayListing> listingsSource)
        {
            Listings = listingsSource ?? throw new ArgumentNullException(nameof(listingsSource));
        }

        /// <summary>
        /// The collection of results shown in the CollectionView.
        /// </summary>
        public ObservableCollection<EbayListing> Listings { get; }

        /// <summary>
        /// The currently selected listing (via tap or Insights icon).
        /// </summary>
        public EbayListing? SelectedListing
        {
            get => selectedListing;
            set
            {
                if (!ReferenceEquals(selectedListing, value))
                {
                    selectedListing = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasSelectedListing));
                    OnPropertyChanged(nameof(SelectedEstimatedValueDisplay));
                }
            }
        }

        /// <summary>
        /// Text shown in the small status label above the results list.
        /// </summary>
        public string StatusText
        {
            get => statusText;
            set
            {
                if (!string.Equals(statusText, value, StringComparison.Ordinal))
                {
                    statusText = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// True when there is a selected listing.
        /// Used to control visibility of the Est. pill.
        /// </summary>
        public bool HasSelectedListing => SelectedListing != null;

        /// <summary>
        /// Display value for the Est. pill under the selected listing.
        /// - If an EstimatedValue exists, use it.
        /// - Otherwise, fall back to the listing's formatted price.
        /// </summary>
        public string SelectedEstimatedValueDisplay
        {
            get
            {
                if (SelectedListing == null)
                {
                    return string.Empty;
                }

                if (SelectedListing.EstimatedValue.HasValue)
                {
                    return EbayListing.FormatPrice(
                        SelectedListing.EstimatedValue,
                        SelectedListing.Currency);
                }

                // Fall back to the current price.
                return SelectedListing.FormattedPrice;
            }
        }

        /// <summary>
        /// Call this when the underlying EstimatedValue of the selected
        /// listing has changed (e.g., after the Insights overlay closes).
        /// </summary>
        public void RefreshSelectedComputedProperties()
        {
            OnPropertyChanged(nameof(SelectedEstimatedValueDisplay));
        }

        /// <summary>
        /// Helper to raise PropertyChanged.
        /// </summary>
        /// <param name="propertyName">Name of the property to raise.</param>
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                return;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
