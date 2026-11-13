using CustomGolfStats.App.Core.Platform;
using CustomGolfStats.App.Core.Storage;

namespace CustomGolfStats.App.Core.Tests.Support;

public sealed class FakeClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = start;
    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>A LocalDb on a temp file, deleted on dispose. Reopen simulates an app restart.</summary>
public sealed class TempDb : IAsyncDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cgs-core-tests-" + Guid.NewGuid().ToString("N"));

    public string Path_ { get; }
    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
    public LocalDb Db { get; private set; } = null!;

    private TempDb()
    {
        Directory.CreateDirectory(_dir);
        Path_ = System.IO.Path.Combine(_dir, "golf.db");
    }

    public static async Task<TempDb> CreateAsync(IReadOnlyList<Migration>? migrations = null)
    {
        var t = new TempDb();
        t.Db = await LocalDb.OpenAsync(t.Path_, t.Clock, migrations);
        return t;
    }

    public async Task ReopenAsync(IReadOnlyList<Migration>? migrations = null)
    {
        await Db.DisposeAsync();
        Db = await LocalDb.OpenAsync(Path_, Clock, migrations);
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }
}
