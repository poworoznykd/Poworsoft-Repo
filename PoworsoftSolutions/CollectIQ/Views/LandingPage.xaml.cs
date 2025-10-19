using Microsoft.Maui.Controls;
using System;
using System.Threading.Tasks;

namespace CollectIQ.Views
{
    public partial class LandingPage : ContentPage
    {
        private bool _animated;

        public LandingPage()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            if (_animated) return;
            _animated = true;

            try
            {
                // Set initial states here (not in XAML) so the UI is visible
                LogoImage.Opacity = 0;
                TitleLabel.Opacity = 0;
                SubtitleLabel.Opacity = 0;
                EnterButton.Opacity = 0;

                await LogoImage.FadeTo(1, 600, Easing.CubicOut);
                await Task.WhenAll(
                    TitleLabel.FadeTo(1, 400, Easing.CubicOut),
                    SubtitleLabel.FadeTo(1, 450, Easing.CubicOut),
                    EnterButton.FadeTo(1, 500, Easing.CubicOut)
                );
            }
            catch
            {
                // Never let animation failures break the page
                LogoImage.Opacity = TitleLabel.Opacity = SubtitleLabel.Opacity = EnterButton.Opacity = 1;
            }
        }

        private async void OnEnter(object sender, EventArgs e)
        {
            EnterButton.IsEnabled = false;
            await EnterButton.ScaleTo(0.98, 80);
            await EnterButton.ScaleTo(1.0, 80);
            Application.Current.MainPage = new AppShell();
        }
    }
}
