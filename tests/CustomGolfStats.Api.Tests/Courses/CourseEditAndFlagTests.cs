using System.Net;
using System.Net.Http.Json;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Features.Courses;
using CustomGolfStats.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static CustomGolfStats.Api.Tests.Courses.CourseTestHelpers;

namespace CustomGolfStats.Api.Tests.Courses;

public sealed class CourseEditAndFlagTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Editing_details_of_an_unverified_course_applies_and_marks_it_user_edited()
    {
        var client = await TestAuth.RegisterAsync(factory, "details@example.com");
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, name: "Old Name", status: CourseStatus.Imported));

        var course = await (await client.PutAsJsonAsync($"/courses/{seeded.Course.Id}", DetailsBody("New Name", "Tumalo"), Ct))
            .ReadAsync<CourseDetailDto>(HttpStatusCode.OK);

        Assert.Equal(("New Name", "Tumalo", "https://example.com", CourseStatus.UserEdited), (course.Name, course.City, course.Website, course.Status));
        Assert.Equal(0, await factory.WithDbAsync(db => db.CourseFlags.CountAsync(f => f.CourseId == seeded.Course.Id, Ct)));
    }

    [Fact]
    public async Task Details_edit_validates_fields()
    {
        var client = await TestAuth.RegisterAsync(factory, "detailsbad@example.com");
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, name: "Valid Details"));

        var errors = await (await client.PutAsJsonAsync($"/courses/{seeded.Course.Id}",
            new { name = " ", latitude = 95.0, longitude = 10.0 }, Ct)).ReadValidationErrorsAsync();
        var missing = await client.PutAsJsonAsync($"/courses/{Guid.NewGuid()}", DetailsBody("X"), Ct);

        Assert.Equal(["latitude", "name"], errors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task User_edits_to_a_verified_course_become_flags()
    {
        var email = "proposer@example.com";
        var client = await TestAuth.RegisterAsync(factory, email);
        var accountId = await TestAuth.GetAccountIdAsync(factory, email);
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, name: "Verified Course", status: CourseStatus.Verified));
        var versionsUrl = $"/courses/{seeded.Course.Id}/tees/{seeded.Tee.Id}/versions";

        var details = await (await client.PutAsJsonAsync($"/courses/{seeded.Course.Id}", DetailsBody("Proposed Name"), Ct))
            .ReadAsync<CourseFlagDto>(HttpStatusCode.Accepted);
        var newVersion = await (await client.PostAsJsonAsync(versionsUrl, VersionBody(new DateOnly(2026, 2, 1), rating: 68.8m), Ct))
            .ReadAsync<CourseFlagDto>(HttpStatusCode.Accepted);
        var inPlace = await (await client.PutAsJsonAsync($"{versionsUrl}/{seeded.Version.Id}", VersionBody(rating: 66.6m), Ct))
            .ReadAsync<CourseFlagDto>(HttpStatusCode.Accepted);

        Assert.Contains("Proposed Name", details.Note);
        Assert.Null(details.TeeId);
        Assert.Contains("68.8", newVersion.Note);
        Assert.Contains("2026-02-01", newVersion.Note);
        Assert.Equal(seeded.Tee.Id, newVersion.TeeId);
        Assert.Contains("66.6", inPlace.Note);
        Assert.Contains(seeded.Version.Id.ToString(), inPlace.Note);

        await factory.WithDbAsync(async db =>
        {
            var flags = await db.CourseFlags.Where(f => f.CourseId == seeded.Course.Id).ToListAsync(Ct);
            Assert.Equal(3, flags.Count);
            Assert.All(flags, f => Assert.Equal((accountId, FlagStatus.Open), (f.ReporterAccountId, f.Status)));

            var course = await db.Courses.Include(c => c.Tees).ThenInclude(t => t.Versions).SingleAsync(c => c.Id == seeded.Course.Id, Ct);
            Assert.Equal(("Verified Course", CourseStatus.Verified), (course.Name, course.Status));
            var version = Assert.Single(Assert.Single(course.Tees).Versions);
            Assert.Equal(72.0m, version.Rating);
        });
    }

    [Fact]
    public async Task Admin_edits_to_a_verified_course_apply_and_keep_it_verified()
    {
        var admin = await TestAuth.RegisterAdminAsync(factory, "admin-edit@example.com");
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, name: "Admin Verified", status: CourseStatus.Verified));

        var course = await (await admin.PutAsJsonAsync($"/courses/{seeded.Course.Id}", DetailsBody("Admin Renamed"), Ct))
            .ReadAsync<CourseDetailDto>(HttpStatusCode.OK);
        var version = await (await admin.PostAsJsonAsync($"/courses/{seeded.Course.Id}/tees/{seeded.Tee.Id}/versions",
                VersionBody(new DateOnly(2026, 3, 1)), Ct))
            .ReadAsync<TeeVersionDto>(HttpStatusCode.Created);

        Assert.Equal(("Admin Renamed", CourseStatus.Verified), (course.Name, course.Status));
        Assert.Equal(new DateOnly(2026, 3, 1), version.EffectiveFrom);
        Assert.Equal(0, await factory.WithDbAsync(db => db.CourseFlags.CountAsync(f => f.CourseId == seeded.Course.Id, Ct)));
    }

    [Fact]
    public async Task Flags_are_stored_with_the_reporter()
    {
        var email = "flagger@example.com";
        var client = await TestAuth.RegisterAsync(factory, email);
        var accountId = await TestAuth.GetAccountIdAsync(factory, email);
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, name: "Flagged"));

        var flag = await (await client.PostAsJsonAsync($"/courses/{seeded.Course.Id}/flags",
                new { teeId = seeded.Tee.Id, holeNumber = 7, note = "  Hole 7 is a par 4 now.  " }, Ct))
            .ReadAsync<CourseFlagDto>(HttpStatusCode.Created);
        var courseOnly = await (await client.PostAsJsonAsync($"/courses/{seeded.Course.Id}/flags", new { note = "Closed for good." }, Ct))
            .ReadAsync<CourseFlagDto>(HttpStatusCode.Created);

        Assert.Equal((seeded.Course.Id, seeded.Tee.Id, 7, "Hole 7 is a par 4 now.", FlagStatus.Open, ApiFactory.StartTime),
            (flag.CourseId, flag.TeeId, flag.HoleNumber, flag.Note, flag.Status, flag.CreatedAt));
        Assert.Null(courseOnly.TeeId);
        var stored = await factory.WithDbAsync(db => db.CourseFlags.SingleAsync(f => f.Id == flag.Id, Ct));
        Assert.Equal((accountId, seeded.Tee.Id, 7, FlagStatus.Open), (stored.ReporterAccountId, stored.TeeId, stored.HoleNumber, stored.Status));
    }

    [Fact]
    public async Task Flags_are_validated()
    {
        var client = await TestAuth.RegisterAsync(factory, "badflag@example.com");
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, name: "Flag Target"));
        var other = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, name: "Other Course"));
        var url = $"/courses/{seeded.Course.Id}/flags";

        var errors = await (await client.PostAsJsonAsync(url, new { teeId = other.Tee.Id, holeNumber = 0, note = " " }, Ct))
            .ReadValidationErrorsAsync();
        var unknownCourse = await client.PostAsJsonAsync($"/courses/{Guid.NewGuid()}/flags", new { note = "x" }, Ct);

        Assert.Equal(["holeNumber", "note", "teeId"], errors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.NotFound, unknownCourse.StatusCode);
    }
}
