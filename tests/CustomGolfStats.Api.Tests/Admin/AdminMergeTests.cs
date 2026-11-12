using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Features.Admin;
using CustomGolfStats.Api.Features.Courses;
using CustomGolfStats.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static CustomGolfStats.Api.Tests.Courses.CourseTestHelpers;

namespace CustomGolfStats.Api.Tests.Admin;

public sealed class AdminMergeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Rerated = new(2025, 1, 1);

    [Fact]
    public async Task Merge_moves_rounds_to_matching_tee_versions_and_marks_the_source()
    {
        var admin = await TestAuth.RegisterAdminAsync(factory, "merge-admin@example.com");
        var adminId = await TestAuth.GetAccountIdAsync(factory, "merge-admin@example.com");
        var golfer = await TestAuth.RegisterAsync(factory, "merge-golfer@example.com");
        var golferId = await TestAuth.GetAccountIdAsync(factory, "merge-golfer@example.com");

        var source = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "Pine Valley Dup", teeName: "White"));
        var target = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "Pine Valley", teeName: "WHITE "));
        var (sourceLater, targetLater, oldRound, newRound, deletedRound) = await factory.WithDbAsync(async db =>
        {
            var sl = await TestData.AddTeeVersionAsync(db, await db.Tees.SingleAsync(t => t.Id == source.Tee.Id, Ct), Rerated);
            var tl = await TestData.AddTeeVersionAsync(db, await db.Tees.SingleAsync(t => t.Id == target.Tee.Id, Ct), Rerated);
            // An unused source tee with no match doesn't block the merge.
            db.Tees.Add(new Tee { CourseId = source.Course.Id, Name = "Gold", Gender = Gender.Male });
            var o = await TestData.SeedQuickRoundAsync(db, golferId, source.Version, new DateOnly(2024, 6, 1), [4, 4]);
            var n = await TestData.SeedQuickRoundAsync(db, golferId, sl, new DateOnly(2025, 6, 1), [5, 4]);
            var d = await TestData.SeedQuickRoundAsync(db, golferId, sl, new DateOnly(2025, 7, 1), [5]);
            d.DeletedAt = ApiFactory.StartTime;
            db.CourseFlags.Add(new CourseFlag { CourseId = source.Course.Id, TeeId = source.Tee.Id, ReporterAccountId = golferId, Note = "Open one", CreatedAt = ApiFactory.StartTime });
            db.CourseFlags.Add(new CourseFlag
            {
                CourseId = source.Course.Id,
                ReporterAccountId = golferId,
                Note = "Closed one",
                Status = FlagStatus.Dismissed,
                ResolvedByAccountId = adminId,
                ResolvedAt = ApiFactory.StartTime,
                CreatedAt = ApiFactory.StartTime,
            });
            (await db.Users.SingleAsync(a => a.Id == golferId, Ct)).HomeCourseId = source.Course.Id;
            await db.SaveChangesAsync(Ct);
            return (sl, tl, o, n, d);
        });
        factory.Time.Advance(TimeSpan.FromMinutes(1));
        var now = factory.Time.GetUtcNow();

        var result = await (await admin.PostAsJsonAsync($"/admin/courses/{source.Course.Id}/merge", new { intoCourseId = target.Course.Id }, Ct))
            .ReadAsync<CourseMergeResultDto>(HttpStatusCode.OK);

        Assert.Equal((source.Course.Id, target.Course.Id, 3, 1), (result.CourseId, result.IntoCourseId, result.RoundsMoved, result.FlagsMoved));
        await factory.WithDbAsync(async db =>
        {
            var rounds = await db.Rounds.Where(r => r.AccountId == golferId).ToDictionaryAsync(r => r.Id, Ct);
            Assert.Equal(target.Version.Id, rounds[oldRound.Id].TeeVersionId);
            Assert.Equal(targetLater.Id, rounds[newRound.Id].TeeVersionId);
            Assert.Equal(targetLater.Id, rounds[deletedRound.Id].TeeVersionId);
            Assert.All(rounds.Values, r => Assert.Equal(now, r.ModifiedAt)); // so sync sends the new course and tee
            Assert.All(rounds.Values, r => Assert.Equal(2, r.Version));

            var merged = await db.Courses.SingleAsync(c => c.Id == source.Course.Id, Ct);
            Assert.Equal(target.Course.Id, merged.MergedIntoCourseId);

            var flags = await db.CourseFlags.ToListAsync(Ct);
            var open = Assert.Single(flags, f => f.Note == "Open one");
            Assert.Equal((target.Course.Id, target.Tee.Id), (open.CourseId, open.TeeId));
            Assert.Equal(source.Course.Id, Assert.Single(flags, f => f.Note == "Closed one").CourseId);

            Assert.Equal(target.Course.Id, (await db.Users.SingleAsync(a => a.Id == golferId, Ct)).HomeCourseId);
        });

        var round = await (await golfer.GetAsync($"/rounds/{newRound.Id}", Ct)).Content.ReadAsStringAsync(Ct);
        Assert.Equal(target.Course.Id, JsonDocument.Parse(round).RootElement.GetProperty("courseId").GetGuid());
        var search = await (await admin.GetAsync("/courses?query=Pine", Ct)).ReadAsync<List<CourseSummaryDto>>(HttpStatusCode.OK);
        Assert.Equal([target.Course.Id], search.Select(c => c.Id));
    }

    [Fact]
    public async Task Merge_with_unmatched_used_versions_is_409_and_changes_nothing()
    {
        var admin = await TestAuth.RegisterAdminAsync(factory, "unmatched-admin@example.com");
        await TestAuth.RegisterAsync(factory, "unmatched-golfer@example.com");
        var golferId = await TestAuth.GetAccountIdAsync(factory, "unmatched-golfer@example.com");

        var source = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "Unmatched Source", teeName: "Blue"));
        var target = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "Unmatched Target", teeName: "White"));
        var (redTee, sourceLater, roundIds) = await factory.WithDbAsync(async db =>
        {
            // Matching tee "White" exists in the target, but only with the default version; the target has no Red (Female).
            var white = new Tee { CourseId = source.Course.Id, Name = "White", Gender = Gender.Male };
            var red = new Tee { CourseId = source.Course.Id, Name = "Red", Gender = Gender.Female };
            db.Tees.AddRange(white, red);
            await db.SaveChangesAsync(Ct);
            var whiteDefault = await TestData.AddTeeVersionAsync(db, white, TestData.DefaultEffectiveFrom);
            var whiteLater = await TestData.AddTeeVersionAsync(db, white, Rerated);
            var redDefault = await TestData.AddTeeVersionAsync(db, red, TestData.DefaultEffectiveFrom);
            db.Tees.Add(new Tee { CourseId = target.Course.Id, Name = "Red", Gender = Gender.Male }); // wrong gender
            await db.SaveChangesAsync(Ct);
            var ids = new List<Guid>();
            foreach (var version in new[] { source.Version, whiteDefault, whiteLater, redDefault })
            {
                ids.Add((await TestData.SeedQuickRoundAsync(db, golferId, version, new DateOnly(2025, 6, 1), [4])).Id);
            }

            return (red, whiteLater, ids);
        });

        var response = await admin.PostAsJsonAsync($"/admin/courses/{source.Course.Id}/merge", new { intoCourseId = target.Course.Id }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        var unmatched = body.GetProperty("unmatched").EnumerateArray()
            .Select(u => (u.GetProperty("teeName").GetString(), u.GetProperty("gender").GetString(), u.GetProperty("versionId").GetGuid()))
            .OrderBy(u => u.Item1).ThenBy(u => u.Item3)
            .ToList();
        Assert.Equal(3, unmatched.Count);
        Assert.Contains(("Blue", "Male", source.Version.Id), unmatched);
        Assert.Contains(("Red", "Female", redTee.Versions.Single().Id), unmatched);
        Assert.Contains(("White", "Male", sourceLater.Id), unmatched);
        await factory.WithDbAsync(async db =>
        {
            Assert.Null((await db.Courses.SingleAsync(c => c.Id == source.Course.Id, Ct)).MergedIntoCourseId);
            var onSource = await db.Rounds.CountAsync(r => roundIds.Contains(r.Id) && r.TeeVersion.Tee.CourseId == source.Course.Id, Ct);
            Assert.Equal(4, onSource);
        });
    }

    [Fact]
    public async Task Merge_rejects_bad_targets()
    {
        var admin = await TestAuth.RegisterAdminAsync(factory, "badmerge-admin@example.com");
        var a = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "Bad A"));
        var b = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "Bad B"));
        var mergedAway = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "Bad Merged"));
        await factory.WithDbAsync(async db =>
        {
            (await db.Courses.SingleAsync(c => c.Id == mergedAway.Course.Id, Ct)).MergedIntoCourseId = b.Course.Id;
            await db.SaveChangesAsync(Ct);
        });

        async Task<HttpResponseMessage> Merge(Guid id, Guid? into) =>
            await admin.PostAsJsonAsync($"/admin/courses/{id}/merge", new { intoCourseId = into }, Ct);

        Assert.Contains("intoCourseId", (await (await Merge(a.Course.Id, a.Course.Id)).ReadValidationErrorsAsync()).Keys);
        Assert.Contains("intoCourseId", (await (await Merge(a.Course.Id, mergedAway.Course.Id)).ReadValidationErrorsAsync()).Keys);
        Assert.Contains("intoCourseId", (await (await Merge(a.Course.Id, Guid.NewGuid())).ReadValidationErrorsAsync()).Keys);
        Assert.Contains("intoCourseId", (await (await Merge(a.Course.Id, null)).ReadValidationErrorsAsync()).Keys);
        Assert.Equal(HttpStatusCode.NotFound, (await Merge(Guid.NewGuid(), b.Course.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Merge(mergedAway.Course.Id, a.Course.Id)).StatusCode);
    }

    [Fact]
    public async Task Courses_already_merged_into_the_source_follow_it_to_the_target()
    {
        var admin = await TestAuth.RegisterAdminAsync(factory, "chain-admin@example.com");
        var older = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "Chain Older"));
        var middle = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "Chain Middle"));
        var final = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "Chain Final"));
        await factory.WithDbAsync(async db =>
        {
            (await db.Courses.SingleAsync(c => c.Id == older.Course.Id, Ct)).MergedIntoCourseId = middle.Course.Id;
            await db.SaveChangesAsync(Ct);
        });

        await (await admin.PostAsJsonAsync($"/admin/courses/{middle.Course.Id}/merge", new { intoCourseId = final.Course.Id }, Ct))
            .ReadAsync<CourseMergeResultDto>(HttpStatusCode.OK);

        var mergedInto = await factory.WithDbAsync(db =>
            db.Courses.Where(c => c.Id == older.Course.Id).Select(c => c.MergedIntoCourseId).SingleAsync(Ct));
        Assert.Equal(final.Course.Id, mergedInto);
    }
}
