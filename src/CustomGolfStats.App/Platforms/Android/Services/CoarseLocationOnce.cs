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

    public async Task<bool> RequestAccessAsync(CancellationToken ct)
    {
        try
        {
            return await AndroidPermissions.RequestAsync(Manifest.Permission.AccessCoarseLocation).WaitAsync(ct);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <remarks>Never throws: callers may stop waiting (the weather lookup has a timeout), so a late failure here must not surface
    /// as an unobserved task exception.</remarks>
    public async Task<GeoPoint?> GetCoarseLocationAsync(CancellationToken ct)
    {
        try
        {
            return await ReadAsync(ct);
        }
        catch (Exception)
        {
            return null; // includes Android.OS.OperationCanceledException from a cancelled request
        }
    }

    private async Task<GeoPoint?> ReadAsync(CancellationToken ct)
    {
        if (!AndroidPermissions.IsGranted(Manifest.Permission.AccessCoarseLocation))
            return null; // RequestAccessAsync asks first

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
            tcs.TrySetResult(null);
            try
            {
                cancel.Cancel();
            }
            catch (Exception)
            {
                // the platform may report the cancellation itself as an exception
            }
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
