/*
 *  FILE        : CameraViewModel.cs
 *  PROJECT     : CollectIQ (Mobile Application)
 *  PROGRAMMER  : Darryl Poworoznyk
 *  DATE        : 2025-10-27
 *  DESCRIPTION :
 *      Provides a view model for binding to the CommunityToolkit.MAUI.CameraView control.
 *      Exposes properties for selected camera, zoom factor, flash mode, and resolution.
 *      Implements INotifyPropertyChanged for reactive UI updates.
 */

using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;

namespace CollectIQ.ViewModels
{
    /// <summary>
    /// ViewModel for the CameraView providing binding support for SelectedCamera,
    /// CurrentZoom, FlashMode, and SelectedResolution.
    /// </summary>
    public class CameraViewModel : INotifyPropertyChanged
    {
        // ============================================================
        // Fields
        // ============================================================

        private CameraInfo _selectedCamera;
        private double _currentZoom = 1.0;
        private CameraFlashMode _flashMode = CameraFlashMode.Auto;
        private Size _selectedResolution;

        // ============================================================
        // Properties (with SET-3.2 summaries)
        // ============================================================

        /// <summary>
        /// Gets or sets the currently selected camera (front or back).
        /// </summary>
        public CameraInfo SelectedCamera
        {
            get => _selectedCamera;
            set
            {
                _selectedCamera = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Gets or sets the current zoom factor applied to the camera.
        /// </summary>
        public double CurrentZoom
        {
            get => _currentZoom;
            set
            {
                _currentZoom = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Gets or sets the current flash mode (Auto, On, Off).
        /// </summary>
        public CameraFlashMode FlashMode
        {
            get => _flashMode;
            set
            {
                _flashMode = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Gets or sets the selected capture resolution of the camera.
        /// </summary>
        public Size SelectedResolution
        {
            get => _selectedResolution;
            set
            {
                _selectedResolution = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Gets a list of all supported flash modes.
        /// </summary>
        public List<CameraFlashMode> FlashModes { get; } =
            new() { CameraFlashMode.Auto, CameraFlashMode.On, CameraFlashMode.Off };

        // ============================================================
        // Event & Helper
        // ============================================================

        /// <summary>
        /// Event raised when a bound property changes.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Raises PropertyChanged for the specified property.
        /// </summary>
        /// <param name="propertyName">Name of the property changed</param>
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
