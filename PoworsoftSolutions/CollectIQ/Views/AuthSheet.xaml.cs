//
//  FILE            : AuthSheet.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-21
//  UPDATED         : 2026-06-05
//  DESCRIPTION     :
//      Displays the sign-in and registration sheet for CollectIQ. The page
//      receives authentication through dependency injection and binds the
//      login/register commands to LoginViewModel.
//

using CollectIQ.Helpers;
using CollectIQ.Interfaces;
using CollectIQ.ViewModels.Auth;

namespace CollectIQ.Views
{
    /// <summary>
    /// Page that hosts CollectIQ authentication options.
    /// </summary>
    public partial class AuthSheet : ContentPage
    {
        #region Private Members

        private readonly IAuthService authService;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the AuthSheet class.
        /// </summary>
        /// <param name="authService">The authentication service.</param>
        public AuthSheet(IAuthService authService)
        {
            InitializeComponent();

            this.authService = authService ?? throw new ArgumentNullException(nameof(authService));
            BindingContext = new LoginViewModel(this.authService);
        }

        /// <summary>
        /// Initializes a new instance of the AuthSheet class for XAML/Shell usage.
        /// </summary>
        public AuthSheet()
            : this(ServiceHelper.GetService<IAuthService>()
                   ?? throw new InvalidOperationException("IAuthService is not registered."))
        {
        }

        #endregion
    }
}
