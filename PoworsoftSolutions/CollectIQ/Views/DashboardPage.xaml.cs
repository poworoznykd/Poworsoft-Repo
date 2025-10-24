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

        public Command AddCardCommand => new Command(async () =>
        {
            await DisplayAlert("Coming Soon", "Card addition feature in development.", "OK");
        });

        public Command ViewInventoryCommand => new Command(async () =>
        {
            await DisplayAlert("Inventory", "View your collection stats soon.", "OK");
        });
    }
}
