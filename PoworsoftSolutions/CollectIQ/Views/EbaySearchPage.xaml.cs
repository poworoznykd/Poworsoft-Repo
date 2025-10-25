//
//  FILE            : EbaySearchPage.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  LAST UPDATED    : 2025-10-26
//  DESCRIPTION     :
//      Displays mock eBay results and prepares for
//      integration with real eBay API or scraping.
//
using Microsoft.Maui.Controls;
using System;

namespace CollectIQ.Views
{
    [QueryProperty(nameof(Query), "query")]
    public partial class EbaySearchPage : ContentPage
    {
        public string Query { get; set; } = "";

        public EbaySearchPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigatedToEventArgs args)
        {
            base.OnNavigatedTo(args);
            if (!string.IsNullOrEmpty(Query))
            {
                EbayQueryEntry.Text = Query;
                EbayResultLabel.Text = $"Searching for: {Query}\n(Mock results coming soon)";
            }
        }

        private async void OnSearchClicked(object sender, EventArgs e)
        {
            string term = EbayQueryEntry.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(term))
            {
                await DisplayAlert("Error", "Please enter a search term.", "OK");
                return;
            }

            EbayResultLabel.Text = $"Searching for: {term}\n(Mock results coming soon)";
        }
    }
}
