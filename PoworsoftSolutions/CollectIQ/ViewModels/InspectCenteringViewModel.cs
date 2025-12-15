//
//  FILE            : InspectCenteringViewModel.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-12-14
//  DESCRIPTION     :
//      ViewModel for the Inspect Centering page.
//      - Tracks card title / subtitle / images.
//      - Maintains horizontal and vertical centering metrics.
//      - Exposes formatted ratio text and a combined summary.
//      - Intended to be driven by the page's gesture events,
//        which provide the calculated border insets in pixels.
//

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;

namespace CollectIQ.ViewModels
{
    public class InspectCenteringViewModel : INotifyPropertyChanged
    {
        // -----------------------------------------------------------------
        //  FIELDS
        // -----------------------------------------------------------------

        private string cardTitle;
        private string cardSubtitle;
        private string centeringSummary;

        private string horizontalRatioText;
        private string verticalRatioText;

        private ImageSource cardImageSource;
        private ImageSource cardThumbnailSource;

        private double lastLeftInset;
        private double lastRightInset;
        private double lastTopInset;
        private double lastBottomInset;

        // -----------------------------------------------------------------
        //  PUBLIC PROPERTIES
        // -----------------------------------------------------------------

        public string CardTitle
        {
            get => cardTitle;
            set => SetProperty(ref cardTitle, value);
        }

        public string CardSubtitle
        {
            get => cardSubtitle;
            set => SetProperty(ref cardSubtitle, value);
        }

        public string CenteringSummary
        {
            get => centeringSummary;
            set => SetProperty(ref centeringSummary, value);
        }

        public string HorizontalRatioText
        {
            get => horizontalRatioText;
            set => SetProperty(ref horizontalRatioText, value);
        }

        public string VerticalRatioText
        {
            get => verticalRatioText;
            set => SetProperty(ref verticalRatioText, value);
        }

        public ImageSource CardImageSource
        {
            get => cardImageSource;
            set => SetProperty(ref cardImageSource, value);
        }

        public ImageSource CardThumbnailSource
        {
            get => cardThumbnailSource;
            set => SetProperty(ref cardThumbnailSource, value);
        }

        // -----------------------------------------------------------------
        //  CONSTRUCTOR
        // -----------------------------------------------------------------

        public InspectCenteringViewModel()
        {
            // Default text – you can override this when loading a card.
            CardTitle = "Player Name • 2020 Prizm";
            CardSubtitle = "Base • Silver Prizm";
            CenteringSummary = "Drag the lines to match the printed borders.";

            HorizontalRatioText = "Left 50% / Right 50%";
            VerticalRatioText = "Top 50% / Bottom 50%";
        }

        // -----------------------------------------------------------------
        //  PUBLIC METHODS
        // -----------------------------------------------------------------

        /*
         * FUNCTION     : InitializeFromCard
         * DESCRIPTION  :
         *     Allows the caller to populate this ViewModel with card data.
         *     You can wire this to your Card model when navigating to the
         *     Inspect Centering page.
         */
        public void InitializeFromCard(
            ImageSource mainImage,
            ImageSource thumbnailImage,
            string title,
            string subtitle)
        {
            CardImageSource = mainImage;
            CardThumbnailSource = thumbnailImage;
            CardTitle = title;
            CardSubtitle = subtitle;
        }

        /*
         * FUNCTION     : UpdateHorizontalInsets
         * DESCRIPTION  :
         *     Accepts the measured left and right border insets in pixels
         *     and computes the horizontal centering percentages.
         *     This method is typically called by the page after the user
         *     drags the left/right guides.
         */
        public void UpdateHorizontalInsets(double leftInsetPixels, double rightInsetPixels)
        {
            if (leftInsetPixels < 0)
            {
                leftInsetPixels = 0;
            }

            if (rightInsetPixels < 0)
            {
                rightInsetPixels = 0;
            }

            lastLeftInset = leftInsetPixels;
            lastRightInset = rightInsetPixels;

            double totalBorder = leftInsetPixels + rightInsetPixels;

            double leftPercent;
            double rightPercent;

            if (totalBorder <= 0.5)
            {
                leftPercent = 50.0;
                rightPercent = 50.0;
            }
            else
            {
                leftPercent = (leftInsetPixels / totalBorder) * 100.0;
                rightPercent = 100.0 - leftPercent;
            }

            leftPercent = Math.Round(leftPercent, 1);
            rightPercent = Math.Round(rightPercent, 1);

            HorizontalRatioText = $"Left {leftPercent}% / Right {rightPercent}%";

            UpdateSummary();
        }

        /*
         * FUNCTION     : UpdateVerticalInsets
         * DESCRIPTION  :
         *     Accepts the measured top and bottom border insets in pixels
         *     and computes the vertical centering percentages.
         *     This method is typically called by the page after the user
         *     drags the top/bottom guides.
         */
        public void UpdateVerticalInsets(double topInsetPixels, double bottomInsetPixels)
        {
            if (topInsetPixels < 0)
            {
                topInsetPixels = 0;
            }

            if (bottomInsetPixels < 0)
            {
                bottomInsetPixels = 0;
            }

            lastTopInset = topInsetPixels;
            lastBottomInset = bottomInsetPixels;

            double totalBorder = topInsetPixels + bottomInsetPixels;

            double topPercent;
            double bottomPercent;

            if (totalBorder <= 0.5)
            {
                topPercent = 50.0;
                bottomPercent = 50.0;
            }
            else
            {
                topPercent = (topInsetPixels / totalBorder) * 100.0;
                bottomPercent = 100.0 - topPercent;
            }

            topPercent = Math.Round(topPercent, 1);
            bottomPercent = Math.Round(bottomPercent, 1);

            VerticalRatioText = $"Top {topPercent}% / Bottom {bottomPercent}%";

            UpdateSummary();
        }

        // -----------------------------------------------------------------
        //  PRIVATE HELPERS
        // -----------------------------------------------------------------

        /*
         * FUNCTION     : UpdateSummary
         * DESCRIPTION  :
         *     Builds a concise overview string from the current horizontal
         *     and vertical ratio text and stores it in CenteringSummary.
         */
        private void UpdateSummary()
        {
            CenteringSummary =
                $"Horizontal: {HorizontalRatioText}   •   Vertical: {VerticalRatioText}";
        }

        // -----------------------------------------------------------------
        //  INotifyPropertyChanged IMPLEMENTATION
        // -----------------------------------------------------------------

        public event PropertyChangedEventHandler PropertyChanged;

        private void SetProperty<T>(ref T backingField, T value,
            [CallerMemberName] string propertyName = "")
        {
            if (Equals(backingField, value))
            {
                return;
            }

            backingField = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
