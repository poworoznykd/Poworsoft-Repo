using Microsoft.Maui.Controls;

namespace CollectIQ.Views
{
    public partial class DashboardPage : ContentPage
    {
        public DashboardPage()
        {
            InitializeComponent();

            BindingContext = this;
        }

        // Navigate to the ScanPage
        public Command ScanCardCommand => new(async () =>
        {
            await Shell.Current.GoToAsync(nameof(ScanPage));
        });

        // Navigate to the eBay Search Page (comparison)
        public Command EbayCompareCommand => new(async () =>
        {
            await Shell.Current.GoToAsync(nameof(EbaySearchPage));
        });

        // Navigate to My Collection
        public Command ViewCollectionCommand => new(async () =>
        {
            await Shell.Current.GoToAsync(nameof(CollectionPage));
        });
    }
}
