using CollectIQ.Helpers;
using CollectIQ.Interfaces;
using CollectIQ.Models;
using CollectIQ.Services.Session;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CollectIQ.Views
{
    public partial class AccountPage : ContentPage
    {
        private UserAccount? account;
        private UserProfile? profile;

        public AccountPage()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadAccountAsync();
        }

        private async Task LoadAccountAsync()
        {
            string accountId = UserSession.CurrentUserAccountId;
            if (string.IsNullOrWhiteSpace(accountId))
            {
                await DisplayAlert("Account", "No signed-in CollectIQ account is available.", "OK");
                await Navigation.PopAsync();
                return;
            }

            profile = UserSession.CurrentUser;
            account = await App.Database.GetUserAccountByIdAsync(accountId);

            EmailLabel.Text = account?.Email ?? profile?.Email ?? "—";
            AccountIdLabel.Text = accountId;
            StatusLabel.Text = account?.AccountStatus ?? "Active";
            EmailVerifiedLabel.Text = account?.IsEmailVerified == true ? "Verified" : "Not verified";

            UserCredential? credential = await App.Database.GetLocalCredentialAsync(accountId);
            CredentialLabel.Text = credential != null && !string.IsNullOrWhiteSpace(credential.PasswordHash)
                ? "Stored"
                : (account?.IsGuest == true ? "Guest" : "Missing");

            DisplayNameEntry.Text = profile?.DisplayName ?? string.Empty;
            LocationEntry.Text = profile?.LocationDisplay ?? string.Empty;
            BioEditor.Text = profile?.Bio ?? string.Empty;

            var collections = await App.Database.GetCollectionsForUserAsync(accountId);
            var cards = await App.Database.GetAllCardsAsync();
            decimal value = cards.Where(c => c.EstimatedValue.HasValue).Sum(c => c.EstimatedValue ?? 0m);

            CollectionCountLabel.Text = collections.Count.ToString();
            CardCountLabel.Text = cards.Count.ToString();
            CollectionValueLabel.Text = $"${value:N0}";

            UpdateReadiness();
        }

        private void UpdateReadiness()
        {
            int completed = 0;
            const int total = 5;
            if (!string.IsNullOrWhiteSpace(DisplayNameEntry.Text)) completed++;
            if (!string.IsNullOrWhiteSpace(LocationEntry.Text)) completed++;
            if (!string.IsNullOrWhiteSpace(BioEditor.Text)) completed++;
            if (account?.IsEmailVerified == true) completed++;
            if (account?.IsGuest != true) completed++;

            double progress = (double)completed / total;
            ReadinessProgress.Progress = progress;
            ReadinessPercentLabel.Text = $"{completed * 100 / total}% ready";
            ReadinessLabel.Text = completed == total
                ? "Your collector identity is ready for future marketplace features."
                : "Complete your collector identity now so sharing, trading and marketplace features can use it later.";
        }

        private async void OnSaveProfileClicked(object sender, EventArgs e)
        {
            if (profile == null)
            {
                return;
            }

            profile.DisplayName = DisplayNameEntry.Text?.Trim() ?? string.Empty;
            profile.LocationDisplay = LocationEntry.Text?.Trim() ?? string.Empty;
            profile.Bio = BioEditor.Text?.Trim() ?? string.Empty;
            profile.UpdatedUtc = DateTime.UtcNow;

            await App.Database.UpsertUserProfileAsync(profile);
            UserSession.CurrentUser = profile;

            IProfileService? profileService = ServiceHelper.GetService<IProfileService>();
            if (profileService != null)
            {
                profileService.Profile.DisplayName = profile.DisplayName ?? string.Empty;
                profileService.Profile.Location = profile.LocationDisplay ?? string.Empty;
            }

            UpdateReadiness();
            await DisplayAlert("Account", "Your CollectIQ profile was saved.", "OK");
        }

        private async void OnCopyAccountIdClicked(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(AccountIdLabel.Text) && AccountIdLabel.Text != "—")
            {
                await Clipboard.SetTextAsync(AccountIdLabel.Text);
            }
        }

        private async void OnDeveloperDatabaseClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new DeveloperDatabasePage());
        }

        private async void OnBackClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }
    }
}
