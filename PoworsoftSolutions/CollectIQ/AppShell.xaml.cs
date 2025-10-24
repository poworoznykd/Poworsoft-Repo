namespace CollectIQ
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Shell.SetBackgroundColor(this, Color.FromArgb("#0B0B0D")); // deep black
            Shell.SetTabBarTitleColor(this, Color.FromArgb("#00B4FF"));      // neon blue text

        }
    }
}
