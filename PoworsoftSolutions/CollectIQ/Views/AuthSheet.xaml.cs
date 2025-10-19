using CollectIQ.Interfaces;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Org.Apache.Http.Authentication;
using System;
using System.Threading.Tasks;

namespace CollectIQ.Views
{
    /// <summary>
    /// Authentication modal with Sign In and Create Account support.
    /// </summary>
    public partial class AuthSheet : ContentPage
    {
        private readonly IAuthService _auth;
        private const string GuestIdKey = "guest_id";

        /// <summary>
        /// Initializes a new instance of the <see cref="AuthSheet"/> class.
        /// </summary>
        /// <param name="authService">An injected authentication service.</param>
        public AuthSheet(IAuthService authService)
        {
            InitializeComponent();
            _auth = authService;
            DisplayAlert("Auth Service", _auth.GetType().Name, "OK");
        }

        /// <summary>
        /// Handles backdrop taps to close the modal sheet.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The event arguments.</param>
        private async void OnBackdropTapped(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }

        /// <summary>
        /// Switches UI to Sign In mode.
        /// </summary>
        private void OnSignInTab(object sender, EventArgs e)
        {
            SignInTab.BackgroundColor = Color.FromArgb("#E8EAF6");
            SignInTab.TextColor = Color.FromArgb("#2B0B98");
            RegisterTab.BackgroundColor = Colors.Transparent;
            RegisterTab.TextColor = Color.FromArgb("#6B7280");

            SignInPanel.IsVisible = true;
            RegisterPanel.IsVisible = false;
        }

        /// <summary>
        /// Switches UI to Registration mode.
        /// </summary>
        private void OnRegisterTab(object sender, EventArgs e)
        {
            RegisterTab.BackgroundColor = Color.FromArgb("#E8EAF6");
            RegisterTab.TextColor = Color.FromArgb("#2B0B98");
            SignInTab.BackgroundColor = Colors.Transparent;
            SignInTab.TextColor = Color.FromArgb("#6B7280");

            SignInPanel.IsVisible = false;
            RegisterPanel.IsVisible = true;
        }

        /// <summary>
        /// Attempts email+password sign in.
        /// </summary>
        private async void OnSignInPassword(object sender, EventArgs e)
        {
            var email = SignInEmail.Text?.Trim();
            var pw = SignInPassword.Text;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(pw))
            {
                await DisplayAlert("Missing info", "Please enter email and password.", "OK");
                return;
            }

            var ok = await _auth.LoginWithPasswordAsync(email, pw);
            await HandlePostAuthAsync(ok);
        }

        /// <summary>
        /// Sends magic link to the provided email.
        /// </summary>
        private async void OnSignInMagic(object sender, EventArgs e)
        {
            var email = SignInEmail.Text?.Trim();
            if (string.IsNullOrEmpty(email))
            {
                await DisplayAlert("Missing email", "Enter your email to receive a magic link.", "OK");
                return;
            }

            var ok = await _auth.SendMagicLinkAsync(email);
            if (ok)
                await DisplayAlert("Check your inbox", "We sent you a sign-in link.", "OK");
            else
                await DisplayAlert("Error", "Could not send magic link. Try again.", "OK");
        }

        /// <summary>
        /// Attempts registration using email and password.
        /// </summary>
        private async void OnRegister(object sender, EventArgs e)
        {
            var email = RegEmail.Text?.Trim();
            var pw1 = RegPassword.Text;
            var pw2 = RegPassword2.Text;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(pw1) || string.IsNullOrEmpty(pw2))
            {
                await DisplayAlert("Missing info", "Please complete all fields.", "OK");
                return;
            }
            if (pw1.Length < 8)
            {
                await DisplayAlert("Weak password", "Password must be at least 8 characters.", "OK");
                return;
            }
            if (pw1 != pw2)
            {
                await DisplayAlert("Password mismatch", "Passwords do not match.", "OK");
                return;
            }

            var ok = await _auth.RegisterAsync(email, pw1);
            await HandlePostAuthAsync(ok);
        }

        /// <summary>
        /// Signs in with Google provider.
        /// </summary>
        private async void OnGoogle(object sender, EventArgs e)
        {
            var ok = await _auth.LoginWithGoogleAsync();
            await HandlePostAuthAsync(ok);
        }

        /// <summary>
        /// Continues into the app without an account (guest mode).
        /// </summary>
        private async void OnContinueGuest(object sender, EventArgs e)
        {
            var existing = await SecureStorage.GetAsync(GuestIdKey);
            if (string.IsNullOrEmpty(existing))
            {
                var id = System.Guid.NewGuid().ToString("N");
                await SecureStorage.SetAsync(GuestIdKey, id);
            }

            await Navigation.PopModalAsync();
            Application.Current.MainPage = new AppShell();
        }

        /// <summary>
        /// Finalizes auth (link guest data, then navigate).
        /// </summary>
        /// <param name="ok">True if auth succeeded; otherwise, false.</param>
        private async Task HandlePostAuthAsync(bool ok)
        {
            if (!ok)
            {
                await DisplayAlert("Auth failed", "Please check your credentials and try again.", "OK");
                return;
            }

            var guest = await SecureStorage.GetAsync(GuestIdKey);
            if (!string.IsNullOrEmpty(guest))
            {
                await _auth.LinkGuestDataAsync(guest);
            }

            await Navigation.PopModalAsync();
            Application.Current.MainPage = new AppShell();
        }
    }
}
