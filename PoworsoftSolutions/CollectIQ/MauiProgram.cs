//
//  FILE            : MauiProgram.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  UPDATED VERSION : 2025-10-28
//  DESCRIPTION     :
//      Configures CollectIQ’s MAUI application, registers core services,
//      enables CommunityToolkit CameraView handlers, and applies consistent navigation bar styling.
//

using CollectIQ.Interfaces;
using CollectIQ.Services;
using CollectIQ.Views;
using CommunityToolkit.Maui;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Hosting;
using Plugin.Maui.OCR;


namespace CollectIQ
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .UseMauiCommunityToolkitCamera()
                .UseOcr()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // Dependency Injection
            builder.Services.AddSingleton<IDatabase, SqliteDatabase>();
            builder.Services.AddSingleton<IAuthService, LocalAuthService>();
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddTransient<AuthSheet>();
            builder.Services.AddTransient<LandingPage>();

#if ANDROID
            NavigationViewHandler.Mapper.AppendToMapping("CustomNavBarColors", (handler, view) =>
            {
                var activity = Platform.CurrentActivity;
                if (activity != null)
                {
                    var window = activity.Window;
                    window?.SetStatusBarColor(Android.Graphics.Color.ParseColor("#0B0B0D"));
                    window?.SetNavigationBarColor(Android.Graphics.Color.ParseColor("#0B0B0D"));
                }
            });
#endif

#if IOS
            NavigationViewHandler.Mapper.AppendToMapping("CustomNavBarColors", (handler, view) =>
            {
                if (handler.PlatformView != null)
                {
                    handler.PlatformView.BarTintColor = UIKit.UIColor.FromRGB(11, 11, 13);
                    handler.PlatformView.TintColor = UIKit.UIColor.FromRGB(0, 180, 255);
                    handler.PlatformView.TitleTextAttributes = new UIKit.UIStringAttributes
                    {
                        ForegroundColor = UIKit.UIColor.FromRGB(0, 180, 255)
                    };
                }
            });
#endif

            var app = builder.Build();
            CollectIQ.Utilities.ServiceHelper.Services = app.Services;
            return app;
        }
    }
}
