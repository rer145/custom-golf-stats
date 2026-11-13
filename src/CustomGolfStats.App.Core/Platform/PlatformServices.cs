namespace CustomGolfStats.App.Core.Platform;

/// <summary>Steps (and distance, where the platform measures it) between two times.</summary>
public sealed record PedometerReading(int Steps, double? DistanceMiles);

public interface IPedometer
{
    /// <summary>The golfer's steps from <paramref name="from"/> to <paramref name="to"/>, or null when unavailable or not permitted.</summary>
    Task<PedometerReading?> ReadAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public sealed record GeoPoint(double Latitude, double Longitude);

public interface ILocationOnce
{
    /// <summary>Asks for (coarse, when-in-use) location permission if it was never asked, and says whether it is granted. Waits
    /// for the golfer's answer, however long that takes: callers start their own time limits only after this.</summary>
    Task<bool> RequestAccessAsync(CancellationToken ct);

    /// <summary>One coarse location reading, or null when unavailable or not permitted. No tracking.</summary>
    Task<GeoPoint?> GetCoarseLocationAsync(CancellationToken ct);
}

public interface IConnectivity
{
    bool IsOnline { get; }
}
