using System;

namespace CollectIQ.Helpers
{
    /// <summary>
    /// Provides access to the DI container from places where constructor injection isn't used.
    /// </summary>
    public static class ServiceHelper
    {
        /// <summary>
        /// Stores the application's service provider.
        /// </summary>
        private static IServiceProvider? serviceProvider;

        /// <summary>
        /// Gets the service provider for the current platform.
        /// </summary>
        public static IServiceProvider Services =>
#if ANDROID
            MauiApplication.Current.Services;
#elif WINDOWS
            MauiWinUIApplication.Current.Services;
#elif IOS || MACCATALYST
            MauiUIApplicationDelegate.Current.Services;
#else
            throw new PlatformNotSupportedException();
#endif

        public static void Initialize(IServiceProvider services)
        {
            serviceProvider = services;
        }

        /// <summary>
        /// Resolves a registered service.
        /// </summary>
        /// <typeparam name="T">The service type.</typeparam>
        /// <returns>The resolved service instance or null.</returns>
        public static T? GetService<T>() where T : class => Services.GetService(typeof(T)) as T;
    }
}
