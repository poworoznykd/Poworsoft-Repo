//
//  FILE            : MauiProgram.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  UPDATED VERSION : 2026-01-18
//  DESCRIPTION     :
//      Configures CollectIQ’s MAUI application, registers core services,
//      and ensures required services exist app-wide (Profile, Browser, Alerts, DB, Auth).
//

using CollectIQ.Interfaces;
using CollectIQ.Services;
using CollectIQ.Services.Roles;
using CollectIQ.Views;
using CommunityToolkit.Maui;
using Maui.FreakyControls.Extensions;
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
                .InitializeFreakyControls()
                .UseMauiCommunityToolkit()
                .UseMauiCommunityToolkitCamera()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // ------------------------------
            // Dependency Injection
            // ------------------------------
            builder.Services.AddSingleton<IDatabase, SqliteDatabase>();

            // Auth + Profile
            builder.Services.AddSingleton<IAuthService, LocalAuthService>();
            builder.Services.AddSingleton<IProfileService, ProfileService>();

            // REQUIRED: Browser + Alerts (prevents null crashes)
            builder.Services.AddSingleton<IBrowserService, BrowserService>();
            builder.Services.AddSingleton<IAlertService, AlertService>();

            // Views
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddTransient<AuthSheet>();
            builder.Services.AddTransient<LandingPage>();

            // Role behaviors
            builder.Services.AddSingleton<IUserRoleBehavior, AdminRoleBehavior>();
            builder.Services.AddSingleton<IUserRoleBehavior, RegularRoleBehavior>();
            builder.Services.AddSingleton<IUserRoleBehavior, GuestRoleBehavior>();

            // App-wide mode tracking (Collect / Inspect / Trade)
            builder.Services.AddSingleton<AppModeService>();

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
