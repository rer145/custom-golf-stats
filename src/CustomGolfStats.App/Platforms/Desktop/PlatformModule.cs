using CustomGolfStats.App.Platform;
using CustomGolfStats.App.Platforms.Desktop.Services;

namespace CustomGolfStats.App;

/// <summary>Desktop (developer runs only): no secure storage, so tokens live in memory for the session; no sensors.</summary>
internal static class PlatformModule
{
    public static void AddPlatformServices(IServiceCollection services)
    {
        services.AddSingleton<ISecureStore, InMemorySecureStore>();
        services.AddSingleton<IPedometer, NoPedometer>();
        services.AddSingleton<ILocationOnce, NoLocation>();
        services.AddSingleton<IConnectivity, DesktopConnectivity>();
        services.AddSingleton<IMotionPermission, NoMotionPreparation>();
    }
}
