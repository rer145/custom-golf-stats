using CustomGolfStats.App.Core.Platform;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.App.Core.Tests.Support;
using CustomGolfStats.App.Core.ViewModels;
using CustomGolfStats.Domain;

namespace CustomGolfStats.App.Core.Tests.ViewModels;

public sealed class FakeNavigator : INavigator
{
    public List<(Route Route, object? Parameter)> Visits { get; } = [];
    public int Backs { get; private set; }

    public Task NavigateAsync(Route route, object? parameter = null)
    {
        Visits.Add((route, parameter));
        return Task.CompletedTask;
    }

    public Task GoBackAsync()
    {
        Backs++;
        return Task.CompletedTask;
    }

    public (Route Route, object? Parameter) Last => Visits[^1];
}

public sealed class FakeConnectivity(bool online = true) : IConnectivity
{
    public bool IsOnline { get; set; } = online;
}

public sealed class FakePedometer : IPedometer
{
    public PedometerReading? Reading { get; set; }
    public bool Throws { get; set; }
    public List<(DateTimeOffset From, DateTimeOffset To)> Calls { get; } = [];

    public Task<PedometerReading?> ReadAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        Calls.Add((from, to));
        if (Throws)
            throw new InvalidOperationException("no motion permission");
        return Task.FromResult(Reading);
    }
}

public sealed class FakeLocation : ILocationOnce
{
    public GeoPoint? Point { get; set; }
    public int Calls { get; private set; }

    public Task<GeoPoint?> GetCoarseLocationAsync(CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(Point);
    }
}

/// <summary>A temp database with the sample course saved, plus helpers to create rounds in it.</summary>
public sealed class VmRig : IAsyncDisposable
{
    public TempDb Temp { get; private init; } = null!;
    public LocalDb Db => Temp.Db;
    public FakeNavigator Nav { get; } = new();

    public static async Task<VmRig> CreateAsync()
    {
        var rig = new VmRig { Temp = await TempDb.CreateAsync() };
        await rig.Db.Courses.SaveFromServerAsync(Samples.Course());
        return rig;
    }

    /// <summary>An empty round on the sample course (hole 1 par 4 120 yd, hole 2 par 5 140 yd, hole 3 par 3 160 yd ...).</summary>
    public async Task<Guid> CreateRoundAsync(EntryMode mode)
    {
        var round = Samples.Round(mode: mode);
        round.FinishedAt = null;
        await Db.Rounds.SaveAsync(new RoundRecord(round, []));
        return round.Id;
    }

    public ValueTask DisposeAsync() => Temp.DisposeAsync();
}
