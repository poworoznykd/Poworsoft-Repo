using CollectIQ.Enums;
using CollectIQ.Helpers;
using CollectIQ.Interfaces;
using CollectIQ.Models;
using CollectIQ.Models.Cloud;
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
            await RefreshSyncDiagnosticsAsync();
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

        private async void OnTestCloudConnectionClicked(object sender, EventArgs e)
        {
            if (profile == null)
            {
                CloudStatusLabel.Text = "No signed-in CollectIQ profile is available.";
                return;
            }

            ICloudProfileService? cloudProfileService = ServiceHelper.GetService<ICloudProfileService>();
            if (cloudProfileService == null)
            {
                CloudStatusLabel.Text = "Cloud service is not registered.";
                return;
            }

            CloudTestButton.IsEnabled = false;
            CloudTestButton.Text = "Testing…";
            CloudStatusLabel.Text = "Checking Supabase Auth and the profiles table…";

            try
            {
                CloudConnectionResult result = await cloudProfileService.TestConnectionAsync(profile.ProviderUserId);

                if (!result.IsSuccess &&
                    result.Message.StartsWith("No Supabase session was found", StringComparison.OrdinalIgnoreCase))
                {
                    CloudStatusLabel.Text = "Supabase session missing. Reconnecting with Google…";

                    ISocialAuthService? socialAuthService = ServiceHelper.GetService<ISocialAuthService>();
                    if (socialAuthService == null)
                    {
                        CloudStatusLabel.Text = "Supabase session is missing and the Google sign-in service is unavailable.";
                        return;
                    }

                    var socialUser = await socialAuthService.SignInAsync(AuthProvider.Google);
                    if (!socialUser.IsSuccess)
                    {
                        CloudStatusLabel.Text = string.IsNullOrWhiteSpace(socialUser.ErrorMessage)
                            ? "Google reauthentication did not complete."
                            : socialUser.ErrorMessage;
                        return;
                    }

                    profile.ProviderUserId = socialUser.ProviderUserId;
                    profile.UpdatedUtc = DateTime.UtcNow;
                    await App.Database.UpsertUserProfileAsync(profile);
                    UserSession.CurrentUser = profile;

                    result = await cloudProfileService.TestConnectionAsync(profile.ProviderUserId);
                }

                if (result.IsSuccess &&
                    !string.IsNullOrWhiteSpace(result.AuthUserId) &&
                    !string.Equals(profile.ProviderUserId, result.AuthUserId, StringComparison.OrdinalIgnoreCase))
                {
                    profile.ProviderUserId = result.AuthUserId;
                    profile.UpdatedUtc = DateTime.UtcNow;
                    await App.Database.UpsertUserProfileAsync(profile);
                    UserSession.CurrentUser = profile;
                }

                CloudStatusLabel.Text = result.IsSuccess
                    ? $"Connected ✓  {result.DisplayName}"
                    : result.Message;
            }
            catch (Exception ex)
            {
                CloudStatusLabel.Text = $"Cloud test failed: {ex.Message}";
            }
            finally
            {
                CloudTestButton.Text = "Test Cloud Connection";
                CloudTestButton.IsEnabled = true;
            }
        }

        private async void OnTestCollectionCloudSyncClicked(object sender, EventArgs e)
        {
            string accountId = UserSession.CurrentUserAccountId;
            if (string.IsNullOrWhiteSpace(accountId))
            {
                CloudCollectionStatusLabel.Text = "No signed-in CollectIQ account is available.";
                return;
            }

            ICloudCollectionService? cloudCollectionService =
                ServiceHelper.GetService<ICloudCollectionService>();

            if (cloudCollectionService == null)
            {
                CloudCollectionStatusLabel.Text = "Cloud collection service is not registered.";
                return;
            }

            CloudCollectionTestButton.IsEnabled = false;
            CloudCollectionTestButton.Text = "Uploading + verifying…";
            CloudCollectionStatusLabel.Text = "Selecting one local collection…";

            try
            {
                var collections = await App.Database.GetCollectionsForUserAsync(accountId);
                CardCollection? collection = collections
                    .OrderByDescending(c => c.IsDefault)
                    .ThenBy(c => c.CreatedUtc)
                    .FirstOrDefault();

                if (collection == null)
                {
                    CloudCollectionStatusLabel.Text = "No local collection exists for this account.";
                    return;
                }

                CloudCollectionStatusLabel.Text =
                    $"Uploading '{collection.Name}' with local ID {collection.Id}…";

                CloudCollectionSyncResult result =
                    await cloudCollectionService.UpsertAndVerifyCollectionAsync(collection);

                CloudCollectionStatusLabel.Text = result.Message;
            }
            catch (Exception ex)
            {
                CloudCollectionStatusLabel.Text =
                    $"Collection cloud test failed: {ex.Message}";
            }
            finally
            {
                CloudCollectionTestButton.Text = "Upload + Verify One Collection";
                CloudCollectionTestButton.IsEnabled = true;
            }
        }


        private async void OnTestCardCloudSyncClicked(object sender, EventArgs e)
        {
            string accountId = UserSession.CurrentUserAccountId;
            if (string.IsNullOrWhiteSpace(accountId))
            {
                CloudCardStatusLabel.Text = "No signed-in CollectIQ account is available.";
                return;
            }

            ICloudCardService? cloudCardService =
                ServiceHelper.GetService<ICloudCardService>();

            if (cloudCardService == null)
            {
                CloudCardStatusLabel.Text = "Cloud card service is not registered.";
                return;
            }

            CloudCardTestButton.IsEnabled = false;
            CloudCardTestButton.Text = "Uploading + verifying…";
            CloudCardStatusLabel.Text = "Selecting the verified local collection…";

            try
            {
                var collections =
                    await App.Database.GetCollectionsForUserAsync(accountId);

                CardCollection? collection = collections
                    .OrderByDescending(c => c.IsDefault)
                    .ThenBy(c => c.CreatedUtc)
                    .FirstOrDefault();

                if (collection == null)
                {
                    CloudCardStatusLabel.Text =
                        "No local collection exists for this account.";
                    return;
                }

                List<Card> visibleCards =
                    await App.Database.GetAllCardsAsync();

                List<Card> cards = visibleCards
                    .Where(card =>
                        !card.IsDeleted &&
                        string.Equals(
                            card.CollectionId,
                            collection.Id,
                            StringComparison.Ordinal))
                    .ToList();

                if (cards.Count == 0)
                {
                    CloudCardStatusLabel.Text =
                        $"'{collection.Name}' has no local cards to upload.";
                    return;
                }

                CloudCardStatusLabel.Text =
                    $"Uploading {cards.Count} card{(cards.Count == 1 ? string.Empty : "s")} from '{collection.Name}'…";

                CloudCardSyncResult result =
                    await cloudCardService.UpsertAndVerifyCardsAsync(
                        collection,
                        cards);

                CloudCardStatusLabel.Text = result.Message;
            }
            catch (Exception ex)
            {
                CloudCardStatusLabel.Text =
                    $"Card cloud test failed: {ex.Message}";
            }
            finally
            {
                CloudCardTestButton.Text =
                    "Upload + Verify Cards";
                CloudCardTestButton.IsEnabled = true;
            }
        }


        private async void OnSyncNowClicked(object sender, EventArgs e)
        {
            string accountId = UserSession.CurrentUserAccountId;
            if (string.IsNullOrWhiteSpace(accountId))
            {
                SyncSummaryLabel.Text = "No signed-in CollectIQ account is available.";
                return;
            }

            ISyncService? syncService = ServiceHelper.GetService<ISyncService>();
            if (syncService == null)
            {
                SyncSummaryLabel.Text = "Sync service is not registered.";
                return;
            }

            SyncNowButton.IsEnabled = false;
            RefreshSyncDiagnosticsButton.IsEnabled = false;
            SyncSummaryLabel.Text = "Forcing pending/failed queue items to retry now…";

            try
            {
                await App.Database.ResetFailedSyncItemsForRetryAsync(accountId);
                await syncService.SyncAsync();
                PullSyncStatusLabel.Text = syncService.LastPullSummary;
                await Task.Delay(500);
                await RefreshSyncDiagnosticsAsync();
            }
            catch (Exception ex)
            {
                SyncSummaryLabel.Text = $"Manual sync failed: {ex.Message}";
            }
            finally
            {
                SyncNowButton.IsEnabled = true;
                RefreshSyncDiagnosticsButton.IsEnabled = true;
            }
        }

        private async void OnRefreshSyncDiagnosticsClicked(object sender, EventArgs e)
        {
            await RefreshSyncDiagnosticsAsync();
        }

        private async void OnForceFullPullClicked(object sender, EventArgs e)
        {
            ISyncService? syncService =
                ServiceHelper.GetService<ISyncService>();

            if (syncService == null)
            {
                PullSyncStatusLabel.Text =
                    "Sync service is not registered.";
                return;
            }

            ForceFullPullButton.IsEnabled = false;
            SyncNowButton.IsEnabled = false;
            RefreshSyncDiagnosticsButton.IsEnabled = false;

            try
            {
                PullSyncStatusLabel.Text =
                    "Clearing cloud cursor and performing a complete pull…";

                await syncService.ResetPullCursorAsync();
                await syncService.SyncAsync();

                PullSyncStatusLabel.Text =
                    syncService.LastPullSummary;

                await RefreshSyncDiagnosticsAsync();
            }
            catch (Exception ex)
            {
                PullSyncStatusLabel.Text =
                    $"Full cloud pull failed: {ex.Message}";
            }
            finally
            {
                ForceFullPullButton.IsEnabled = true;
                SyncNowButton.IsEnabled = true;
                RefreshSyncDiagnosticsButton.IsEnabled = true;
            }
        }

        private async Task RefreshSyncDiagnosticsAsync()
        {
            string accountId = UserSession.CurrentUserAccountId;
            if (string.IsNullOrWhiteSpace(accountId))
            {
                SyncSummaryLabel.Text = "No signed-in CollectIQ account is available.";
                SyncDetailLabel.Text = string.Empty;
                return;
            }

            try
            {
                ISyncService? syncService =
                    ServiceHelper.GetService<ISyncService>();

                if (syncService != null)
                {
                    PullSyncStatusLabel.Text =
                        syncService.LastPullSummary;
                }

                ICloudCardImageStorageService? imageStorageService =
                    ServiceHelper.GetService<ICloudCardImageStorageService>();

                if (imageStorageService != null)
                {
                    ImageSyncStatusLabel.Text =
                        imageStorageService.LastOperationSummary;
                }

                List<SyncQueueItem> items =
                    await App.Database.GetRecentSyncItemsAsync(accountId, 25);

                int pending = items.Count(item =>
                    string.Equals(item.Status, "Pending", StringComparison.OrdinalIgnoreCase));
                int failed = items.Count(item =>
                    string.Equals(item.Status, "Failed", StringComparison.OrdinalIgnoreCase));
                int completed = items.Count(item =>
                    string.Equals(item.Status, "Completed", StringComparison.OrdinalIgnoreCase));

                SyncSummaryLabel.Text =
                    $"Recent queue: {pending} pending • {failed} failed • {completed} completed";

                if (items.Count == 0)
                {
                    SyncDetailLabel.Text =
                        "No sync queue items exist for this signed-in local account.";
                    return;
                }

                IEnumerable<string> lines = items
                    .Take(10)
                    .Select(item =>
                    {
                        string error = string.IsNullOrWhiteSpace(item.LastError)
                            ? string.Empty
                            : $" | error: {item.LastError}";

                        string nextAttempt = item.NextAttemptUtc.HasValue
                            ? $" | next: {item.NextAttemptUtc.Value.ToLocalTime():g}"
                            : string.Empty;

                        return $"{item.EntityType} {item.Operation} | {item.Status} | retries {item.RetryCount} | {item.EntityId}{nextAttempt}{error}";
                    });

                SyncDetailLabel.Text = string.Join(Environment.NewLine, lines);
            }
            catch (Exception ex)
            {
                SyncSummaryLabel.Text = $"Could not read sync diagnostics: {ex.Message}";
                SyncDetailLabel.Text = string.Empty;
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
