using System.Net;
using System.Net.Http.Json;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Features.Courses;
using CustomGolfStats.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CustomGolfStats.Api.Tests.Courses.CourseTestHelpers;

namespace CustomGolfStats.Api.Tests.Courses;

public sealed class TeeVersionEditTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly int[] RevisedPars = [4, 4, 3, 5, 4, 4, 3, 4, 5, 4, 3, 4, 5, 4, 4, 3, 4, 4]; // par 71

    [Fact]
    public async Task New_version_applies_from_its_date_and_leaves_older_rounds_alone()
    {
        var email = "newversion@example.com";
        var client = await TestAuth.RegisterAsync(factory, email);
        var accountId = await TestAuth.GetAccountIdAsync(factory, email);
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, name: "Rerated", status: CourseStatus.Imported));
        var oldRound = await factory.WithDbAsync(db =>
            TestData.SeedQuickRoundAsync(db, accountId, seeded.Version, new DateOnly(2025, 5, 1), [4, 4, 4]));

        var created = await (await client.PostAsJsonAsync(
                $"/courses/{seeded.Course.Id}/tees/{seeded.Tee.Id}/versions",
                VersionBody(new DateOnly(2026, 1, 1), rating: 70.8m, slope: 127, pars: RevisedPars), Ct))
            .ReadAsync<TeeVersionDto>(HttpStatusCode.Created);

        Assert.Equal(new DateOnly(2026, 1, 1), created.EffectiveFrom);
        Assert.True(created.IsCurrent);
        Assert.Equal((71, 70.8m, 127), (created.Par, created.Rating, created.Slope));
        Assert.Equal(CourseSource.Manual, created.Source);

        await using var scope = factory.Services.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<TeeVersionService>();
        Assert.Equal(seeded.Version.Id, (await resolver.ResolveAsync(seeded.Tee.Id, new DateOnly(2025, 12, 31), Ct))!.Id);
        Assert.Equal(created.Id, (await resolver.ResolveAsync(seeded.Tee.Id, new DateOnly(2026, 1, 1), Ct))!.Id);

        await factory.WithDbAsync(async db =>
        {
            Assert.Equal(seeded.Version.Id, await db.Rounds.Where(r => r.Id == oldRound.Id).Select(r => r.TeeVersionId).SingleAsync(Ct));
            var original = await db.TeeVersions.Include(v => v.Holes).SingleAsync(v => v.Id == seeded.Version.Id, Ct);
            Assert.Equal(72, original.Par);
            Assert.Equal(72, original.Holes.Sum(h => h.Par));
            Assert.Equal(CourseStatus.UserEdited, (await db.Courses.SingleAsync(c => c.Id == seeded.Course.Id, Ct)).Status);
        });
    }

    [Fact]
    public async Task New_version_needs_a_date_that_the_tee_does_not_already_use()
    {
        var client = await TestAuth.RegisterAsync(factory, "dupdate@example.com");
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, name: "Dup Date"));
        var url = $"/courses/{seeded.Course.Id}/tees/{seeded.Tee.Id}/versions";

        var missing = await (await client.PostAsJsonAsync(url, VersionBody(effectiveFrom: null), Ct)).ReadValidationErrorsAsync();
        var duplicate = await (await client.PostAsJsonAsync(url, VersionBody(TestData.DefaultEffectiveFrom), Ct)).ReadValidationErrorsAsync();

        Assert.Contains("effectiveFrom", missing.Keys);
        Assert.Contains("effectiveFrom", duplicate.Keys);
    }

    [Fact]
    public async Task Unused_version_is_edited_in_place()
    {
        var client = await TestAuth.RegisterAsync(factory, "inplace@example.com");
        var accountId = await TestAuth.GetAccountIdAsync(factory, "inplace@example.com");
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, name: "Typo", status: CourseStatus.Imported));

        var edited = await (await client.PutAsJsonAsync(
                $"/courses/{seeded.Course.Id}/tees/{seeded.Tee.Id}/versions/{seeded.Version.Id}",
                VersionBody(rating: 69.9m, slope: 121, pars: RevisedPars), Ct))
            .ReadAsync<TeeVersionDto>(HttpStatusCode.OK);

        Assert.Equal(seeded.Version.Id, edited.Id);
        Assert.Equal(TestData.DefaultEffectiveFrom, edited.EffectiveFrom);
        Assert.Equal((71, 69.9m, 121, 36, 35), (edited.Par, edited.Rating, edited.Slope, edited.FrontPar, edited.BackPar));
        await factory.WithDbAsync(async db =>
        {
            var version = await db.TeeVersions.Include(v => v.Holes).SingleAsync(v => v.Id == seeded.Version.Id, Ct);
            Assert.Equal(RevisedPars, version.Holes.OrderBy(h => h.Number).Select(h => h.Par));
            Assert.Equal(18, version.Holes.Count);
            Assert.Equal(accountId, version.ChangedByAccountId);
            Assert.Equal(CourseStatus.UserEdited, (await db.Courses.SingleAsync(c => c.Id == seeded.Course.Id, Ct)).Status);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // a soft-deleted round still uses the version
    public async Task Version_used_by_a_round_cannot_be_edited_in_place(bool roundDeleted)
    {
        var email = $"used-{roundDeleted}@example.com";
        var client = await TestAuth.RegisterAsync(factory, email);
        var accountId = await TestAuth.GetAccountIdAsync(factory, email);
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, name: $"Used {roundDeleted}"));
        await factory.WithDbAsync(async db =>
        {
            var round = await TestData.SeedQuickRoundAsync(db, accountId, seeded.Version, new DateOnly(2026, 5, 1), [4]);
            round.DeletedAt = roundDeleted ? ApiFactory.StartTime : null;
            await db.SaveChangesAsync(Ct);
        });

        var response = await client.PutAsJsonAsync(
            $"/courses/{seeded.Course.Id}/tees/{seeded.Tee.Id}/versions/{seeded.Version.Id}",
            VersionBody(rating: 60.0m, pars: RevisedPars), Ct);

        var problem = await response.ReadAsync<ProblemDetails>(HttpStatusCode.Conflict);
        Assert.Contains("new version", problem.Detail);
        var rating = await factory.WithDbAsync(db =>
            db.TeeVersions.Where(v => v.Id == seeded.Version.Id).Select(v => v.Rating).SingleAsync(Ct));
        Assert.Equal(72.0m, rating);
    }

    [Fact]
    public async Task Editor_can_correct_a_used_version_when_allowed()
    {
        var accountId = await RegisterAndGetIdAsync("editor@example.com");
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, name: "Admin Fix", status: CourseStatus.Verified));
        await factory.WithDbAsync(db => TestData.SeedQuickRoundAsync(db, accountId, seeded.Version, new DateOnly(2026, 5, 1), [4]));
        var input = new TeeVersionInput(null, 70.2m, 126, null, null, null, null,
            TestData.DefaultPars.Select((par, i) => new TeeHoleInput(i + 1, par, 400, TestData.DefaultStrokeIndexes[i])).ToList());

        await using var scope = factory.Services.CreateAsyncScope();
        var editor = scope.ServiceProvider.GetRequiredService<TeeVersionEditor>();
        var blocked = await editor.ApplyAsync(seeded.Version.Id, input, allowWhenUsed: false, accountId, Ct);
        var applied = await editor.ApplyAsync(seeded.Version.Id, input, allowWhenUsed: true, accountId, Ct);

        Assert.Equal(TeeVersionEditOutcome.InUse, blocked.Outcome);
        Assert.Equal(TeeVersionEditOutcome.Applied, applied.Outcome);
        Assert.Equal(70.2m, applied.Version!.Rating);
        Assert.All(applied.Version.Holes, h => Assert.Equal(400, h.Yardage));
        // Verified stays verified when the edit is allowed through (admin correction).
        Assert.Equal(CourseStatus.Verified, applied.Version.Tee.Course.Status);
    }

    [Fact]
    public async Task Versions_are_only_reachable_through_their_own_course_and_tee()
    {
        var client = await TestAuth.RegisterAsync(factory, "wrongtee@example.com");
        var a = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, name: "Course A"));
        var b = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, name: "Course B"));

        var put = await client.PutAsJsonAsync($"/courses/{a.Course.Id}/tees/{a.Tee.Id}/versions/{b.Version.Id}", VersionBody(), Ct);
        var post = await client.PostAsJsonAsync($"/courses/{a.Course.Id}/tees/{b.Tee.Id}/versions", VersionBody(new DateOnly(2026, 1, 1)), Ct);

        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
    }

    private async Task<Guid> RegisterAndGetIdAsync(string email)
    {
        await TestAuth.RegisterAsync(factory, email);
        return await TestAuth.GetAccountIdAsync(factory, email);
    }
}
