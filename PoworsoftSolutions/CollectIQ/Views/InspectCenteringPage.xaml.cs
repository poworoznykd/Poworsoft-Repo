//
//  FILE            : InspectCenteringPage.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-12-14
//  DESCRIPTION     :
//      Code-behind for the Inspect Centering page.
//      - Hosts the InspectCenteringViewModel as BindingContext.
//      - Handles pinch and pan gestures at the view layer.
//      - Converts guide translations into pixel insets and forwards
//        them to the ViewModel for centering calculations.
//

using CollectIQ.Models;
using CollectIQ.ViewModels;
using Microsoft.Maui.Controls;
using System;

namespace CollectIQ.Pages
{
    public partial class InspectCenteringPage : ContentPage
    {
        // ViewModel instance bound to this page.
        private readonly InspectCenteringViewModel viewModel;

        // Size of the canvas the guides live in.
        private double cardCanvasWidth;
        private double cardCanvasHeight;

        // Pan starting offsets.
        private double panStartX;
        private double panStartY;

        // Pinch starting scale.
        private double pinchStartScale = 1.0;

        // -----------------------------------------------------------------
        //  CONSTRUCTOR
        // -----------------------------------------------------------------

        public InspectCenteringPage()
        {
            InitializeComponent();

            viewModel = new InspectCenteringViewModel();
            BindingContext = viewModel;
        }

        // -----------------------------------------------------------------
        //  PUBLIC INITIALIZER
        // -----------------------------------------------------------------

        /*
         * FUNCTION     : InitializeFromCard
         * DESCRIPTION  :
         *     Convenience wrapper to forward card data to the ViewModel.
         *     Call this method when navigating to the Inspect Centering
         *     page so the correct images and text are displayed.
         */
        public void InitializeFromCard(
            ImageSource mainImage,
            ImageSource thumbnailImage,
            string title,
            string subtitle)
        {
            viewModel.InitializeFromCard(mainImage, thumbnailImage, title, subtitle);
        }

        // -----------------------------------------------------------------
        //  LAYOUT / SIZING
        // -----------------------------------------------------------------

        private void OnCardCanvasSizeChanged(object sender, EventArgs e)
        {
            cardCanvasWidth = cardCanvas.Width;
            cardCanvasHeight = cardCanvas.Height;
        }

        // -----------------------------------------------------------------
        //  PINCH TO ZOOM
        // -----------------------------------------------------------------

        private void OnCardImagePinchUpdated(object sender, PinchGestureUpdatedEventArgs e)
        {
            if (e.Status == GestureStatus.Started)
            {
                pinchStartScale = cardImage.Scale;
                return;
            }

            if (e.Status == GestureStatus.Running)
            {
                double newScale = pinchStartScale * e.Scale;

                if (newScale < 1.0)
                {
                    newScale = 1.0;
                }
                else if (newScale > 4.0)
                {
                    newScale = 4.0;
                }

                cardImage.Scale = newScale;
            }
        }

        // -----------------------------------------------------------------
        //  GUIDE PANNING – HORIZONTAL
        // -----------------------------------------------------------------

        private void OnHorizontalGuidePanUpdated(object sender, PanUpdatedEventArgs e)
        {
            BoxView guide = sender as BoxView;
            if (guide == null)
            {
                return;
            }

            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    panStartX = guide.TranslationX;
                    break;

                case GestureStatus.Running:
                    double newX = panStartX + e.TotalX;

                    double maxInset = cardCanvasWidth * 0.4;
                    if (newX > maxInset)
                    {
                        newX = maxInset;
                    }

                    if (newX < -maxInset)
                    {
                        newX = -maxInset;
                    }

                    guide.TranslationX = newX;

                    // After moving one guide, recompute both left/right insets
                    // and forward to the ViewModel.
                    UpdateHorizontalMetrics();
                    break;
            }
        }

        private void UpdateHorizontalMetrics()
        {
            // Left guide starts at left edge and moves inward (+X).
            double leftInset = leftGuide.TranslationX;
            if (leftInset < 0)
            {
                leftInset = 0;
            }

            // Right guide starts at right edge and moves inward (-X).
            double rightInset = -rightGuide.TranslationX;
            if (rightInset < 0)
            {
                rightInset = 0;
            }

            viewModel.UpdateHorizontalInsets(leftInset, rightInset);
        }

        // -----------------------------------------------------------------
        //  GUIDE PANNING – VERTICAL
        // -----------------------------------------------------------------

        private void OnVerticalGuidePanUpdated(object sender, PanUpdatedEventArgs e)
        {
            BoxView guide = sender as BoxView;
            if (guide == null)
            {
                return;
            }

            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    panStartY = guide.TranslationY;
                    break;

                case GestureStatus.Running:
                    double newY = panStartY + e.TotalY;

                    double maxInset = cardCanvasHeight * 0.4;
                    if (newY > maxInset)
                    {
                        newY = maxInset;
                    }

                    if (newY < -maxInset)
                    {
                        newY = -maxInset;
                    }

                    guide.TranslationY = newY;

                    UpdateVerticalMetrics();
                    break;
            }
        }

        private void UpdateVerticalMetrics()
        {
            // Top guide starts at top edge and moves down (+Y).
            double topInset = topGuide.TranslationY;
            if (topInset < 0)
            {
                topInset = 0;
            }

            // Bottom guide starts at bottom edge and moves up (-Y).
            double bottomInset = -bottomGuide.TranslationY;
            if (bottomInset < 0)
            {
                bottomInset = 0;
            }

            viewModel.UpdateVerticalInsets(topInset, bottomInset);
        }
    }
}
