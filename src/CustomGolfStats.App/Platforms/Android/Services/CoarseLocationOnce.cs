using Android;
using Android.Content;
using Android.Locations;
using Android.OS;

namespace CustomGolfStats.App.Platforms.Android.Services;

/// <summary>One approximate (network) location for weather. Asks for coarse location the first time; a recent last-known fix is
/// used when there is one, otherwise one current fix is requested (Android 11+). Never tracks.</summary>
public sealed class CoarseLocationOnce(Context context) : ILocationOnce
{
    private static readonly TimeSpan RecentEnough = TimeSpan.FromMinutes(30);

    public async Task<GeoPoint?> GetCoarseLocationAsync(CancellationToken ct)
    {
        if (!await AndroidPermissions.RequestAsync(Manifest.Permission.AccessCoarseLocation))
            return null;
        if (context.GetSystemService(Context.LocationService) is not LocationManager manager)
            return null;

        var provider = manager.IsProviderEnabled(LocationManager.NetworkProvider) ? LocationManager.NetworkProvider
            : manager.IsProviderEnabled(LocationManager.PassiveProvider) ? LocationManager.PassiveProvider : null;
        if (provider is null)
            return null;

        if (manager.GetLastKnownLocation(provider) is { } last
            && DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(last.Time) < RecentEnough)
            return new GeoPoint(last.Latitude, last.Longitude);

        if (!OperatingSystem.IsAndroidVersionAtLeast(30))
            return manager.GetLastKnownLocation(provider) is { } older ? new GeoPoint(older.Latitude, older.Longitude) : null;

        var tcs = new TaskCompletionSource<GeoPoint?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = new CancellationSignal();
        using var registration = ct.Register(() =>
        {
            cancel.Cancel();
            tcs.TrySetResult(null);
        });
        manager.GetCurrentLocation(provider, cancel, context.MainExecutor!, new Consumer(tcs));
        return await tcs.Task;
    }

    private sealed class Consumer(TaskCompletionSource<GeoPoint?> tcs) : Java.Lang.Object, Java.Util.Functions.IConsumer
    {
        public void Accept(Java.Lang.Object? value) =>
            tcs.TrySetResult(value is Location l ? new GeoPoint(l.Latitude, l.Longitude) : null);
    }
}
