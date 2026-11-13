using CustomGolfStats.App.Platform;
using CustomGolfStats.App.Platforms.iOS.Services;

namespace CustomGolfStats.App;

/// <summary>iOS: Keychain, CoreMotion pedometer (history is kept by the motion coprocessor), one-shot Core Location, NWPathMonitor.</summary>
internal static class PlatformModule
{
    public static void AddPlatformServices(IServiceCollection services)
    {
        services.AddSingleton<ISecureStore, KeychainSecureStore>();
        services.AddSingleton<IPedometer, CoreMotionPedometer>();
        services.AddSingleton<ILocationOnce, CoreLocationOnce>();
        services.AddSingleton<IConnectivity, PathMonitorConnectivity>();
        services.AddSingleton<IMotionPermission, NoMotionPreparation>();
    }
}
