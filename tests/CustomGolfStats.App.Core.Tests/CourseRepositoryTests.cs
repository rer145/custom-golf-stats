using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.App.Core.Tests.Support;
using CustomGolfStats.Domain.Holes;

namespace CustomGolfStats.App.Core.Tests;

public class CourseRepositoryTests
{
    [Fact]
    public async Task Course_with_tees_versions_and_holes_round_trips()
    {
        await using var t = await TempDb.CreateAsync();
        await t.Db.Courses.SaveAsync(Samples.Course());
        await t.ReopenAsync();

        var loaded = await t.Db.Courses.GetAsync(Samples.CourseId);

        Assert.NotNull(loaded);
        Assert.Equal("Pine Valley", loaded.Course.Name);
        Assert.Equal(Api.Client.CourseStatus.Verified, loaded.Course.Status);
        Assert.Equal(39.78, loaded.Course.Latitude);
        var tee = Assert.Single(loaded.Tees);
        Assert.Equal("Blue", tee.Tee.Name);
        Assert.Equal(Api.Client.Gender.Male, tee.Tee.Gender);
        var version = Assert.Single(tee.Versions);
        Assert.Equal(71.2, version.Version.Rating);
        Assert.Equal(130, version.Version.Slope);
        Assert.Equal(35.6, version.Version.FrontRating);
        Assert.Null(version.Version.BackRating);
        Assert.Equal(18, version.Holes.Count);
        Assert.Equal(Enumerable.Range(1, 18), version.Holes.Select(h => h.Number));
    }

    [Fact]
    public async Task Tee_version_holes_map_to_domain_HoleInfo()
    {
        await using var t = await TempDb.CreateAsync();
        await t.Db.Courses.SaveAsync(Samples.Course());

        var infos = await t.Db.Courses.GetHoleInfosAsync(Samples.TeeVersionId);

        Assert.Equal(18, infos.Count);
        Assert.Equal(new HoleInfo(1, 4, 120, 1), infos[0]);
        Assert.Equal(new HoleInfo(3, 3, 160, 3), infos[2]);
    }

    [Fact]
    public async Task Saving_again_replaces_tees_and_holes_so_no_stale_rows_remain()
    {
        await using var t = await TempDb.CreateAsync();
        await t.Db.Courses.SaveAsync(Samples.Course());
        var smaller = Samples.Course("Pine Valley Renamed");
        smaller.Tees[0].Versions[0].Holes.RemoveRange(9, 9); // now a 9 hole layout

        await t.Db.Courses.SaveAsync(smaller);

        var loaded = (await t.Db.Courses.GetAsync(Samples.CourseId))!;
        Assert.Equal("Pine Valley Renamed", loaded.Course.Name);
        Assert.Equal(9, loaded.Tees[0].Versions[0].Holes.Count);
        Assert.Equal(1, await t.Db.Connection.Table<CourseRow>().CountAsync());
        Assert.Equal(9, await t.Db.Connection.Table<TeeHoleRow>().CountAsync());
    }

    [Fact]
    public async Task Favorite_flag_survives_a_server_refresh()
    {
        await using var t = await TempDb.CreateAsync();
        await t.Db.Courses.SaveAsync(Samples.Course());
        await t.Db.Courses.SetFavoriteAsync(Samples.CourseId, true);

        await t.Db.Courses.SaveFromServerAsync(Samples.Course("Pine Valley (updated)"));

        var loaded = (await t.Db.Courses.GetAsync(Samples.CourseId))!;
        Assert.True(loaded.Course.IsFavorite);
        Assert.Equal("Pine Valley (updated)", loaded.Course.Name);
        Assert.False(loaded.Course.PendingSync);
    }

