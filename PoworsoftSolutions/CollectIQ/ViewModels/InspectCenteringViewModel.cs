/*
* FILE: InspectCenteringViewModel.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-12-14
* DESCRIPTION:
*     View model backing the InspectCenteringPage.
*     - Exposes card image, centering metrics, and recommendations.
*     - Provides Auto Analyze and Manual Fine-Tune commands.
*     - Currently uses placeholder logic; ready to plug in
*       machine vision (OpenCV / EmguCV / etc.) for real analysis.
*/

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace CollectIQ.Views
{
    public class InspectCenteringViewModel : INotifyPropertyChanged
    {
        // ============================================================
        //  FIELDS
        // ============================================================

        private ImageSource cardImageSource;
        private double horizontalCenterPercent;
        private double verticalCenterPercent;
        private string centeringSummary;
        private string horizontalCenteringText;
        private string verticalCenteringText;
        private string recommendation;
        private double zoomLevel;
        private double tolerance;

        // ============================================================
        //  CONSTRUCTOR
        // ============================================================

        public InspectCenteringViewModel()
        {
            // Placeholder sample card image; you can bind a real card later.
            cardImageSource = "sample_card_front.png";

            // Start at “perfect” to show format.
            horizontalCenterPercent = 50.0;
            verticalCenterPercent = 50.0;
            zoomLevel = 1.4;
            tolerance = 3.0;

            UpdateTextFromMetrics();

            AnalyzeCommand = new Command(ExecuteAnalyze);
            ManualCommand = new Command(ExecuteManual);
        }

        // ============================================================
        //  PUBLIC PROPERTIES
        // ============================================================

        public event PropertyChangedEventHandler PropertyChanged;

        public ImageSource CardImageSource
        {
            get { return cardImageSource; }
            set
            {
                if (cardImageSource != value)
                {
                    cardImageSource = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Horizontal centering in percent (50% is perfect).
        /// </summary>
        public double HorizontalCenterPercent
        {
            get { return horizontalCenterPercent; }
            set
            {
                if (Math.Abs(horizontalCenterPercent - value) > double.Epsilon)
                {
                    horizontalCenterPercent = value;
                    OnPropertyChanged();
                    UpdateTextFromMetrics();
                }
            }
        }

        /// <summary>
        /// Vertical centering in percent (50% is perfect).
        /// </summary>
        public double VerticalCenterPercent
        {
            get { return verticalCenterPercent; }
            set
            {
                if (Math.Abs(verticalCenterPercent - value) > double.Epsilon)
                {
                    verticalCenterPercent = value;
                    OnPropertyChanged();
                    UpdateTextFromMetrics();
                }
            }
        }

        public string CenteringSummary
        {
            get { return centeringSummary; }
            private set
            {
                if (centeringSummary != value)
                {
                    centeringSummary = value;
                    OnPropertyChanged();
                }
            }
        }

        public string HorizontalCenteringText
        {
            get { return horizontalCenteringText; }
            private set
            {
                if (horizontalCenteringText != value)
                {
                    horizontalCenteringText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string VerticalCenteringText
        {
            get { return verticalCenteringText; }
            private set
            {
                if (verticalCenteringText != value)
                {
                    verticalCenteringText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Recommendation
        {
            get { return recommendation; }
            private set
            {
                if (recommendation != value)
                {
                    recommendation = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Zoom factor for the card preview (bound to slider).
        /// </summary>
        public double ZoomLevel
        {
            get { return zoomLevel; }
            set
            {
                if (Math.Abs(zoomLevel - value) > double.Epsilon)
                {
                    zoomLevel = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Tolerance in percent for what counts as "well centered".
        /// </summary>
        public double Tolerance
        {
            get { return tolerance; }
            set
            {
                if (Math.Abs(tolerance - value) > double.Epsilon)
                {
                    tolerance = value;
                    OnPropertyChanged();
                    UpdateTextFromMetrics();
                }
            }
        }

        // ============================================================
        //  COMMANDS
        // ============================================================

        public ICommand AnalyzeCommand { get; }

        public ICommand ManualCommand { get; }

        // ============================================================
        //  COMMAND HANDLERS
        // ============================================================

        /*
         * FUNCTION     : ExecuteAnalyze
         * DESCRIPTION  :
         *     Placeholder "auto analyze" logic. Right now it generates
         *     centered values with a small random offset to simulate
         *     what a real CV pipeline might return. Replace this with
         *     actual image processing (e.g., OpenCV) once integrated.
         */
        private void ExecuteAnalyze()
        {
            // Simulate a slight off-center result to show the flow.
            Random random = new Random();

            double maxOffset = Tolerance; // e.g., +/- 3% by default
            double offsetX = random.NextDouble() * maxOffset * 2 - maxOffset;
            double offsetY = random.NextDouble() * maxOffset * 2 - maxOffset;

            HorizontalCenterPercent = 50.0 + offsetX;
            VerticalCenterPercent = 50.0 + offsetY;

            UpdateTextFromMetrics();
        }

        /*
         * FUNCTION     : ExecuteManual
         * DESCRIPTION  :
         *     Placeholder for a future "manual fine-tune" mode where
         *     the user can drag guides or input precise edge distances.
         *     For now it simply nudges the tolerance and refreshes
         *     the summary so the button actually does something.
         */
        private void ExecuteManual()
        {
            // Simple UX placeholder: tighten tolerance a bit to show
            // that more strict grading is possible.
            if (Tolerance > 1.0)
            {
                Tolerance -= 0.5;
            }

            UpdateTextFromMetrics();
        }

        // ============================================================
        //  PRIVATE HELPERS
        // ============================================================

        private void UpdateTextFromMetrics()
        {
            double deltaX = Math.Abs(HorizontalCenterPercent - 50.0);
            double deltaY = Math.Abs(VerticalCenterPercent - 50.0);

            HorizontalCenteringText =
                $"{HorizontalCenterPercent:F1}% (Δ {deltaX:F1}%)";
            VerticalCenteringText =
                $"{VerticalCenterPercent:F1}% (Δ {deltaY:F1}%)";

            double worstDelta = Math.Max(deltaX, deltaY);

            string grade;
            string detail;

            if (worstDelta <= Tolerance)
            {
                grade = "Excellent";
                detail = "Centering looks very strong and should meet most grading thresholds.";
            }
            else if (worstDelta <= Tolerance + 3)
            {
                grade = "Good";
                detail = "Slightly off-center but still within a range many graders accept.";
            }
            else
            {
                grade = "Poor";
                detail = "Noticeable centering issues. This may hold the overall grade back.";
            }

            CenteringSummary = $"Centering Grade: {grade} (ΔX {deltaX:F1}%, ΔY {deltaY:F1}%)";
            Recommendation = detail;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
