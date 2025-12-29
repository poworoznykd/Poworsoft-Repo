//
//  FILE            : UnderConstructionPage.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2026-01-18
//  DESCRIPTION     :
//      Reusable "Under Construction" screen.
//      - Shows a generic message that a feature / lane is not ready yet.
//      - Has a Back button that simply pops the modal and returns the user
//        to whatever page they were on before.
//
using System;
using Microsoft.Maui.Controls;

namespace CollectIQ.Views
{
    public partial class UnderConstructionPage : ContentPage
    {
        public UnderConstructionPage(string contextLabel)
        {
            InitializeComponent();

            // Context label is something like "Inspect lane" or "Trade lane".
            HeaderLabel.Text = $"{contextLabel} coming soon";

            BodyLabel.Text =
                $"We're still building the {contextLabel.ToLower()} for CollectIQ.\n\n" +
                "You’ll be able to use this lane in a future update, but for now " +
                "feel free to continue using the rest of the app.";
        }

        private async void OnBackClicked(object sender, EventArgs e)
        {
            // This just closes the modal and returns to whatever page was underneath.
            await Navigation.PopModalAsync(animated: true);
        }
    }
}