    [Fact]
    public async Task Manual_courses_are_pending_until_synced()
    {
        await using var t = await TempDb.CreateAsync();
        var manual = Samples.Course("My Home Track");
        manual.Course.Source = Api.Client.CourseSource.Manual;

        await t.Db.Courses.SaveLocalAsync(manual);

        var pending = await t.Db.Courses.GetPendingAsync();
        Assert.Equal([Samples.CourseId], pending.Select(p => p.Course.Id));
        Assert.True(pending[0].Course.PendingSync);

        await t.Db.Courses.SaveFromServerAsync(manual);
        Assert.Empty(await t.Db.Courses.GetPendingAsync());
    }

    [Fact]
    public async Task Search_matches_name_club_and_city_and_lists_favorites_first()
    {
        await using var t = await TempDb.CreateAsync();
        var a = Samples.Course("Alpha Links"); // reuse ids on purpose is wrong, so build distinct courses
        var other = Samples.Course("Bravo Hills");
        other.Course.Id = Guid.NewGuid();
        other.Course.City = "Springfield";
        other.Tees[0].Tee.Id = Guid.NewGuid();
        other.Tees[0].Tee.CourseId = other.Course.Id;
        other.Tees[0].Versions[0].Version.Id = Guid.NewGuid();
        other.Tees[0].Versions[0].Version.TeeId = other.Tees[0].Tee.Id;
        other.Tees[0].Versions[0].Holes.ForEach(h => h.TeeVersionId = other.Tees[0].Versions[0].Version.Id);
        await t.Db.Courses.SaveAsync(a);
        await t.Db.Courses.SaveAsync(other);

        Assert.Equal(["Bravo Hills"], (await t.Db.Courses.SearchAsync("spring")).Select(c => c.Name));
        Assert.Equal(["Alpha Links"], (await t.Db.Courses.SearchAsync("alpha")).Select(c => c.Name));
        Assert.Equal(2, (await t.Db.Courses.SearchAsync("")).Count);

        await t.Db.Courses.SetFavoriteAsync(other.Course.Id, true);
        Assert.Equal("Bravo Hills", (await t.Db.Courses.SearchAsync("")).First().Name);
    }

    [Fact]
    public async Task Latest_version_as_of_a_date_picks_the_one_in_effect()
    {
        await using var t = await TempDb.CreateAsync();
        var course = Samples.Course();
        var v2 = new TeeVersionRow { Id = Guid.NewGuid(), TeeId = Samples.TeeId, EffectiveFrom = new DateTime(2025, 1, 1), Par = 72, Rating = 72.0, Slope = 133 };
        course.Tees[0].Versions.Add(new TeeVersionRecord(v2, [new TeeHoleRow { TeeVersionId = v2.Id, Number = 1, Par = 4, Yardage = 400, StrokeIndex = 1 }]));
        await t.Db.Courses.SaveAsync(course);

        var asOf2024 = await t.Db.Courses.GetVersionInEffectAsync(Samples.TeeId, new DateTime(2024, 6, 1));
        var asOf2026 = await t.Db.Courses.GetVersionInEffectAsync(Samples.TeeId, new DateTime(2026, 6, 1));

        Assert.Equal(Samples.TeeVersionId, asOf2024!.Id);
        Assert.Equal(v2.Id, asOf2026!.Id);
    }

    [Fact]
    public async Task A_date_before_the_first_version_uses_the_earliest_one_like_the_API()
    {
        await using var t = await TempDb.CreateAsync();
        var course = Samples.Course();
        var v2 = new TeeVersionRow { Id = Guid.NewGuid(), TeeId = Samples.TeeId, EffectiveFrom = new DateTime(2025, 1, 1), Par = 72, Rating = 72.0, Slope = 133 };
        course.Tees[0].Versions.Add(new TeeVersionRecord(v2, []));
        await t.Db.Courses.SaveAsync(course);

        var before = await t.Db.Courses.GetVersionInEffectAsync(Samples.TeeId, new DateTime(2010, 1, 1));

        Assert.Equal(Samples.TeeVersionId, before!.Id); // sample version is effective 2020: the earliest
        Assert.Null(await t.Db.Courses.GetVersionInEffectAsync(Guid.NewGuid(), new DateTime(2026, 1, 1))); // a tee with no versions
    }
}
