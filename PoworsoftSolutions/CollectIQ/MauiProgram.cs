//
//  FILE            : MauiProgram.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-10-19
//  DESCRIPTION     :
//      Configures the MAUI app, dependency injection container,
//      and application-level services.
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

            builder.Services.AddSingleton<IDatabase, SqliteDatabase>();
            builder.Services.AddSingleton<IAuthService, LocalAuthService>();
            builder.Services.AddTransient<AuthSheet>();
            builder.Services.AddTransient<LandingPage>();

#if DEBUG
            builder.Logging.AddDebug();
#endif
            return builder.Build();
        }
    }
}
