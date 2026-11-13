using System.Collections.Concurrent;
using System.Net.NetworkInformation;

namespace CustomGolfStats.App.Platforms.Desktop.Services;

/// <summary>The desktop head is for development; it has no OS-backed secret store wired up, so tokens are never written to disk
/// and the session ends when the app closes.</summary>
public sealed class InMemorySecureStore : ISecureStore
{
    private readonly ConcurrentDictionary<string, string> _values = new();

    public Task<string?> GetAsync(string key) => Task.FromResult(_values.TryGetValue(key, out var v) ? v : null);

    public Task SetAsync(string key, string value)
    {
        _values[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        _values.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

public sealed class NoPedometer : IPedometer
{
    public Task<PedometerReading?> ReadAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) => Task.FromResult<PedometerReading?>(null);
}

public sealed class NoLocation : ILocationOnce
{
    public Task<bool> RequestAccessAsync(CancellationToken ct) => Task.FromResult(false);

    public Task<GeoPoint?> GetCoarseLocationAsync(CancellationToken ct) => Task.FromResult<GeoPoint?>(null);
}

public sealed class DesktopConnectivity : IConnectivity
{
    public bool IsOnline => NetworkInterface.GetIsNetworkAvailable();
}
