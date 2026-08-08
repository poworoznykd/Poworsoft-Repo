/*
* FILE: InspectHubPage.xaml.cs
* PROJECT: CollectIQ (Mobile Application)
* PROGRAMMER: Darryl Poworoznyk
* DESCRIPTION:
*     Landing page for the Inspect lane. Keeps Centering, Corners, Edges and
*     Surface as independent modules so each can be validated separately.
*/

namespace CollectIQ.Views
{
    public partial class InspectHubPage : ContentPage
    {
        public InspectHubPage()
        {
            InitializeComponent();
        }

        private async void OnCenteringClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(InspectCenteringPage));
        }

        private async void OnCornersClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(InspectCornersPage));
        }

        private async void OnEdgesClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(InspectEdgesPage));
        }

        private async void OnSurfaceClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(InspectSurfacePage));
        }
    }
}
