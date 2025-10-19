//
//  FILE            : AuthSheet.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  LAST UPDATED    : 2025-10-19
//  DESCRIPTION     :
//      Handles login/registration and renders a smaller,
//      repositioned animated password strength meter.
//
using CollectIQ.Interfaces;
using Microsoft.Maui.Graphics;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CollectIQ.Views
{
    public partial class AuthSheet : ContentPage
    {
        private readonly IAuthService _authService;
        private double _currentStrength = 0;
        private double _targetStrength = 0;
        private bool _animating = false;

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
            await DisplayAlert("Register", success ? "Registration successful!" : "Account already exists.", "OK");
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
            _targetStrength = CalculateStrength(e.NewTextValue ?? "");
            if (!_animating)
                _ = AnimateStrengthAsync();
        }

        private async Task AnimateStrengthAsync()
        {
            _animating = true;
            const double step = 0.04;
            const int delay = 16;

            while (Math.Abs(_targetStrength - _currentStrength) > 0.01)
            {
                _currentStrength += Math.Sign(_targetStrength - _currentStrength) * step;
                _currentStrength = Math.Clamp(_currentStrength, 0, 1);
                StrengthMeterView.Drawable = new StrengthDrawable(_currentStrength);
                StrengthMeterView.Invalidate();
                await Task.Delay(delay);
            }

            _currentStrength = _targetStrength;
            StrengthMeterView.Drawable = new StrengthDrawable(_currentStrength);
            StrengthMeterView.Invalidate();
            _animating = false;
        }

        private static double CalculateStrength(string pwd)
        {
            double score = 0;
            if (pwd.Length >= 6) score += 0.3;
            if (pwd.Length >= 10) score += 0.2;
            if (Regex.IsMatch(pwd, "[A-Z]")) score += 0.2;
            if (Regex.IsMatch(pwd, "[0-9]")) score += 0.2;
            if (Regex.IsMatch(pwd, "[^a-zA-Z0-9]")) score += 0.1;
            return Math.Min(score, 1.0);
        }

        private sealed class StrengthDrawable : IDrawable
        {
            private readonly double _strength;
            public StrengthDrawable(double strength) => _strength = strength;

            public void Draw(ICanvas canvas, RectF dirtyRect)
            {
                canvas.Antialias = true;
                float centerX = (float)dirtyRect.Center.X;
                float centerY = (float)dirtyRect.Center.Y - 10;
                float radius = 50;   // smaller size now fits perfectly
                float strokeWidth = 10f;

                // Base arc
                canvas.StrokeColor = new Color(0, 0, 0, 0.45f);
                canvas.StrokeSize = strokeWidth + 2;
                canvas.StrokeLineCap = LineCap.Round;
                canvas.DrawArc(centerX - radius, centerY - radius, radius * 2, radius * 2, 180, 180, false, false);

                // Progress arc
                canvas.StrokeColor = GetGradientColor(_strength);
                canvas.StrokeSize = strokeWidth;
                canvas.StrokeLineCap = LineCap.Round;
                canvas.DrawArc(centerX - radius, centerY - radius, radius * 2, radius * 2, 180, (float)(_strength * 180), false, false);

                // Tick marks
                canvas.StrokeColor = new Color(1, 1, 1, 0.4f);
                for (int i = 0; i <= 6; i++)
                {
                    float angle = 180 + (i * 30);
                    double rad = Math.PI * angle / 180;
                    float x1 = centerX + (float)Math.Cos(rad) * (radius - 6);
                    float y1 = centerY + (float)Math.Sin(rad) * (radius - 6);
                    float x2 = centerX + (float)Math.Cos(rad) * (radius + 6);
                    float y2 = centerY + (float)Math.Sin(rad) * (radius + 6);
                    canvas.DrawLine(x1, y1, x2, y2);
                }

                // Strength label
                string label = _strength switch
                {
                    < 0.3 => "Weak",
                    < 0.6 => "Fair",
                    < 0.8 => "Good",
                    _ => "Strong"
                };

                Color color = _strength switch
                {
                    < 0.3 => Colors.Red,
                    < 0.6 => Colors.Orange,
                    < 0.8 => Colors.Yellow,
                    _ => Colors.LimeGreen
                };

                canvas.FontSize = 13;
                canvas.FontColor = color;
                canvas.DrawString(label, centerX, centerY + 26, HorizontalAlignment.Center);
            }

            private static Color GetGradientColor(double s)
            {
                return s switch
                {
                    < 0.2 => new Color(1, 0, 0),
                    < 0.4 => new Color(1, 0.4f, 0),
                    < 0.6 => new Color(1, 0.8f, 0),
                    < 0.8 => new Color(0.8f, 1, 0),
                    _ => new Color(0, 1, 0)
                };
            }
        }
    }
}
