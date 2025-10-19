//
//  FILE            : MauiProgram.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-18
//  DESCRIPTION     :
//      Application entry point and dependency injection container setup
//      for the CollectIQ MAUI mobile application. This file configures
//      fonts, logging, HttpClient, authentication, and app services following
//      the SET Coding Standards.
//
using CollectIQ.Interfaces;
using CollectIQ.Services;
using CollectIQ.Views;
using CommunityToolkit.Maui;
using Microsoft.Extensions.DependencyInjection;   // AddHttpClient, AddSingleton, AddTransient
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;

namespace CollectIQ
{
    /// <summary>
    /// Provides application startup logic and service configuration.
    /// </summary>
    public static class MauiProgram
    {
        /// <summary>
        /// Creates and configures the main MAUI application instance.
        /// </summary>
        /// <returns>
        /// A fully configured <see cref="MauiApp"/> instance used to initialize the CollectIQ app.
        /// </returns>
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // -----------------------------
            // Dependency Injection (Services)
            // -----------------------------
            builder.Services.AddSingleton<IDatabase, SqliteDatabase>();
            builder.Services.AddSingleton<IImageStorage, ImageStorage>();

            // Add Supabase-backed authentication
            builder.Services.AddSingleton<IAuthService, SupabaseAuthService>();

            builder.Services.AddHttpClient<IEbayService, EbayService>();

            // Register pages that need DI
            builder.Services.AddTransient<AddCardPage>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }

    }
}
