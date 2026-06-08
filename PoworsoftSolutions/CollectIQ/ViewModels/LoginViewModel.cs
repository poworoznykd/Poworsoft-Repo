// FILE: LoginViewModel.cs
// PROJECT: CollectIQ (Mobile Application)
// PROGRAMMER: Darryl Poworoznyk
// FIRST VERSION: 2025-12-05
// DESCRIPTION:
//     ViewModel for login and registration using LocalAuthService.
//     Implements MVVM bindings for AuthSheet.xaml, including
//     password strength tracking, remember-me, and navigation.

using CollectIQ.Domain.Enums;
using CollectIQ.Enums;
using CollectIQ.Interfaces;
using CollectIQ.Views;
using Microsoft.Maui.Controls;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace CollectIQ.ViewModels.Auth
{
    public sealed class LoginViewModel : INotifyPropertyChanged
    {
        private readonly IAuthService authService;

        private string email = string.Empty;
        private string password = string.Empty;
        private string confirmPassword = string.Empty;
        private string message = string.Empty;
        private bool rememberMe;

        // Password visibility
        private bool isPasswordHidden = true;
        private string passwordEyeIcon = "eye_closed.png"; // make sure you have these in Resources/Images

        // Password strength
        private PasswordStrength strength = PasswordStrength.None;

        public LoginViewModel(IAuthService authService)
        {
            this.authService = authService;

            LoginCommand = new Command(async () => await LoginAsync());
            RegisterCommand = new Command(async () => await RegisterAsync());
            GoogleLoginCommand = new Command(async () => await ProviderLoginAsync(AuthProvider.Google));
            FacebookLoginCommand = new Command(async () => await ProviderLoginAsync(AuthProvider.Facebook));
            GuestLoginCommand = new Command(async () => await GuestLoginAsync());
            TogglePasswordVisibilityCommand = new Command(TogglePasswordVisibility);
            PasswordEyeIcon = "eye_closed.png";
        }

        public string Email
        {
            get => email;
            set
            {
                if (email == value) return;
                email = value;
                OnPropertyChanged();
            }
        }

        public string Password
        {
            get => password;
            set
            {
                if (password == value) return;
                password = value;
                OnPropertyChanged();
                UpdateStrength();
            }
        }

        public string ConfirmPassword
        {
            get => confirmPassword;
            set
            {
                if (confirmPassword == value) return;
                confirmPassword = value;
                OnPropertyChanged();
            }
        }

        public string Message
        {
            get => message;
            set
            {
                if (message == value) return;
                message = value;
                OnPropertyChanged();
            }
        }

        public bool RememberMe
        {
            get => rememberMe;
            set
            {
                if (rememberMe == value) return;
                rememberMe = value;
                OnPropertyChanged();
            }
        }

        public bool IsPasswordHidden
        {
            get => isPasswordHidden;
            set
            {
                if (isPasswordHidden == value) return;
                isPasswordHidden = value;
                OnPropertyChanged();
            }
        }

        public string PasswordEyeIcon
        {
            get => passwordEyeIcon;
            set
            {
                if (passwordEyeIcon == value) return;
                passwordEyeIcon = value;
                OnPropertyChanged();
            }
        }

        public PasswordStrength Strength
        {
            get => strength;
            set
            {
                if (strength == value) return;
                strength = value;
                OnPropertyChanged();
            }
        }

        public ICommand LoginCommand { get; }
        public ICommand RegisterCommand { get; }
        public ICommand GoogleLoginCommand { get; }
        public ICommand FacebookLoginCommand { get; }
        public ICommand GuestLoginCommand { get; }
        public ICommand TogglePasswordVisibilityCommand { get; }

        private async Task RegisterAsync()
        {
            Message = string.Empty;

            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
            {
                Message = "Email and password are required.";
                return;
            }

            if (Password != ConfirmPassword)
            {
                Message = "Passwords do not match.";
                return;
            }

            var ok = await authService.RegisterAsync(Email, Password);

            Message = ok
                ? "Account created. You can now sign in."
                : "Email already exists. Try signing in.";
        }

        /// <summary>
        /// Attempts to sign in using the credentials entered by the user.
        /// </summary>
        private async Task LoginAsync()
        {
            Message = string.Empty;

            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
            {
                Message = "Email and password are required.";
                return;
            }
            
            var ok = await authService.LoginAsync(Email, Password);

            if (!ok)
            {
                Message = "Invalid login.";
                return;
            }

            Message = "Login successful.";

            Application.Current.MainPage = new AppShell();

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Shell.Current.GoToAsync("///DashboardPage");
            });

        }

        private async Task ProviderLoginAsync(AuthProvider provider)
        {
            // Social login is not enabled yet (Google setup still pending).
            await Application.Current.MainPage.DisplayAlert(
                "Under construction",
                "Provider sign-in is coming soon.",
                "OK");

            //try
            //{
            //    Message = string.Empty;

            //    bool ok = await authService.SignInWithProviderAsync(provider);
            //    if (!ok)
            //    {
            //        Message = $"{provider} sign-in is not configured yet.";
            //        return;
            //    }

            //    Application.Current.MainPage = new AppShell();

            //    await MainThread.InvokeOnMainThreadAsync(async () =>
            //    {
            //        await Shell.Current.GoToAsync("///DashboardPage");
            //    });
            //}
            //catch (Exception ex)
            //{
            //    Message = ex.Message;
            //}
        }

        private async Task GuestLoginAsync()
        {
            try
            {
                Message = string.Empty;

                bool ok = await authService.SignInGuestAsync();
                if (!ok)
                {
                    Message = "Unable to sign in as guest.";
                    return;
                }

                Application.Current.MainPage = new AppShell();

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await Shell.Current.GoToAsync("///DashboardPage");
                });
            }
            catch (Exception ex)
            {
                Message = ex.Message;
            }
        }

        private void TogglePasswordVisibility()
        {
            IsPasswordHidden = !IsPasswordHidden;
            PasswordEyeIcon = IsPasswordHidden ? "eye_closed_icon.png" : "eye_open_icon.png";
        }

        private void UpdateStrength()
        {
            if (string.IsNullOrEmpty(Password))
            {
                Strength = PasswordStrength.None;
                return;
            }

            int score = 0;

            if (Password.Length >= 8) score++;
            if (Password.Length >= 12) score++;
            if (HasLetters(Password) && HasDigits(Password)) score++;
            if (HasSymbols(Password)) score++;

            if (score <= 1)
            {
                Strength = PasswordStrength.Weak;
            }
            else if (score == 2 || score == 3)
            {
                Strength = PasswordStrength.Medium;
            }
            else
            {
                Strength = PasswordStrength.Strong;
            }
        }

        private static bool HasLetters(string input) =>
            !string.IsNullOrEmpty(input) && input.Any(char.IsLetter);

        private static bool HasDigits(string input) =>
            !string.IsNullOrEmpty(input) && input.Any(char.IsDigit);

        private static bool HasSymbols(string input) =>
            !string.IsNullOrEmpty(input) && input.Any(ch => !char.IsLetterOrDigit(ch));

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
