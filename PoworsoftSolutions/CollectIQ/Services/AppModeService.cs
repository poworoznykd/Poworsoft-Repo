//
//  FILE            : AppModeService.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-12-13
//  DESCRIPTION     :
//      Central service for tracking the current high-level app mode
//      (Collect, Inspect, Trade). UI elements like the top mode toggle
//      bar and the futuristic bottom nav bar subscribe to this service
//      so they stay in sync.
//

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CollectIQ.Navigation;

namespace CollectIQ.Services
{
    public sealed class AppModeService : INotifyPropertyChanged
    {
        private AppMode currentMode = AppMode.Collect;

        /// <summary>
        /// Raised whenever the current mode changes.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Event-based notification for consumers that prefer an explicit callback.
        /// </summary>
        public event EventHandler<AppMode>? ModeChanged;

        /// <summary>
        /// Gets or sets the current application mode.
        /// Changing this property notifies listeners via PropertyChanged
        /// and ModeChanged.
        /// </summary>
        public AppMode CurrentMode
        {
            get => currentMode;
            set
            {
                if (currentMode == value)
                {
                    return;
                }

                currentMode = value;
                OnPropertyChanged();
                ModeChanged?.Invoke(this, currentMode);
            }
        }

        /// <summary>
        /// Helper method for raising PropertyChanged.
        /// </summary>
        /// <param name="propertyName">Name of the changed property.</param>
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
