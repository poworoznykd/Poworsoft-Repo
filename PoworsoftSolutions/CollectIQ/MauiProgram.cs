//
//  FILE            : MauiProgram.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-23
//  DESCRIPTION     :
//      Configures CollectIQ’s MAUI application, registers core services
//      (authentication and database), and applies consistent navigation
//      bar styling across supported platforms.
//

using CollectIQ.Interfaces;
using CollectIQ.Services;
using CollectIQ.Views;
using CommunityToolkit.Maui;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Hosting;

namespace CollectIQ
{
    public static class MauiProgram
    {
        /// <summary>
        /// Configures and builds the CollectIQ MAUI application.
        /// </summary>
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

            // ============================================================
            //  APP INITIALIZATION & TOOLKIT
            // ============================================================
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // ============================================================
            //  DEPENDENCY INJECTION
            // ============================================================
            builder.Services.AddSingleton<IDatabase, SqliteDatabase>();
            builder.Services.AddSingleton<IAuthService, LocalAuthService>();
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddTransient<AuthSheet>();
            builder.Services.AddTransient<LandingPage>();

            // ============================================================
            //  PLATFORM-SPECIFIC CUSTOMIZATION (ANDROID)
            // ============================================================
#if ANDROID
            NavigationViewHandler.Mapper.AppendToMapping("CustomNavBarColors", (handler, view) =>
            {
                var activity = Platform.CurrentActivity;
                if (activity != null)
                {
                    var window = activity.Window;
                    window?.SetStatusBarColor(Android.Graphics.Color.ParseColor("#0B0B0D")); // Dark background
                    window?.SetNavigationBarColor(Android.Graphics.Color.ParseColor("#0B0B0D"));
                }
            });
#endif

            // ============================================================
            //  PLATFORM-SPECIFIC CUSTOMIZATION (iOS)
            // ============================================================
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

            // ============================================================
            //  BUILD APP INSTANCE
            // ============================================================
            return builder.Build();
        }
    }
}
