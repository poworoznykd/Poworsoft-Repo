//
//  FILE            : MauiProgram.cs
//  PROJECT         : CollectIQ
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-21
//  DESCRIPTION     :
//      Configures CollectIQ services and pages for dependency injection.
//
using CollectIQ.Interfaces;
using CollectIQ.Services;
using CollectIQ.Views;
using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;

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
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
            builder.Logging.AddDebug();
#endif

            // Register services
            builder.Services.AddSingleton<IDatabase, SqliteDatabase>();
            builder.Services.AddSingleton<IAuthService, LocalAuthService>();

            // Register views
            builder.Services.AddTransient<LandingPage>();
            builder.Services.AddTransient<AuthSheet>();
            builder.Services.AddTransient<AppShell>();

            return builder.Build();
        }
    }
}
