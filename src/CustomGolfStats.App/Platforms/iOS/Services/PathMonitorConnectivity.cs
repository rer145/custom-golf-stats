using CoreFoundation;
using Network;

namespace CustomGolfStats.App.Platforms.iOS.Services;

/// <summary>Online when the system has a satisfied network path. Assumed online until the first update arrives (a failed request
/// is handled as offline anyway).</summary>
public sealed class PathMonitorConnectivity : IConnectivity, IDisposable
{
    private readonly NWPathMonitor _monitor = new();
    private volatile bool _online = true;

    public PathMonitorConnectivity()
    {
        _monitor.SnapshotHandler = path => _online = path.Status == NWPathStatus.Satisfied;
        _monitor.SetQueue(DispatchQueue.DefaultGlobalQueue);
        _monitor.Start();
    }

    public bool IsOnline => _online;

    public void Dispose()
    {
        _monitor.Cancel();
        _monitor.Dispose();
    }
}
