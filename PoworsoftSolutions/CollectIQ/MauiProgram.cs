//
//  FILE            : MauiProgram.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  UPDATED VERSION : 2026-06-08
//  DESCRIPTION     :
//      Configures CollectIQ's MAUI application and registers core services,
//      repositories, authentication, profile support, browser support, alerts,
//      and local database access.
//

using CollectIQ.Helpers;
using CollectIQ.Interfaces;
using CollectIQ.Repositories;
using CollectIQ.Services;
using CollectIQ.Services.Roles;
using CollectIQ.Views;
using CommunityToolkit.Maui;
using Maui.FreakyControls.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Hosting;

namespace CollectIQ
{
    /// <summary>
    /// Configures the CollectIQ MAUI application.
    /// </summary>
    public static class MauiProgram
    {
        /// <summary>
        /// Creates and configures the MAUI application instance.
        /// </summary>
        /// <returns>The configured MAUI application.</returns>
        public static MauiApp CreateMauiApp()
        {
            MauiAppBuilder builder = MauiApp.CreateBuilder();

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

            RegisterServices(builder);
            ConfigurePlatformHandlers();

            MauiApp app = builder.Build();
            ServiceHelper.Initialize(app.Services);

            return app;
        }

        /// <summary>
        /// Registers application services and repositories.
        /// </summary>
        /// <param name="builder">The MAUI app builder.</param>
        private static void RegisterServices(MauiAppBuilder builder)
        {
            // Database
            builder.Services.AddSingleton<IDatabase, SqliteDatabase>();

            // Repositories
            builder.Services.AddSingleton<IUserRepository, UserRepository>();
            builder.Services.AddSingleton<ICollectionRepository, CollectionRepository>();
            builder.Services.AddSingleton<ICardRepository, CardRepository>();

            // Auth + Profile
            builder.Services.AddSingleton<ISocialAuthService, SupabaseAuthService>();
            builder.Services.AddSingleton<IAuthService>(serviceProvider =>
            {
                IDatabase database = serviceProvider.GetRequiredService<IDatabase>();
                ISocialAuthService socialAuthService = serviceProvider.GetRequiredService<ISocialAuthService>();
                return new LocalAuthService(database, socialAuthService);
            });
            builder.Services.AddSingleton<IProfileService, ProfileService>();

            // App utilities
            builder.Services.AddSingleton<IBrowserService, BrowserService>();
            builder.Services.AddSingleton<IAlertService, AlertService>();
            builder.Services.AddSingleton<AppModeService>();

            // Role behaviors
            builder.Services.AddSingleton<IUserRoleBehavior, AdminRoleBehavior>();
            builder.Services.AddSingleton<IUserRoleBehavior, RegularRoleBehavior>();
            builder.Services.AddSingleton<IUserRoleBehavior, GuestRoleBehavior>();

            // Views
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddTransient<AuthSheet>();
            builder.Services.AddTransient<LandingPage>();
        }

        /// <summary>
        /// Configures platform-specific handlers.
        /// </summary>
        private static void ConfigurePlatformHandlers()
        {
#if ANDROID
            NavigationViewHandler.Mapper.AppendToMapping("CustomNavBarColors", (handler, view) =>
            {
                Android.App.Activity? activity = Platform.CurrentActivity;

                if (activity != null)
                {
                    Android.Views.Window? window = activity.Window;
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
        }
    }
}
