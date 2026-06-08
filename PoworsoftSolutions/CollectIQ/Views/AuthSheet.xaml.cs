using CollectIQ.Interfaces;
using CollectIQ.Services;
using CollectIQ.ViewModels.Auth;

namespace CollectIQ.Views
{
    public partial class AuthSheet : ContentPage
    {
        private readonly IAuthService authService;

        // MAIN constructor — injected dependency
        public AuthSheet(IAuthService authService)
        {
            InitializeComponent();
            this.authService = authService;

            // ViewModel binding
            BindingContext = new LoginViewModel(authService);
        }

        // DEFAULT constructor — required for XAML & Shell
        public AuthSheet() : this(new LocalAuthService(new SqliteDatabase()))
        {
            // Nothing else needed here — the base constructor sets BindingContext
        }
    }
}
