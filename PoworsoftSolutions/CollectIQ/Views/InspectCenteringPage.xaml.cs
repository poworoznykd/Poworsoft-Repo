/*
* FILE: InspectCenteringPage.xaml.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* FIRST VERSION: 2025-12-14
* DESCRIPTION:
*     Page for visually inspecting card centering.
*     - Shows card image with purple neon guides.
*     - Binds to InspectCenteringViewModel for metrics
*       and actions (auto-analyze, manual fine-tune).
*/

using CollectIQ.Helpers;
using CollectIQ.Utilities;
using CollectIQ.ViewModels;
using Microsoft.Maui.Controls;

namespace CollectIQ.Views
{
    public partial class InspectCenteringPage : ContentPage
    {
        public InspectCenteringPage()
        {
            InitializeComponent();

            // Prefer DI if configured; fall back to direct construction.
            var resolvedViewModel =
                ServiceHelper.Services?.GetService(typeof(InspectCenteringViewModel)) as InspectCenteringViewModel;

            BindingContext = resolvedViewModel ?? new InspectCenteringViewModel();
        }
    }
}
