using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Features.Admin;
using CustomGolfStats.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static CustomGolfStats.Api.Tests.Courses.CourseTestHelpers;

namespace CustomGolfStats.Api.Tests.Admin;

public sealed class AdminFlagAndSettingsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Flags_are_listed_by_status_and_can_be_resolved_or_dismissed()
    {
        var admin = await TestAuth.RegisterAdminAsync(factory, "flags-admin@example.com");
        var adminId = await TestAuth.GetAccountIdAsync(factory, "flags-admin@example.com");
        var golfer = await TestAuth.RegisterAsync(factory, "flags-golfer@example.com");
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "Flagged Links", teeName: "Black"));
        var first = await (await golfer.PostAsJsonAsync($"/courses/{seeded.Course.Id}/flags", new { teeId = seeded.Tee.Id, holeNumber = 7, note = "Hole 7 is a par 4" }, Ct))
            .ReadAsync<JsonElement>(HttpStatusCode.Created);
        var second = await (await golfer.PostAsJsonAsync($"/courses/{seeded.Course.Id}/flags", new { note = "Phone number is wrong" }, Ct))
            .ReadAsync<JsonElement>(HttpStatusCode.Created);
        var firstId = first.GetProperty("id").GetGuid();
        var secondId = second.GetProperty("id").GetGuid();

        var open = await (await admin.GetAsync("/admin/flags", Ct)).ReadAsync<List<AdminFlagDto>>(HttpStatusCode.OK);
        var row = Assert.Single(open, f => f.Id == firstId);
        Assert.Equal(("Flagged Links", "Black", 7, "flags-golfer@example.com", FlagStatus.Open),
            (row.CourseName, row.TeeName, row.HoleNumber, row.ReporterEmail, row.Status));
        Assert.Equal(2, open.Count);

        factory.Time.Advance(TimeSpan.FromMinutes(3));
        var resolved = await (await admin.PostAsync($"/admin/flags/{firstId}/resolve", null, Ct)).ReadAsync<AdminFlagDto>(HttpStatusCode.OK);
        var dismissed = await (await admin.PostAsync($"/admin/flags/{secondId}/dismiss", null, Ct)).ReadAsync<AdminFlagDto>(HttpStatusCode.OK);

        Assert.Equal((FlagStatus.Resolved, adminId, factory.Time.GetUtcNow()), (resolved.Status, resolved.ResolvedByAccountId, resolved.ResolvedAt));
        Assert.Equal(FlagStatus.Dismissed, dismissed.Status);
        Assert.Empty(await (await admin.GetAsync("/admin/flags?status=Open", Ct)).ReadAsync<List<AdminFlagDto>>(HttpStatusCode.OK));
        Assert.Equal([firstId], (await (await admin.GetAsync("/admin/flags?status=resolved", Ct))
            .ReadAsync<List<AdminFlagDto>>(HttpStatusCode.OK)).Select(f => f.Id));

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/admin/flags/{firstId}/dismiss", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/admin/flags/{Guid.NewGuid()}/resolve", null, Ct)).StatusCode);
        Assert.Contains("status", (await (await admin.GetAsync("/admin/flags?status=Nope", Ct)).ReadValidationErrorsAsync()).Keys);
        Assert.Equal(FlagStatus.Dismissed, await factory.WithDbAsync(db => db.CourseFlags.Where(f => f.Id == secondId).Select(f => f.Status).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Trial_allowance_setting_is_read_and_updated()
    {
        var admin = await TestAuth.RegisterAdminAsync(factory, "settings-admin@example.com");
        var golfer = await TestAuth.RegisterAsync(factory, "settings-golfer@example.com");
        await TestAuth.SetAsync(factory, "settings-golfer@example.com", a => a.TrialShotRoundsUsed = 2);

        var initial = await (await admin.GetAsync("/admin/settings", Ct)).ReadAsync<AdminSettingsDto>(HttpStatusCode.OK);
        var updated = await (await admin.PutAsJsonAsync("/admin/settings", new { trialShotRounds = 5 }, Ct))
            .ReadAsync<AdminSettingsDto>(HttpStatusCode.OK);
        var reread = await (await admin.GetAsync("/admin/settings", Ct)).ReadAsync<AdminSettingsDto>(HttpStatusCode.OK);
        var entitlements = JsonDocument.Parse(await golfer.GetStringAsync("/me/entitlements", Ct)).RootElement;

        Assert.Equal(3, initial.TrialShotRounds);
        Assert.Equal(5, updated.TrialShotRounds);
        Assert.Equal(5, reread.TrialShotRounds);
        Assert.Equal(3, entitlements.GetProperty("trialShotRoundsRemaining").GetInt32());

        Assert.Contains("trialShotRounds", (await (await admin.PutAsJsonAsync("/admin/settings", new { trialShotRounds = -1 }, Ct)).ReadValidationErrorsAsync()).Keys);
        Assert.Contains("trialShotRounds", (await (await admin.PutAsJsonAsync("/admin/settings", new { }, Ct)).ReadValidationErrorsAsync()).Keys);

        var zero = await (await admin.PutAsJsonAsync("/admin/settings", new { trialShotRounds = 0 }, Ct)).ReadAsync<AdminSettingsDto>(HttpStatusCode.OK);
        Assert.Equal(0, zero.TrialShotRounds);
        Assert.Equal("0", await factory.WithDbAsync(db => db.AppSettings.Where(s => s.Key == AppSettingKeys.TrialShotRounds).Select(s => s.Value).SingleAsync(Ct)));
    }
}
