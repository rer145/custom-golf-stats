using System.Net;
using System.Net.Http.Json;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Features.Admin;
using CustomGolfStats.Api.Features.Courses;
using CustomGolfStats.Api.Features.Stats;
using CustomGolfStats.Api.Tests.Infrastructure;
using CustomGolfStats.Api.Tests.Rounds;
using Microsoft.EntityFrameworkCore;
using static CustomGolfStats.Api.Tests.Courses.CourseTestHelpers;

namespace CustomGolfStats.Api.Tests.Admin;

public sealed class AdminCourseTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly int[] CorrectedPars = [5, 5, 3, 5, 4, 4, 3, 4, 5, 4, 3, 4, 5, 4, 4, 3, 5, 4]; // par 74

    [Fact]
    public async Task Courses_are_listed_and_filtered_by_status()
    {
        var admin = await TestAuth.RegisterAdminAsync(factory, "courses-list@example.com");
        var golferId = await TestAuth.GetAccountIdAsync(factory, "courses-list@example.com");
        var imported = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "List Imported", status: CourseStatus.Imported));
        var verified = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "List Verified", status: CourseStatus.Verified));
        await factory.WithDbAsync(async db =>
        {
            db.CourseFlags.Add(new CourseFlag { CourseId = imported.Course.Id, ReporterAccountId = golferId, Note = "Wrong", CreatedAt = ApiFactory.StartTime });
            await db.SaveChangesAsync(Ct);
        });

        var all = await (await admin.GetAsync("/admin/courses", Ct)).ReadAsync<List<AdminCourseSummaryDto>>(HttpStatusCode.OK);
        var onlyImported = await (await admin.GetAsync("/admin/courses?status=imported", Ct))
            .ReadAsync<List<AdminCourseSummaryDto>>(HttpStatusCode.OK);
        var badStatus = await (await admin.GetAsync("/admin/courses?status=Bogus", Ct)).ReadValidationErrorsAsync();

        Assert.Contains(all, c => c.Id == verified.Course.Id && c.Status == CourseStatus.Verified);
        var row = Assert.Single(onlyImported, c => c.Id == imported.Course.Id);
        Assert.Equal(1, row.OpenFlags);
        Assert.DoesNotContain(onlyImported, c => c.Id == verified.Course.Id);
        Assert.Contains("status", badStatus.Keys);
    }

    [Fact]
    public async Task Details_edit_keeps_a_verified_course_verified()
    {
        var admin = await TestAuth.RegisterAdminAsync(factory, "details-admin@example.com");
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "Before Name", status: CourseStatus.Verified));

        var updated = await (await admin.PutAsJsonAsync($"/admin/courses/{seeded.Course.Id}", DetailsBody("After Name", "Sisters"), Ct))
            .ReadAsync<CourseDetailDto>(HttpStatusCode.OK);
        var invalid = await (await admin.PutAsJsonAsync($"/admin/courses/{seeded.Course.Id}", DetailsBody(""), Ct)).ReadValidationErrorsAsync();
        var missing = await admin.PutAsJsonAsync($"/admin/courses/{Guid.NewGuid()}", DetailsBody("X"), Ct);

        Assert.Equal(("After Name", "Sisters", CourseStatus.Verified), (updated.Name, updated.City, updated.Status));
        Assert.Single(updated.Tees);
        Assert.Contains("name", invalid.Keys);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(0, await factory.WithDbAsync(db => db.CourseFlags.CountAsync(f => f.CourseId == seeded.Course.Id, Ct)));
    }

    [Fact]
    public async Task Correcting_a_used_version_changes_the_rounds_stats()
    {
        var admin = await TestAuth.RegisterAdminAsync(factory, "correct-admin@example.com");
        var adminId = await TestAuth.GetAccountIdAsync(factory, "correct-admin@example.com");
        var golfer = await TestAuth.RegisterAsync(factory, "correct-golfer@example.com");
        var golferId = await TestAuth.GetAccountIdAsync(factory, "correct-golfer@example.com");
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "Typo National", status: CourseStatus.Verified));
        var round = await factory.WithDbAsync(db =>
            TestData.SeedQuickRoundAsync(db, golferId, seeded.Version, new DateOnly(2026, 5, 1), [5, 5, 3]));
        var before = await golfer.GetFromJsonAsync<ScorecardResponse>($"/rounds/{round.Id}/scorecard", Ct);
        Assert.Equal(13 - (4 + 4 + 3), before!.ToPar);

        var corrected = await (await admin.PutAsJsonAsync(
                $"/admin/courses/{seeded.Course.Id}/tees/{seeded.Tee.Id}/versions/{seeded.Version.Id}",
                VersionBody(rating: 73.1m, slope: 133, pars: CorrectedPars), Ct))
            .ReadAsync<TeeVersionDto>(HttpStatusCode.OK);

        Assert.Equal((seeded.Version.Id, 74, 73.1m, 133), (corrected.Id, corrected.Par, corrected.Rating, corrected.Slope));
        var after = await golfer.GetFromJsonAsync<ScorecardResponse>($"/rounds/{round.Id}/scorecard", Ct);
        Assert.Equal(13 - (5 + 5 + 3), after!.ToPar);
        Assert.Equal((73.1m, 133), (after.Rating, after.Slope));
        await factory.WithDbAsync(async db =>
        {
            var version = await db.TeeVersions.Include(v => v.Tee).ThenInclude(t => t.Course).SingleAsync(v => v.Id == seeded.Version.Id, Ct);
            Assert.Equal(adminId, version.ChangedByAccountId);
            Assert.Equal(CourseStatus.Verified, version.Tee.Course.Status);
        });
    }

    [Fact]
    public async Task Correcting_a_used_version_rederives_shot_by_shot_hole_summaries()
    {
        var admin = await TestAuth.RegisterAdminAsync(factory, "rederive-admin@example.com");
        var golfer = await TestAuth.RegisterAsync(factory, "rederive-golfer@example.com");
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "Rederive Hills", status: CourseStatus.Verified));
        var roundId = Guid.NewGuid();
        var push = await RoundRequests.PutAsync(golfer, roundId, RoundRequests.ShotByShot(seeded.Tee.Id, ApiFactory.StartTime, RoundRequests.ShotHole(1, 1)));
        Assert.True(push.IsSuccessStatusCode, await push.Content.ReadAsStringAsync(Ct));
        var before = await golfer.GetFromJsonAsync<ScorecardResponse>($"/rounds/{roundId}/scorecard", Ct);
        Assert.True(before!.Holes[0].Result!.Gir); // par 4: on the green in two
        Assert.True(before.Holes[0].Result!.FairwayHit);
        var versionBefore = await factory.WithDbAsync(db => db.Rounds.Where(r => r.Id == roundId).Select(r => r.Version).SingleAsync(Ct));
        factory.Time.Advance(TimeSpan.FromMinutes(1));

        var pars = (int[])TestData.DefaultPars.Clone();
        pars[0] = 3; // hole 1 was really a par 3
        await (await admin.PutAsJsonAsync(
                $"/admin/courses/{seeded.Course.Id}/tees/{seeded.Tee.Id}/versions/{seeded.Version.Id}", VersionBody(pars: pars), Ct))
            .ReadAsync<TeeVersionDto>(HttpStatusCode.OK);

        var after = await golfer.GetFromJsonAsync<ScorecardResponse>($"/rounds/{roundId}/scorecard", Ct);
        Assert.False(after!.Holes[0].Result!.Gir); // par 3: green in one needed
        Assert.False(after.Holes[0].Result!.FairwayHit); // a par 3 isn't a driving hole
        var (modifiedAt, version) = await factory.WithDbAsync(async db =>
        {
            var r = await db.Rounds.SingleAsync(x => x.Id == roundId, Ct);
            return (r.ModifiedAt, r.Version);
        });
        Assert.Equal(factory.Time.GetUtcNow(), modifiedAt);
        Assert.Equal(versionBefore + 1, version);
    }

    [Fact]
    public async Task Correction_checks_the_route_and_the_values()
    {
        var admin = await TestAuth.RegisterAdminAsync(factory, "correct-checks@example.com");
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "Checks"));
        var other = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "Other Checks"));
        var later = await factory.WithDbAsync(async db =>
            await TestData.AddTeeVersionAsync(db, await db.Tees.SingleAsync(t => t.Id == seeded.Tee.Id, Ct), new DateOnly(2025, 1, 1)));
        var url = $"/admin/courses/{seeded.Course.Id}/tees/{seeded.Tee.Id}/versions/{seeded.Version.Id}";

        var wrongCourse = await admin.PutAsJsonAsync(
            $"/admin/courses/{other.Course.Id}/tees/{seeded.Tee.Id}/versions/{seeded.Version.Id}", VersionBody(), Ct);
        var invalid = await (await admin.PutAsJsonAsync(url, VersionBody(slope: 10), Ct)).ReadValidationErrorsAsync();
        var duplicate = await (await admin.PutAsJsonAsync(url, VersionBody(later.EffectiveFrom), Ct)).ReadValidationErrorsAsync();

        Assert.Equal(HttpStatusCode.NotFound, wrongCourse.StatusCode);
        Assert.Contains("slope", invalid.Keys);
        Assert.Contains("effectiveFrom", duplicate.Keys);
    }

    [Fact]
    public async Task Verify_marks_the_course_verified_now()
    {
        var admin = await TestAuth.RegisterAdminAsync(factory, "verify-admin@example.com");
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "To Verify", status: CourseStatus.Imported));
        factory.Time.Advance(TimeSpan.FromMinutes(10));
        var now = factory.Time.GetUtcNow();

        var verified = await (await admin.PostAsync($"/admin/courses/{seeded.Course.Id}/verify", null, Ct))
            .ReadAsync<CourseDetailDto>(HttpStatusCode.OK);
        var missing = await admin.PostAsync($"/admin/courses/{Guid.NewGuid()}/verify", null, Ct);

        Assert.Equal(CourseStatus.Verified, verified.Status);
        Assert.Equal(now, verified.LastVerifiedAt);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
