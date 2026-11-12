using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Tests.Infrastructure;
using CustomGolfStats.Domain;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Tests.Me;

public sealed class MeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;

    [Fact]
    public async Task Me_endpoints_require_authentication()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/me/handicap", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/me/entitlements", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync("/me", new { }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/me/handicap", new { }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync($"/me/handicap/{Guid.NewGuid()}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Get_me_returns_defaults_for_a_new_account()
    {
        var client = await TestAuth.RegisterAsync(factory, "defaults@example.com");

        var response = await client.GetAsync("/me", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal("defaults@example.com", body.GetProperty("email").GetString());
        Assert.Equal("Unspecified", body.GetProperty("gender").GetString());
        Assert.Equal("Yards", body.GetProperty("units").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("homeCourseId").ValueKind);
    }

    [Fact]
    public async Task Put_me_persists_the_profile()
    {
        var client = await TestAuth.RegisterAsync(factory, "profile@example.com");
        var courseId = await SeedCourseIdAsync();

        var put = await client.PutAsJsonAsync("/me", new
        {
            firstName = "Ada",
            lastName = "Lovelace",
            gender = "Female",
            ghinNumber = "1234567",
            homeCourseId = courseId,
            units = "Meters",
        }, Ct);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var body = await ReadAsync(await client.GetAsync("/me", Ct));
        Assert.Equal("Ada", body.GetProperty("firstName").GetString());
        Assert.Equal("Lovelace", body.GetProperty("lastName").GetString());
        Assert.Equal("Female", body.GetProperty("gender").GetString());
        Assert.Equal("1234567", body.GetProperty("ghinNumber").GetString());
        Assert.Equal(courseId, body.GetProperty("homeCourseId").GetGuid());
        Assert.Equal("Meters", body.GetProperty("units").GetString());

        var stored = await factory.WithDbAsync(db => db.Users.SingleAsync(a => a.NormalizedEmail == "PROFILE@EXAMPLE.COM"));
        Assert.Equal(DistanceUnits.Meters, stored.Units);
        Assert.Equal(courseId, stored.HomeCourseId);
    }

    [Fact]
    public async Task Put_me_can_clear_the_home_course_and_ghin()
    {
        var client = await TestAuth.RegisterAsync(factory, "clear@example.com");
        var courseId = await SeedCourseIdAsync();
        await client.PutAsJsonAsync("/me", Profile(homeCourseId: courseId, ghin: "99"), Ct);

        var put = await client.PutAsJsonAsync("/me", Profile(), Ct);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var body = await ReadAsync(await client.GetAsync("/me", Ct));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("homeCourseId").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("ghinNumber").ValueKind);
    }

    [Fact]
    public async Task Put_me_rejects_an_unknown_home_course()
    {
        var client = await TestAuth.RegisterAsync(factory, "badcourse@example.com");

        var put = await client.PutAsJsonAsync("/me", Profile(homeCourseId: Guid.NewGuid()), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.True((await ReadAsync(put)).GetProperty("errors").TryGetProperty("homeCourseId", out _));
    }

    [Theory]
    [InlineData("Nonsense", "Yards", "gender")]
    [InlineData("Male", "Furlongs", "units")]
    public async Task Put_me_rejects_unknown_enum_values(string gender, string units, string field)
    {
        var client = await TestAuth.RegisterAsync(factory, $"enum-{field}@example.com");

        var put = await client.PutAsJsonAsync("/me", Profile(gender: gender, units: units), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.True((await ReadAsync(put)).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Handicap_entries_can_be_added_listed_and_deleted()
    {
        var client = await TestAuth.RegisterAsync(factory, "hcp@example.com");

        var first = await client.PostAsJsonAsync("/me/handicap", new { effectiveOn = "2026-03-01", index = 12.4 }, Ct);
        var second = await client.PostAsJsonAsync("/me/handicap", new { effectiveOn = "2026-05-01", index = 10.9 }, Ct);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var created = await ReadAsync(first);
        Assert.Equal(12.4m, created.GetProperty("index").GetDecimal());
        Assert.Equal("2026-03-01", created.GetProperty("effectiveOn").GetString());

        var list = await ReadAsync(await client.GetAsync("/me/handicap", Ct));
        Assert.Equal(2, list.GetArrayLength());
        Assert.Equal("2026-05-01", list[0].GetProperty("effectiveOn").GetString());

        var delete = await client.DeleteAsync($"/me/handicap/{created.GetProperty("id").GetGuid()}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(1, (await ReadAsync(await client.GetAsync("/me/handicap", Ct))).GetArrayLength());
    }

    [Fact]
    public async Task Handicap_allows_two_entries_on_the_same_date()
    {
        var client = await TestAuth.RegisterAsync(factory, "samedate@example.com");

        var a = await client.PostAsJsonAsync("/me/handicap", new { effectiveOn = "2026-03-01", index = 12.0 }, Ct);
        var b = await client.PostAsJsonAsync("/me/handicap", new { effectiveOn = "2026-03-01", index = 11.0 }, Ct);

        Assert.Equal(HttpStatusCode.Created, a.StatusCode);
        Assert.Equal(HttpStatusCode.Created, b.StatusCode);
        Assert.Equal(2, (await ReadAsync(await client.GetAsync("/me/handicap", Ct))).GetArrayLength());
    }

    [Theory]
    [InlineData(-10.0, true)]
    [InlineData(54.0, true)]
    [InlineData(0.0, true)]
    [InlineData(-10.1, false)]
    [InlineData(54.1, false)]
    [InlineData(12.34, false)]
    public async Task Handicap_index_must_be_in_range_with_one_decimal(double index, bool valid)
    {
        var client = await TestAuth.RegisterAsync(factory, $"range{index}@example.com");

        var response = await client.PostAsJsonAsync("/me/handicap", new { effectiveOn = "2026-03-01", index }, Ct);

        if (valid)
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.True((await ReadAsync(response)).GetProperty("errors").TryGetProperty("index", out _));
        }
    }

    [Fact]
    public async Task Another_golfers_handicap_entry_is_invisible_and_cannot_be_deleted()
    {
        var owner = await TestAuth.RegisterAsync(factory, "owner@example.com");
        var other = await TestAuth.RegisterAsync(factory, "intruder@example.com");
        var created = await ReadAsync(await owner.PostAsJsonAsync("/me/handicap", new { effectiveOn = "2026-03-01", index = 5.0 }, Ct));
        var id = created.GetProperty("id").GetGuid();

        Assert.Equal(0, (await ReadAsync(await other.GetAsync("/me/handicap", Ct))).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/me/handicap/{id}", Ct)).StatusCode);
        Assert.Equal(1, (await ReadAsync(await owner.GetAsync("/me/handicap", Ct))).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"/me/handicap/{Guid.NewGuid()}", Ct)).StatusCode);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(3, 0)]
    [InlineData(5, 0)]
    public async Task Free_entitlements_count_down_the_trial(int used, int expectedRemaining)
    {
        var email = $"free{used}@example.com";
        var client = await TestAuth.RegisterAsync(factory, email);
        await TestAuth.SetAsync(factory, email, a => a.TrialShotRoundsUsed = used);

        var response = await client.GetAsync("/me/entitlements", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal("Free", body.GetProperty("tier").GetString());
        Assert.Equal("User", body.GetProperty("role").GetString());
        Assert.Equal(expectedRemaining > 0, body.GetProperty("features").GetProperty("shotByShotEntry").GetBoolean());
        Assert.Equal(expectedRemaining, body.GetProperty("trialShotRoundsRemaining").GetInt32());
    }

    [Fact]
    public async Task Paid_entitlements_are_unlimited()
    {
        var client = await TestAuth.RegisterAsync(factory, "paid@example.com");
        await TestAuth.SetAsync(factory, "paid@example.com", a =>
        {
            a.Tier = Tier.Paid;
            a.TrialShotRoundsUsed = 9;
        });

        var body = await ReadAsync(await client.GetAsync("/me/entitlements", Ct));

        Assert.Equal("Paid", body.GetProperty("tier").GetString());
        Assert.True(body.GetProperty("features").GetProperty("shotByShotEntry").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("trialShotRoundsRemaining").ValueKind);
    }

    [Fact]
    public async Task Entitlements_reflect_tier_changes_without_a_new_token()
    {
        var client = await TestAuth.RegisterAsync(factory, "upgrade@example.com");
        Assert.Equal("Free", (await ReadAsync(await client.GetAsync("/me/entitlements", Ct))).GetProperty("tier").GetString());

        await TestAuth.SetAsync(factory, "upgrade@example.com", a => a.Tier = Tier.Paid);

        Assert.Equal("Paid", (await ReadAsync(await client.GetAsync("/me/entitlements", Ct))).GetProperty("tier").GetString());
    }

    [Fact]
    public async Task Entitlements_honour_the_trial_setting()
    {
        // The database is shared by the class, so the setting is restored afterwards.
        var client = await TestAuth.RegisterAsync(factory, "setting@example.com");
        await TestAuth.SetAsync(factory, "setting@example.com", a => a.TrialShotRoundsUsed = 1);
        await SetTrialSettingAsync("5");

        try
        {
            var body = await ReadAsync(await client.GetAsync("/me/entitlements", Ct));
            Assert.Equal(4, body.GetProperty("trialShotRoundsRemaining").GetInt32());
        }
        finally
        {
            await SetTrialSettingAsync("3");
        }
    }

    private Task SetTrialSettingAsync(string value) => factory.WithDbAsync(async db =>
    {
        var setting = await db.AppSettings.SingleAsync(s => s.Key == AppSettingKeys.TrialShotRounds);
        setting.Value = value;
        await db.SaveChangesAsync();
    });

    private Task<Guid> SeedCourseIdAsync() => factory.WithDbAsync(async db => (await TestData.SeedCourseAsync(db)).Course.Id);

    private static object Profile(Guid? homeCourseId = null, string? ghin = null, string gender = "Unspecified", string units = "Yards") =>
        new { firstName = "A", lastName = "B", gender, ghinNumber = ghin, homeCourseId, units };
}
