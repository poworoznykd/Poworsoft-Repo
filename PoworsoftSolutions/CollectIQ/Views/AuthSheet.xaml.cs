//
//  FILE            : AuthSheet.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-21
//  DESCRIPTION     :
//      Provides login and registration logic with password
//      strength feedback and local database integration.
//
using CollectIQ.Interfaces;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace CollectIQ.Views
{
    public partial class AuthSheet : ContentPage
    {
        private readonly IAuthService _authService;
        private readonly PasswordStrengthMeter _meter;

        /// <summary>
        /// Initializes AuthSheet with dependency-injected authentication service.
        /// </summary>
        public AuthSheet(IAuthService authService)
        {
            InitializeComponent();
            _authService = authService;
            _meter = new PasswordStrengthMeter();
            StrengthMeterView.Drawable = _meter;
        }

        /// <summary>
        /// Updates the password strength meter as the user types.
        /// </summary>
        private void OnPasswordChanged(object sender, TextChangedEventArgs e)
        {
            _meter.UpdateStrength(e.NewTextValue ?? string.Empty);
            StrengthMeterView.Invalidate();
        }

        /// <summary>
        /// Handles the registration workflow.
        /// Ensures password confirmation and feedback to user.
        /// </summary>
        private async void OnRegister(object sender, EventArgs e)
        {
            string email = EmailEntry.Text?.Trim() ?? string.Empty;
            string password = PasswordEntry.Text ?? string.Empty;
            string confirm = ConfirmPasswordEntry.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                await DisplayAlert("Missing Info", "Please enter both email and password.", "OK");
                return;
            }

            if (password != confirm)
            {
                await DisplayAlert("Password Mismatch", "Passwords do not match.", "OK");
                return;
            }

            bool success = await _authService.RegisterAsync(email, password);
            if (success)
            {
                await DisplayAlert("Registration Successful", "Account created successfully.", "OK");
                Application.Current!.MainPage = new AppShell();
            }
            else
            {
                await DisplayAlert("Error", "Registration failed. Please try again.", "OK");
            }
        }

        /// <summary>
        /// Handles the login process.
        /// </summary>
        private async void OnLogin(object sender, EventArgs e)
        {
            string email = EmailEntry.Text?.Trim() ?? string.Empty;
            string password = PasswordEntry.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                await DisplayAlert("Missing Info", "Please enter your email and password.", "OK");
                return;
            }

            bool success = await _authService.LoginWithPasswordAsync(email, password);
            if (success)
            {
                await DisplayAlert("Welcome Back", "Login successful.", "OK");
                Application.Current!.MainPage = new AppShell();
            }
            else
            {
                await DisplayAlert("Login Failed", "Invalid email or password.", "OK");
            }
        }

        /// <summary>
        /// Drawable class for password strength speedometer visualization.
        /// </summary>
        private class PasswordStrengthMeter : IDrawable
        {
            private double _strength;

            public void UpdateStrength(string password)
            {
                _strength = Evaluate(password);
            }

            private static double Evaluate(string pwd)
            {
                if (string.IsNullOrWhiteSpace(pwd)) return 0;
                double score = 0;
                if (pwd.Length >= 6) score += 0.25;
                if (pwd.Any(char.IsUpper)) score += 0.25;
                if (pwd.Any(char.IsDigit)) score += 0.25;
                if (pwd.Any(ch => !char.IsLetterOrDigit(ch))) score += 0.25;
                return Math.Min(score, 1.0);
            }

            public void Draw(ICanvas canvas, RectF dirtyRect)
            {
                float width = dirtyRect.Width;
                float height = dirtyRect.Height;
                float radius = (float)(Math.Min(width, height) / 2.0 - 10);
                float centerX = width / 2;
                float centerY = height;

                canvas.StrokeSize = 10;
                canvas.StrokeLineCap = LineCap.Round;

                // background arc
                canvas.StrokeColor = Colors.DarkGray;
                canvas.DrawArc(centerX - radius, centerY - radius, radius * 2, radius * 2, 180, 180, false, false);

                // strength color
                Color strengthColor = _strength switch
                {
                    < 0.3 => Colors.Red,
                    < 0.6 => Colors.Orange,
                    < 0.8 => Colors.Yellow,
                    _ => Colors.LimeGreen
                };

                canvas.StrokeColor = strengthColor;
                float sweep = (float)(_strength * 180);
                canvas.DrawArc(centerX - radius, centerY - radius, radius * 2, radius * 2, 180, sweep, false, false);
            }
        }
    }
}
