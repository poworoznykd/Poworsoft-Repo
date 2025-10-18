//
//  FILE            : MauiProgram.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : <Your Name>
//  FIRST VERSION   : 2025-10-18
//  DESCRIPTION     :
//      Application entry point and dependency injection container setup
//      for the CollectIQ MAUI mobile application. This file configures
//      fonts, logging, HttpClient, and app services following the
//      SET Coding Standards.
//

using Microsoft.Extensions.DependencyInjection;   // AddHttpClient, AddSingleton, AddTransient
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using CollectIQ.Interfaces;
using CollectIQ.Services;
using CollectIQ.Views;

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
            // Create a new MAUI App builder.
            var builder = MauiApp.CreateBuilder();

            // Register application root and configure UI fonts.
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    // Add fonts for UI consistency and accessibility.
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // -----------------------------
            // Dependency Injection (Services)
            // -----------------------------

            // Persistent storage via SQLite.
            builder.Services.AddSingleton<IDatabase, SqliteDatabase>();

            // Local image file persistence.
            builder.Services.AddSingleton<IImageStorage, ImageStorage>();

            // HttpClient for marketplace lookups (eBay).
            // NOTE: Requires NuGet package: Microsoft.Extensions.Http
            builder.Services.AddHttpClient<IEbayService, EbayService>(client =>
            {
                // Optionally set defaults here; real token handling will be added later.
                // client.BaseAddress = new Uri("https://api.ebay.com/");
                // client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            });

            // -----------------------------
            // Views (constructed via DI)
            // -----------------------------
            // Register pages that require constructor injection so Shell can resolve them.
            builder.Services.AddTransient<AddCardPage>();

#if DEBUG
            // Enable debug-level logging during development only.
            builder.Logging.AddDebug();
#endif

            // Build and return the configured application instance.
            return builder.Build();
        }
    }
}
