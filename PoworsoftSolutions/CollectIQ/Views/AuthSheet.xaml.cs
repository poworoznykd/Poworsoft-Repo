//
//  FILE            : AuthSheet.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-19
//  DESCRIPTION     :
//      Handles authentication page logic for registration and login,
//      including password strength meter animation.
//
using CollectIQ.Interfaces;
using Microsoft.Maui.Graphics;
using System;
using System.Threading.Tasks;

namespace CollectIQ.Views
{
    public partial class AuthSheet : ContentPage
    {
        private readonly IAuthService _authService;

        public AuthSheet(IAuthService authService)
        {
            InitializeComponent();
            _authService = authService;
        }

        private async void OnRegister(object sender, EventArgs e)
        {
            string email = EmailEntry.Text?.Trim() ?? "";
            string password = PasswordEntry.Text ?? "";
            string confirm = ConfirmPasswordEntry.Text ?? "";

            if (password != confirm)
            {
                await DisplayAlert("Error", "Passwords do not match.", "OK");
                return;
            }

            bool success = await _authService.RegisterAsync(email, password);
            await DisplayAlert("Register", success ? "Registration successful." : "Account exists.", "OK");
        }

        private async void OnLogin(object sender, EventArgs e)
        {
            string email = EmailEntry.Text?.Trim() ?? "";
            string password = PasswordEntry.Text ?? "";

            bool success = await _authService.LoginAsync(email, password);
            await DisplayAlert("Login", success ? "Welcome back!" : "Invalid credentials.", "OK");
        }

        private void OnPasswordChanged(object sender, TextChangedEventArgs e)
        {
            string pwd = e.NewTextValue ?? "";
            double strength = CalculateStrength(pwd);
            StrengthMeterView.Drawable = new StrengthDrawable(strength);
            StrengthMeterView.Invalidate();
        }

        private static double CalculateStrength(string pwd)
        {
            double score = 0;
            if (pwd.Length >= 6) score += 0.3;
            if (pwd.Length >= 10) score += 0.2;
            if (System.Text.RegularExpressions.Regex.IsMatch(pwd, @"[A-Z]")) score += 0.2;
            if (System.Text.RegularExpressions.Regex.IsMatch(pwd, @"\d")) score += 0.2;
            if (System.Text.RegularExpressions.Regex.IsMatch(pwd, @"[^a-zA-Z0-9]")) score += 0.1;
            return Math.Min(score, 1.0);
        }

        private sealed class StrengthDrawable : IDrawable
        {
            private readonly double _strength;
            public StrengthDrawable(double strength) => _strength = strength;

            public void Draw(ICanvas canvas, RectF dirtyRect)
            {
                float centerX = (float)dirtyRect.Center.X;
                float centerY = (float)dirtyRect.Center.Y;
                float radius = 40;

                canvas.StrokeColor = Colors.White;
                canvas.StrokeSize = 2;
                canvas.DrawArc(centerX - radius, centerY - radius, radius * 2, radius * 2, 180, 180, false, false);

                float angle = (float)(_strength * 180);
                canvas.StrokeColor = _strength switch
                {
                    < 0.3 => Colors.Red,
                    < 0.6 => Colors.Orange,
                    _ => Colors.LimeGreen
                };
                canvas.DrawArc(centerX - radius, centerY - radius, radius * 2, radius * 2, 180, angle, false, false);
            }
        }
    }
}
