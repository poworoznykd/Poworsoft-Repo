using CollectIQ.Helpers;
using CollectIQ.Utilities;

namespace CollectIQ.Views
{
    public partial class InspectCenteringPage : ContentPage
    {
        public InspectCenteringPage()
        {
            InitializeComponent();

            var resolvedViewModel =
                ServiceHelper.Services?.GetService(typeof(InspectCenteringViewModel)) as InspectCenteringViewModel;

            BindingContext = resolvedViewModel ?? new InspectCenteringViewModel();
        }
    }
}
