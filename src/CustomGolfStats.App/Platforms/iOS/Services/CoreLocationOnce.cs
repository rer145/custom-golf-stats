using CoreLocation;
using UIKit;

namespace CustomGolfStats.App.Platforms.iOS.Services;

/// <summary>One reduced-accuracy reading (kilometre accuracy) for weather when the course has no coordinates. Asks for
/// when-in-use permission the first time; never tracks.</summary>
public sealed class CoreLocationOnce : ILocationOnce
{
    public async Task<GeoPoint?> GetCoarseLocationAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<GeoPoint?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CLLocationManager? manager = null;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // CLLocationManager must be created and driven on a thread with a run loop: the main thread.
        UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
        {
            try
            {
                manager = new CLLocationManager { DesiredAccuracy = CLLocation.AccuracyKilometer, Delegate = new OneShotDelegate(tcs) };
                Continue(manager, tcs);
                started.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                started.TrySetResult();
            }
        });

        await started.Task;
        using var registration = ct.Register(() => tcs.TrySetResult(null));
        try
        {
            return await tcs.Task;
        }
        finally
        {
            UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
            {
                manager?.StopUpdatingLocation();
                manager?.Dispose();
            });
        }
    }

    /// <summary>Asks for permission when it was never asked, requests one location when allowed, gives up otherwise.</summary>
    private static void Continue(CLLocationManager manager, TaskCompletionSource<GeoPoint?> tcs)
    {
        switch (manager.AuthorizationStatus)
        {
            case CLAuthorizationStatus.NotDetermined:
                manager.RequestWhenInUseAuthorization();
                break;
            case CLAuthorizationStatus.AuthorizedWhenInUse or CLAuthorizationStatus.AuthorizedAlways:
                manager.RequestLocation();
                break;
            default:
                tcs.TrySetResult(null);
                break;
        }
    }

    private sealed class OneShotDelegate(TaskCompletionSource<GeoPoint?> tcs) : CLLocationManagerDelegate
    {
        public override void LocationsUpdated(CLLocationManager manager, CLLocation[] locations)
        {
            if (locations.LastOrDefault() is { } last)
                tcs.TrySetResult(new GeoPoint(last.Coordinate.Latitude, last.Coordinate.Longitude));
        }

        public override void Failed(CLLocationManager manager, Foundation.NSError error) => tcs.TrySetResult(null);

        public override void DidChangeAuthorization(CLLocationManager manager)
        {
            if (manager.AuthorizationStatus != CLAuthorizationStatus.NotDetermined && !tcs.Task.IsCompleted)
                Continue(manager, tcs);
        }
    }
}
