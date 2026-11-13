using CustomGolfStats.App.Core.Platform;
using CustomGolfStats.App.Core.Storage.Repositories;
using CustomGolfStats.App.Core.Storage.Rows;
using SQLite;

namespace CustomGolfStats.App.Core.Storage;

/// <summary>One schema step. <paramref name="Apply"/> runs inside a transaction together with the <c>user_version</c> bump.</summary>
public sealed record Migration(int Version, Action<SQLiteConnection> Apply);

/// <summary>
/// The app's SQLite database. <see cref="OpenAsync"/> creates the file when missing and migrates it by <c>PRAGMA user_version</c>.
/// </summary>
public sealed class LocalDb : IAsyncDisposable
{
    static LocalDb() => SQLitePCL.Batteries_V2.Init();

    /// <summary>
    /// Version 1 creates the tables from the current row classes. A later migration must therefore be idempotent
    /// (check for the column/table first), because a brand-new install creates the newest shape at version 1 and then replays later steps.
    /// </summary>
    public static IReadOnlyList<Migration> DefaultMigrations { get; } =
    [
        new Migration(1, c =>
        {
            c.CreateTable<RoundRow>();
            c.CreateTable<HoleRow>();
            c.CreateTable<ShotRow>();
            c.CreateTable<CourseRow>();
            c.CreateTable<TeeRow>();
            c.CreateTable<TeeVersionRow>();
            c.CreateTable<TeeHoleRow>();
            c.CreateTable<HandicapRow>();
            c.CreateTable<EntitlementsRow>();
            c.CreateTable<SyncStateRow>();
        }),
        // Version 2: drafts of holes being entered shot by shot, so an unsaved hole survives the app closing.
        new Migration(2, c => c.CreateTable<ShotDraftRow>()),
    ];

    public static int LatestSchemaVersion => DefaultMigrations[^1].Version;

    private bool _disposed;

    private LocalDb(SQLiteAsyncConnection connection, IClock clock)
    {
        Connection = connection;
        Rounds = new RoundRepository(connection, clock);
        Courses = new CourseRepository(connection);
        Handicap = new HandicapRepository(connection);
        Entitlements = new EntitlementsRepository(connection, clock);
        SyncState = new SyncStateRepository(connection);
        Drafts = new ShotDraftRepository(connection);
    }

    /// <summary>The raw connection, for diagnostics and tests. Prefer the repositories.</summary>
    public SQLiteAsyncConnection Connection { get; }
    public RoundRepository Rounds { get; }
    public CourseRepository Courses { get; }
    public HandicapRepository Handicap { get; }
    public EntitlementsRepository Entitlements { get; }
    public SyncStateRepository SyncState { get; }
    public ShotDraftRepository Drafts { get; }

    public static async Task<LocalDb> OpenAsync(string path, IClock clock, IReadOnlyList<Migration>? migrations = null)
    {
        migrations ??= DefaultMigrations;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var connection = new SQLiteAsyncConnection(path, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
        try
        {
            await connection.ExecuteAsync("PRAGMA foreign_keys = ON").ConfigureAwait(false);
            await MigrateAsync(connection, migrations).ConfigureAwait(false);
        }
        catch
        {
            await connection.CloseAsync().ConfigureAwait(false);
            throw;
        }

        return new LocalDb(connection, clock);
    }

    /// <summary>Removes everything that belongs to the signed-in account: rounds, holes, shots, drafts, the course cache, handicap,
    /// entitlements and the sync cursor. Used when a different account signs in on this device.</summary>
    public Task ClearAccountDataAsync() => Connection.RunInTransactionAsync(c =>
    {
        foreach (var table in new[] { "ShotDraftRow", "ShotRow", "HoleRow", "RoundRow", "TeeHoleRow", "TeeVersionRow", "TeeRow", "CourseRow", "HandicapRow", "EntitlementsRow", "SyncStateRow" })
            c.Execute($"DELETE FROM {table}");
    });

    public Task<int> GetSchemaVersionAsync() => Connection.ExecuteScalarAsync<int>("PRAGMA user_version");

    private static async Task MigrateAsync(SQLiteAsyncConnection connection, IReadOnlyList<Migration> migrations)
    {
        var current = await connection.ExecuteScalarAsync<int>("PRAGMA user_version").ConfigureAwait(false);
        var latest = migrations[^1].Version;
        if (current > latest)
            throw new InvalidOperationException($"The database is schema version {current}, newer than this app supports ({latest}). Update the app.");

        foreach (var migration in migrations.Where(m => m.Version > current).OrderBy(m => m.Version))
        {
            await connection.RunInTransactionAsync(c =>
            {
                migration.Apply(c);
                c.Execute($"PRAGMA user_version = {migration.Version}");
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await Connection.CloseAsync().ConfigureAwait(false);
    }
}
