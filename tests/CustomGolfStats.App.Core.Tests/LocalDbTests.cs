using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.App.Core.Tests.Support;

namespace CustomGolfStats.App.Core.Tests;

public class LocalDbTests
{
    [Fact]
    public async Task Empty_file_is_upgraded_to_the_latest_schema()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cgs-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "golf.db");
        await File.WriteAllBytesAsync(path, [], TestContext.Current.CancellationToken); // an empty file, as if just created
        try
        {
            await using var db = await LocalDb.OpenAsync(path, new FakeClock(DateTimeOffset.UnixEpoch));

            Assert.Equal(LocalDb.LatestSchemaVersion, await db.GetSchemaVersionAsync());
            foreach (var table in new[] { "RoundRow", "HoleRow", "ShotRow", "CourseRow", "TeeRow", "TeeVersionRow", "TeeHoleRow", "HandicapRow", "EntitlementsRow", "SyncStateRow" })
            {
                var found = await db.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = ?", table);
                Assert.Equal(1, found);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_file_is_created()
    {
        await using var t = await TempDb.CreateAsync();

        Assert.True(File.Exists(t.Path_));
        Assert.Equal(LocalDb.LatestSchemaVersion, await t.Db.GetSchemaVersionAsync());
    }

    [Fact]
    public async Task Reopening_keeps_data_and_does_not_rerun_migrations()
    {
        await using var t = await TempDb.CreateAsync();
        await t.Db.Rounds.SaveAsync(Samples.FullShotRound());

        await t.ReopenAsync();

        Assert.Single(await t.Db.Rounds.ListAsync());
        Assert.Equal(LocalDb.LatestSchemaVersion, await t.Db.GetSchemaVersionAsync());
    }

    [Fact]
    public async Task A_newer_migration_upgrades_an_existing_database_and_keeps_its_data()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        await t.Db.Rounds.SaveAsync(round);

        var next = LocalDb.LatestSchemaVersion + 1;
        var extra = new Migration(next, c => c.Execute("CREATE TABLE FutureThing (Id INTEGER PRIMARY KEY)"));
        await t.ReopenAsync([.. LocalDb.DefaultMigrations, extra]);

        Assert.Equal(next, await t.Db.GetSchemaVersionAsync());
        Assert.Equal(1, await t.Db.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM sqlite_master WHERE name = 'FutureThing'"));
        var back = await t.Db.Rounds.GetAsync(round.Round.Id);
        Assert.NotNull(back);
        Assert.Equal(2, back.Holes.Count);
    }

    [Fact]
    public async Task A_database_from_a_newer_app_is_refused()
    {
        await using var t = await TempDb.CreateAsync();
        await t.Db.Connection.ExecuteAsync($"PRAGMA user_version = {LocalDb.LatestSchemaVersion + 5}");

        await Assert.ThrowsAsync<InvalidOperationException>(() => t.ReopenAsync());
    }
}
