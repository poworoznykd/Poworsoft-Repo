//
//  FILE            : MainPage.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : <Your Name>
//  FIRST VERSION   : 2025-10-18
//  DESCRIPTION     :
//      Landing page with gradient, logo, and CTA to navigate into the app.
//

namespace CollectIQ
{
    /// <summary>
    /// Landing page that navigates to the Add Card page.
    /// </summary>
    public partial class MainPage : ContentPage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MainPage"/> class.
        /// </summary>
        public MainPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Handles "Get Started" button click; navigates to AddCardPage.
        /// </summary>
        /// <param name="sender">Event source.</param>
        /// <param name="e">Event args.</param>
        private async void OnGetStartedClicked(object sender, EventArgs e)
        {
            // If using Shell routes, ensure AddCardPage is registered in AppShell.xaml.
            await Shell.Current.GoToAsync("//AddCardPage");
        }
    }
}
