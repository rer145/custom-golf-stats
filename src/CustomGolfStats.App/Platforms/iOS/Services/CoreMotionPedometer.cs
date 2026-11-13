using CoreMotion;
using Foundation;

namespace CustomGolfStats.App.Platforms.iOS.Services;

/// <summary>Steps and distance between two times from CoreMotion's pedometer history (up to seven days back). The first query
/// shows the Motion and Fitness permission prompt. Null when the device has no step counting or the golfer said no.</summary>
public sealed class CoreMotionPedometer : IPedometer
{
    private const double MetersPerMile = 1609.344;
    private readonly CMPedometer _pedometer = new();

    public async Task<PedometerReading?> ReadAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (!CMPedometer.IsStepCountingAvailable || to <= from)
            return null;
        if (CMPedometer.AuthorizationStatus is CMAuthorizationStatus.Denied or CMAuthorizationStatus.Restricted)
            return null;
        try
        {
            var data = await _pedometer.QueryPedometerDataAsync((NSDate)from.UtcDateTime, (NSDate)to.UtcDateTime).WaitAsync(ct);
            if (data is null)
                return null;
            double? miles = data.Distance is { } meters ? meters.DoubleValue / MetersPerMile : null;
            return new PedometerReading(data.NumberOfSteps.Int32Value, miles);
        }
        catch (NSErrorException)
        {
            return null; // not permitted or no data
        }
    }
}
