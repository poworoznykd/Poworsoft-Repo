using System;
using Microsoft.Maui.Controls;

namespace CollectIQ.Controls
{
    public partial class ProfileSummaryCard : ContentView
    {
        public event EventHandler? ProfileTapped;

        public static readonly BindableProperty DisplayNameProperty =
            BindableProperty.Create(
                nameof(DisplayName),
                typeof(string),
                typeof(ProfileSummaryCard),
                "Collector");

        public static readonly BindableProperty HandleProperty =
            BindableProperty.Create(
                nameof(Handle),
                typeof(string),
                typeof(ProfileSummaryCard),
                "@handle");

        public static readonly BindableProperty AvatarPathProperty =
            BindableProperty.Create(
                nameof(AvatarPath),
                typeof(string),
                typeof(ProfileSummaryCard),
                string.Empty,
                propertyChanged: OnAvatarPathChanged);

        public static readonly BindableProperty IsVerifiedProperty =
            BindableProperty.Create(
                nameof(IsVerified),
                typeof(bool),
                typeof(ProfileSummaryCard),
                false,
                propertyChanged: OnComputedTextChanged);

        public static readonly BindableProperty RatingProperty =
            BindableProperty.Create(
                nameof(Rating),
                typeof(double),
                typeof(ProfileSummaryCard),
                0.0,
                propertyChanged: OnComputedTextChanged);

        public static readonly BindableProperty RatingCountProperty =
            BindableProperty.Create(
                nameof(RatingCount),
                typeof(int),
                typeof(ProfileSummaryCard),
                0,
                propertyChanged: OnComputedTextChanged);

        public static readonly BindableProperty LocationProperty =
            BindableProperty.Create(
                nameof(Location),
                typeof(string),
                typeof(ProfileSummaryCard),
                "Ontario, CA");

        public static readonly BindableProperty MemberSinceProperty =
            BindableProperty.Create(
                nameof(MemberSince),
                typeof(DateTime),
                typeof(ProfileSummaryCard),
                new DateTime(2024, 1, 1),
                propertyChanged: OnComputedTextChanged);

        public static readonly BindableProperty CollectionValueProperty =
            BindableProperty.Create(
                nameof(CollectionValue),
                typeof(decimal),
                typeof(ProfileSummaryCard),
                0m,
                propertyChanged: OnComputedTextChanged);

        public static readonly BindableProperty TradesCompletedProperty =
            BindableProperty.Create(
                nameof(TradesCompleted),
                typeof(int),
                typeof(ProfileSummaryCard),
                0,
                propertyChanged: OnComputedTextChanged);

        public ProfileSummaryCard()
        {
            InitializeComponent();

            // The control binds internally to itself (its bindable properties).
            BindingContext = this;

            GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => ProfileTapped?.Invoke(this, EventArgs.Empty))
            });
        }

        public string DisplayName
        {
            get => (string)GetValue(DisplayNameProperty);
            set => SetValue(DisplayNameProperty, value);
        }

        public string Handle
        {
            get => (string)GetValue(HandleProperty);
            set => SetValue(HandleProperty, value);
        }

        public string AvatarPath
        {
            get => (string)GetValue(AvatarPathProperty);
            set => SetValue(AvatarPathProperty, value);
        }

        public bool IsVerified
        {
            get => (bool)GetValue(IsVerifiedProperty);
            set => SetValue(IsVerifiedProperty, value);
        }

        public double Rating
        {
            get => (double)GetValue(RatingProperty);
            set => SetValue(RatingProperty, value);
        }

        public int RatingCount
        {
            get => (int)GetValue(RatingCountProperty);
            set => SetValue(RatingCountProperty, value);
        }

        public string Location
        {
            get => (string)GetValue(LocationProperty);
            set => SetValue(LocationProperty, value);
        }

        public DateTime MemberSince
        {
            get => (DateTime)GetValue(MemberSinceProperty);
            set => SetValue(MemberSinceProperty, value);
        }

        public decimal CollectionValue
        {
            get => (decimal)GetValue(CollectionValueProperty);
            set => SetValue(CollectionValueProperty, value);
        }

        public int TradesCompleted
        {
            get => (int)GetValue(TradesCompletedProperty);
            set => SetValue(TradesCompletedProperty, value);
        }

        // ---------- Computed UI properties ----------
        public ImageSource AvatarSource
        {
            get
            {
                if (string.IsNullOrWhiteSpace(AvatarPath))
                {
                    return ImageSource.FromFile("default_avatar.png");
                }

                // File path stored in AppDataDirectory
                return ImageSource.FromFile(AvatarPath);
            }
        }

        public string RatingText => $"{Rating:0.0} ({RatingCount})";
        public string TradeText => $"{TradesCompleted} trades";
        public string MemberSinceText => $"Since {MemberSince:yyyy}";
        public string CollectionValueText => $"${CollectionValue:N0}";

        private static void OnAvatarPathChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (bindable is ProfileSummaryCard card)
            {
                // Force the Image binding (AvatarSource) to refresh when AvatarPath changes
                card.OnPropertyChanged(nameof(AvatarSource));
            }
        }

        private static void OnComputedTextChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (bindable is ProfileSummaryCard card)
            {
                card.OnPropertyChanged(nameof(RatingText));
                card.OnPropertyChanged(nameof(TradeText));
                card.OnPropertyChanged(nameof(MemberSinceText));
                card.OnPropertyChanged(nameof(CollectionValueText));
            }
        }
    }
}
