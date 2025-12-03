using System;
using System.Threading.Tasks;

namespace CollectIQ.Controls
{
    public partial class FilterOverlayControl : ContentView
    {
        public event EventHandler? FiltersChanged;
        public event EventHandler<FilterOptions> FiltersApplied;
        public FilterOverlayControl()
        {
            InitializeComponent();
            FilterOverlay.IsVisible = false;
            FilterScrim.IsVisible = false;
            FilterOverlay.Opacity = 0;
            FilterScrim.Opacity = 0;
            WireEvents();
        }

        private void WireEvents()
        {
            // Text changes
            SearchEntry.TextChanged += (_, __) => RaiseChanged();

            // CheckBoxes
            ChkHockey.CheckedChanged += (_, __) => RaiseChanged();
            ChkFootball.CheckedChanged += (_, __) => RaiseChanged();
            ChkBasketball.CheckedChanged += (_, __) => RaiseChanged();
            ChkPokemon.CheckedChanged += (_, __) => RaiseChanged();

            // Sliders
            YearMinSlider.ValueChanged += (_, __) => UpdateYearLabel();
            YearMaxSlider.ValueChanged += (_, __) => UpdateYearLabel();

            ValueMinSlider.ValueChanged += (_, __) => UpdateValueLabel();
            ValueMaxSlider.ValueChanged += (_, __) => UpdateValueLabel();

            ApplyBtn.Clicked += OnApplyClicked;
        }

        // ------------------------------------------------------
        // LABEL UPDATES
        // ------------------------------------------------------

        private void UpdateYearLabel()
        {
            LblYearRange.Text = $"{(int)YearMinSlider.Value} - {(int)YearMaxSlider.Value}";
            RaiseChanged();
        }

        private void UpdateValueLabel()
        {
            LblValueRange.Text = $"${(int)ValueMinSlider.Value} - ${(int)ValueMaxSlider.Value}";
            RaiseChanged();
        }

        // ------------------------------------------------------
        // PUBLIC FILTER PROPERTIES
        // ------------------------------------------------------

        public string Search => SearchEntry.Text ?? "";

        public bool Hockey => ChkHockey.IsChecked;
        public bool Football => ChkFootball.IsChecked;
        public bool Basketball => ChkBasketball.IsChecked;
        public bool Pokemon => ChkPokemon.IsChecked;

        public int MinYear => (int)YearMinSlider.Value;
        public int MaxYear => (int)YearMaxSlider.Value;

        public decimal MinValue => (decimal)ValueMinSlider.Value;
        public decimal MaxValue => (decimal)ValueMaxSlider.Value;

        private void RaiseChanged()
            => FiltersChanged?.Invoke(this, EventArgs.Empty);

        // ------------------------------------------------------
        // SHOW / HIDE ANIMATIONS
        // ------------------------------------------------------

        public async Task ShowAsync()
        {
            FilterOverlay.IsVisible = true;
            FilterScrim.IsVisible = true;

            FilterOverlay.Opacity = 0;
            FilterOverlay.TranslationY = 60;
            FilterScrim.Opacity = 0;

            await Task.WhenAll(
                FilterOverlay.FadeTo(1, 180, Easing.CubicOut),
                FilterOverlay.TranslateTo(0, 0, 180, Easing.CubicOut),
                FilterScrim.FadeTo(1, 180, Easing.CubicOut));
        }

        private async void OnScrimTapped(object sender, TappedEventArgs e)
        {
            await HideAsync();
        }

        public async Task HideAsync()
        {
            await Task.WhenAll(
                FilterScrim.FadeTo(0, 150, Easing.CubicIn),
                FilterOverlay.FadeTo(0, 150, Easing.CubicIn),
                FilterOverlay.TranslateTo(0, 60, 150, Easing.CubicIn));

            FilterOverlay.IsVisible = false;
            FilterScrim.IsVisible = false;

        }

        private void OnClearClicked(object sender, EventArgs e)
        {
            SearchEntry.Text = "";
            ChkHockey.IsChecked = false;
            ChkFootball.IsChecked = false;
            ChkBasketball.IsChecked = false;
            ChkPokemon.IsChecked = false;
            YearMinSlider.Value = 1900;
            YearMaxSlider.Value = 2050;
            ValueMinSlider.Value = 0;
            ValueMaxSlider.Value = 5000;

            FiltersApplied?.Invoke(this, new FilterOptions());
            _ = HideAsync();
        }

        private void OnApplyClicked(object sender, EventArgs e)
        {
            var options = new FilterOptions()
            {
                Search = SearchEntry.Text,
                Hockey = ChkHockey.IsChecked,
                Football = ChkFootball.IsChecked,
                Basketball = ChkBasketball.IsChecked,
                Pokemon = ChkPokemon.IsChecked,
                MinYear = (int)YearMinSlider.Value,
                MaxYear = (int)YearMaxSlider.Value,
                MinValue = (int)ValueMinSlider.Value,
                MaxValue = (int)ValueMaxSlider.Value
            };

            FiltersApplied?.Invoke(this, options);

            _ = HideAsync(); // close overlay
        }


        private async void OnCloseClicked(object? sender, EventArgs e)
        {
            await HideAsync();
        }
    }
   

    public class FilterOptions
    {
        public string Search { get; set; }
        public bool Hockey { get; set; }
        public bool Football { get; set; }
        public bool Basketball { get; set; }
        public bool Pokemon { get; set; }
        public int MinYear { get; set; }
        public int MaxYear { get; set; }
        public int MinValue { get; set; }
        public int MaxValue { get; set; }
    }

}
