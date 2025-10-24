using Android.App;
using Android.Content.PM;
using Android.OS;

namespace CollectIQ
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
            {
                var window = Platform.CurrentActivity?.Window;
                if (window != null)
                {
                    // Set status bar (top) and navigation bar (bottom) colors
                    window.SetStatusBarColor(Android.Graphics.Color.ParseColor("#0B0B0D"));  // dark
                    window.SetNavigationBarColor(Android.Graphics.Color.ParseColor("#0B0B0D"));
                }
            }
        }
    }

}
