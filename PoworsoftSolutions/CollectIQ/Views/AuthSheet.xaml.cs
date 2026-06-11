//
//  FILE            : AuthSheet.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-12-05
//  UPDATED         : 2026-06-10
//  DESCRIPTION     :
//      Code-behind for the CollectIQ authentication page. This version ensures
//      the page resolves authentication through dependency injection so Google
//      and Facebook social authentication use the configured Supabase broker.
//

using CollectIQ.Helpers;
using CollectIQ.Interfaces;
using CollectIQ.Services;
using CollectIQ.ViewModels.Auth;
using System.Diagnostics;
using System.Windows.Input;

namespace CollectIQ.Views
{
    /// <summary>
    /// Authentication page for email/password, guest, and social sign-in.
    /// </summary>
    public partial class AuthSheet : ContentPage
    {
        private readonly IAuthService authService;
        private readonly LoginViewModel viewModel;

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the AuthSheet class using dependency injection.
        /// </summary>
        /// <param name="authService">The authentication service.</param>
        public AuthSheet(IAuthService authService)
        {
            InitializeComponent();

            this.authService = authService;
            this.viewModel = new LoginViewModel(this.authService);
            BindingContext = this.viewModel;
        }

        /// <summary>
        /// Initializes a new instance of the AuthSheet class for XAML/Shell creation.
        /// </summary>
        public AuthSheet()
            : this(ResolveAuthService())
        {
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Handles the Google sign-in button click.
        /// </summary>
        /// <param name="sender">The button that raised the event.</param>
        /// <param name="e">The event arguments.</param>
        private async void GoogleLoginButton_Clicked(object sender, EventArgs e)
        {
            Debug.WriteLine("[CollectIQ AUTH] Google button clicked.");
            await ExecuteCommandAsync(this.viewModel.GoogleLoginCommand, "Google sign-in command is not available.");
        }

        /// <summary>
        /// Handles the Facebook sign-in button click.
        /// </summary>
        /// <param name="sender">The button that raised the event.</param>
        /// <param name="e">The event arguments.</param>
        private async void FacebookLoginButton_Clicked(object sender, EventArgs e)
        {
            Debug.WriteLine("[CollectIQ AUTH] Facebook button clicked.");
            await ExecuteCommandAsync(this.viewModel.FacebookLoginCommand, "Facebook sign-in command is not available.");
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Resolves the authentication service from the application service provider.
        /// </summary>
        /// <returns>The configured authentication service.</returns>
        private static IAuthService ResolveAuthService()
        {
            IAuthService? service = ServiceHelper.GetService<IAuthService>();

            if (service != null)
            {
                return service;
            }

            Debug.WriteLine("[CollectIQ AUTH] WARNING: DI auth service was unavailable. Falling back to local auth with a direct Supabase social broker.");
            return new LocalAuthService(new SqliteDatabase(), new SupabaseAuthService());
        }

        /// <summary>
        /// Executes a command and shows a controlled error if it cannot run.
        /// </summary>
        /// <param name="command">The command to execute.</param>
        /// <param name="errorMessage">The message shown when the command is unavailable.</param>
        /// <returns>An asynchronous task.</returns>
        private async Task ExecuteCommandAsync(ICommand command, string errorMessage)
        {
            if (command == null || !command.CanExecute(null))
            {
                await DisplayAlert("CollectIQ Auth", errorMessage, "OK");
                return;
            }

            command.Execute(null);
            await Task.CompletedTask;
        }

        #endregion
    }
}
