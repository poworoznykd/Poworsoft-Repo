using System;
using System.IO;
using Microsoft.Maui.Controls;

namespace CollectIQ.Controls
{
    public partial class ProfileSummaryCard : ContentView
    {
        public event EventHandler? ProfileTapped;

        public ProfileSummaryCard()
        {
            InitializeComponent();

            // Bind this control to itself so XAML bindings work without RelativeSource
            BindingContext = this;

            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) => ProfileTapped?.Invoke(this, EventArgs.Empty);
            GestureRecognizers.Add(tap);
        }

        // ------------------------------------------------------------
        // Bindable Properties (Current Names)
        // ------------------------------------------------------------

        public static readonly BindableProperty AvatarSourceProperty =
            BindableProperty.Create(
                nameof(AvatarSource),
                typeof(ImageSource),
                typeof(ProfileSummaryCard),
                default(ImageSource));

        public ImageSource AvatarSource
        {
            get => (ImageSource)GetValue(AvatarSourceProperty);
            set => SetValue(AvatarSourceProperty, value);
        }

        public static readonly BindableProperty DisplayNameProperty =
            BindableProperty.Create(
                nameof(DisplayName),
                typeof(string),
                typeof(ProfileSummaryCard),
                "Collector");

        public string DisplayName
        {
            get => (string)GetValue(DisplayNameProperty);
            set => SetValue(DisplayNameProperty, value);
        }

        public static readonly BindableProperty IsVerifiedProperty =
            BindableProperty.Create(nameof(IsVerified), typeof(bool), typeof(ProfileSummaryCard), false);

        public bool IsVerified
        {
            get => (bool)GetValue(IsVerifiedProperty);
            set => SetValue(IsVerifiedProperty, value);
        }

        public static readonly BindableProperty RatingProperty =
            BindableProperty.Create(nameof(Rating), typeof(double), typeof(ProfileSummaryCard), 0.0);

        public double Rating
        {
            get => (double)GetValue(RatingProperty);
            set => SetValue(RatingProperty, value);
        }

        public static readonly BindableProperty RatingCountProperty =
            BindableProperty.Create(nameof(RatingCount), typeof(int), typeof(ProfileSummaryCard), 0);

        public int RatingCount
        {
            get => (int)GetValue(RatingCountProperty);
            set => SetValue(RatingCountProperty, value);
        }

        public static readonly BindableProperty TradesCompletedProperty =
            BindableProperty.Create(nameof(TradesCompleted), typeof(int), typeof(ProfileSummaryCard), 0);

        public int TradesCompleted
        {
            get => (int)GetValue(TradesCompletedProperty);
            set => SetValue(TradesCompletedProperty, value);
        }

        public static readonly BindableProperty LocationProperty =
            BindableProperty.Create(nameof(Location), typeof(string), typeof(ProfileSummaryCard), "—");

        public string Location
        {
            get => (string)GetValue(LocationProperty);
            set => SetValue(LocationProperty, value);
        }

        public static readonly BindableProperty MemberSinceProperty =
            BindableProperty.Create(nameof(MemberSince), typeof(DateTime?), typeof(ProfileSummaryCard), null);

        public DateTime? MemberSince
        {
            get => (DateTime?)GetValue(MemberSinceProperty);
            set => SetValue(MemberSinceProperty, value);
        }

        public static readonly BindableProperty CollectionValueProperty =
            BindableProperty.Create(nameof(CollectionValue), typeof(decimal), typeof(ProfileSummaryCard), 0m);

        public decimal CollectionValue
        {
            get => (decimal)GetValue(CollectionValueProperty);
            set => SetValue(CollectionValueProperty, value);
        }

        // ------------------------------------------------------------
        // Compatibility Properties (Old Names your XAML is using)
        // ------------------------------------------------------------

        // Old: AvatarPath (string path). We convert it to AvatarSource automatically.
        public static readonly BindableProperty AvatarPathProperty =
            BindableProperty.Create(
                nameof(AvatarPath),
                typeof(string),
                typeof(ProfileSummaryCard),
                string.Empty,
                propertyChanged: OnAvatarPathChanged);

        public string AvatarPath
        {
            get => (string)GetValue(AvatarPathProperty);
            set => SetValue(AvatarPathProperty, value);
        }

        private static void OnAvatarPathChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (bindable is not ProfileSummaryCard card)
                return;

            string? path = newValue as string;

            if (string.IsNullOrWhiteSpace(path))
            {
                card.AvatarSource = null;
                return;
            }

            try
            {
                // Only set if file exists; avoids crashes on missing path
                if (File.Exists(path))
                {
                    card.AvatarSource = ImageSource.FromFile(path);
                }
                else
                {
                    card.AvatarSource = null;
                }
            }
            catch
            {
                card.AvatarSource = null;
            }
        }

        // Old: Handle (string username). We map it to DisplayName.
        public static readonly BindableProperty HandleProperty =
            BindableProperty.Create(
                nameof(Handle),
                typeof(string),
                typeof(ProfileSummaryCard),
                string.Empty,
                propertyChanged: OnHandleChanged);

        public string Handle
        {
            get => (string)GetValue(HandleProperty);
            set => SetValue(HandleProperty, value);
        }

        private static void OnHandleChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (bindable is not ProfileSummaryCard card)
                return;

            string? handle = newValue as string;

            // If your UI prefers "@darryl", keep it as-is.
            // If you want to enforce '@', uncomment below:
            // if (!string.IsNullOrWhiteSpace(handle) && !handle.StartsWith("@")) handle = "@" + handle;

            card.DisplayName = string.IsNullOrWhiteSpace(handle) ? "Collector" : handle!;
        }

        // ------------------------------------------------------------
        // Computed helper texts
        // ------------------------------------------------------------

        public string RatingText => RatingCount > 0
            ? $"★ {Rating:0.0} ({RatingCount})"
            : "★ —";

        public string TradeText => TradesCompleted > 0
            ? $"{TradesCompleted} trades"
            : "0 trades";

        public string MemberSinceText => MemberSince.HasValue
            ? $"Since {MemberSince.Value:yyyy}"
            : "Since —";

        public string CollectionValueText => CollectionValue > 0
            ? $"{CollectionValue:C0}"
            : "$0";

        protected override void OnPropertyChanged(string? propertyName = null)
        {
            base.OnPropertyChanged(propertyName);

            if (propertyName == RatingProperty.PropertyName ||
                propertyName == RatingCountProperty.PropertyName ||
                propertyName == TradesCompletedProperty.PropertyName ||
                propertyName == MemberSinceProperty.PropertyName ||
                propertyName == CollectionValueProperty.PropertyName)
            {
                OnPropertyChanged(nameof(RatingText));
                OnPropertyChanged(nameof(TradeText));
                OnPropertyChanged(nameof(MemberSinceText));
                OnPropertyChanged(nameof(CollectionValueText));
            }
        }
    }
}
