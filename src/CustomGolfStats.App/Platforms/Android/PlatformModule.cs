using CustomGolfStats.App.Platform;
using CustomGolfStats.App.Platforms.Android.Services;

namespace CustomGolfStats.App;

/// <summary>Android: Keystore-encrypted preferences, step-counter sampling, one-shot coarse location, ConnectivityManager.</summary>
internal static class PlatformModule
{
    public static void AddPlatformServices(IServiceCollection services)
    {
        var context = global::Android.App.Application.Context;
        services.AddSingleton<ISecureStore>(_ => new KeystoreSecureStore(context));
        services.AddSingleton(_ => new StepCounterPedometer(context));
        services.AddSingleton<IPedometer>(sp => sp.GetRequiredService<StepCounterPedometer>());
        services.AddSingleton<IMotionPermission>(sp => sp.GetRequiredService<StepCounterPedometer>());
        services.AddSingleton<ILocationOnce>(_ => new CoarseLocationOnce(context));
        services.AddSingleton<IConnectivity>(_ => new AndroidConnectivity(context));
    }
}
