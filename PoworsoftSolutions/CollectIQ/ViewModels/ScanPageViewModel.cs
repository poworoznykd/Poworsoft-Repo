/*
 * FILE: ScanPageViewModel.cs
 * PROJECT: CollectIQ (Mobile Application)
 * PROGRAMMER: Darryl Poworoznyk
 * FIRST VERSION: 2025-12-04
 * UPDATED: 2025-12-09
 * DESCRIPTION:
 *     View model for ScanPage. Manages scanning state and
 *     captured image paths for front and back card images,
 *     as well as the return-page workflow flag and capture mode
 *     (Both, FrontOnly, BackOnly) for CardPage workflows.
 */

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CollectIQ.ViewModels
{
    /*
     * CLASS   : ScanPageViewModel
     * PURPOSE :
     *     Holds the scanning state for ScanPage:
     *     - Whether scanning/animation is active
     *     - Whether the back side is being captured
     *     - Whether a capture is in progress
     *     - Captured image paths (front and back)
     *     - Return page name for workflow routing
     *     - Capture mode: "Both", "FrontOnly", or "BackOnly"
     */
    public class ScanPageViewModel : INotifyPropertyChanged
    {
        // =========================
        // Fields
        // =========================

        private bool isScanning;
        private bool isCapturingBack;
        private bool isCaptureInProgress;
        private string frontImagePath = string.Empty;
        private string backImagePath = string.Empty;
        private string? returnPageName;

        // NEW: capture mode, defaults to "Both" for existing behaviour.
        private string captureMode = "Both";

        // =========================
        // Properties
        // =========================

        public bool IsScanning
        {
            get { return isScanning; }
            set
            {
                if (isScanning != value)
                {
                    isScanning = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsCapturingBack
        {
            get { return isCapturingBack; }
            set
            {
                if (isCapturingBack != value)
                {
                    isCapturingBack = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsCaptureInProgress
        {
            get { return isCaptureInProgress; }
            set
            {
                if (isCaptureInProgress != value)
                {
                    isCaptureInProgress = value;
                    OnPropertyChanged();
                }
            }
        }

        public string FrontImagePath
        {
            get { return frontImagePath; }
            set
            {
                if (!string.Equals(frontImagePath, value, StringComparison.Ordinal))
                {
                    frontImagePath = value ?? string.Empty;
                    OnPropertyChanged();
                }
            }
        }

        public string BackImagePath
        {
            get { return backImagePath; }
            set
            {
                if (!string.Equals(backImagePath, value, StringComparison.Ordinal))
                {
                    backImagePath = value ?? string.Empty;
                    OnPropertyChanged();
                }
            }
        }

        public string? ReturnPageName
        {
            get { return returnPageName; }
            set
            {
                if (!string.Equals(returnPageName, value, StringComparison.Ordinal))
                {
                    returnPageName = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the capture mode for CardPage workflows.
        /// Allowed values:
        ///   - "Both"      : capture front then back (existing behaviour)
        ///   - "FrontOnly" : capture a single front image
        ///   - "BackOnly"  : capture a single back image
        /// </summary>
        public string CaptureMode
        {
            get { return captureMode; }
            set
            {
                string normalized = string.IsNullOrWhiteSpace(value) ? "Both" : value;

                if (!string.Equals(captureMode, normalized, StringComparison.Ordinal))
                {
                    captureMode = normalized;
                    OnPropertyChanged();
                }
            }
        }

        // =========================
        // Public Methods
        // =========================

        /*
         * FUNCTION     : InitializeForAppearing
         * DESCRIPTION  :
         *     Called by the view when the page appears to reset
         *     scanning flags and prepare for a new capture.
         * PARAMETERS   : none
         * RETURNS      : void
         */
        public void InitializeForAppearing()
        {
            IsScanning = true;
            IsCaptureInProgress = false;
        }

        /*
         * FUNCTION     : PrepareForDisappearing
         * DESCRIPTION  :
         *     Called by the view when the page disappears so that
         *     scanning/animation is stopped.
         * PARAMETERS   : none
         * RETURNS      : void
         */
        public void PrepareForDisappearing()
        {
            IsScanning = false;
        }

        // =========================
        // INotifyPropertyChanged
        // =========================

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChangedEventHandler? handler = PropertyChanged;

            if (handler != null && propertyName != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
