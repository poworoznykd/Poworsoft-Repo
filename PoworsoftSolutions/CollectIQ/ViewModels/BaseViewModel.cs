//
//  FILE            : BaseViewModel.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-12-05
//  DESCRIPTION     :
//      Provides INotifyPropertyChanged support and a SetProperty
//      helper for use by all CollectIQ view models.
//

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CollectIQ.ViewModels
{
    /// <summary>
    /// Base class implementing INotifyPropertyChanged for all view models.
    /// </summary>
    public abstract class BaseViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        /*
         * FUNCTION     : SetProperty
         * DESCRIPTION  :
         *     Generic helper that assigns a new value to a backing field
         *     and raises PropertyChanged if the value actually changed.
         * PARAMETERS   :
         *     storage - reference to the backing field
         *     value   - new value to assign
         *     propertyName - automatically supplied by CallerMemberName
         * RETURNS      : bool  (true if value changed)
         */
        protected bool SetProperty<T>(
            ref T storage,
            T value,
            [CallerMemberName] string propertyName = "")
        {
            if (Equals(storage, value))
                return false;

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        /*
         * FUNCTION     : OnPropertyChanged
         * DESCRIPTION  :
         *     Raises the PropertyChanged event for a given property.
         * PARAMETERS   :
         *     propertyName - Name of the property that changed.
         * RETURNS      : void
         */
        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
