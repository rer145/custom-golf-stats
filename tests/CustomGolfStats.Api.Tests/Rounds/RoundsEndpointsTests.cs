using System.Net;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Features.Rounds;
using CustomGolfStats.Api.Tests.Infrastructure;
using CustomGolfStats.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using static CustomGolfStats.Api.Tests.Rounds.RoundRequests;

namespace CustomGolfStats.Api.Tests.Rounds;

public sealed class RoundsEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => factory.Time.GetUtcNow();

    [Fact]
    public async Task Round_json_uses_enum_names()
    {
        var client = await TestAuth.RegisterAsync(factory, "enum-names@example.com");
        var seeded = await SeedCourseAsync("Enum Links");
        var id = Guid.NewGuid();
        await PutAsync(client, id, Quick(seeded.Tee.Id, Now, QuickHole(1, 4)) with { RoundType = RoundType.League });

        var json = await client.GetStringAsync($"/rounds/{id}", Ct);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("Quick", doc.RootElement.GetProperty("entryMode").GetString());
        Assert.Equal("League", doc.RootElement.GetProperty("roundType").GetString());
    }

    [Fact]
    public async Task OpenApi_document_shows_enums_as_strings()
    {
        var json = await factory.CreateClient().GetStringAsync("/openapi/v1.json", Ct);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var entryMode = doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("EntryMode");
        Assert.Equal(["Quick", "ShotByShot"], entryMode.GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Creates_and_reads_a_quick_round()
    {
        var client = await TestAuth.RegisterAsync(factory, "quick@example.com");
        var seeded = await SeedCourseAsync("Quick Links");
        var id = Guid.NewGuid();
        var request = Quick(seeded.Tee.Id, Now.AddMinutes(-5), QuickHole(1, 5), QuickHole(2, 4), QuickHole(3, 3)) with
        {
            Notes = "Windy",
            RoundType = RoundType.League,
            StepCount = 9000,
        };

        var put = await PutAsync(client, id, request);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var created = await ReadAsync<RoundDto>(put);
        Assert.Equal(seeded.Version.Id, created.TeeVersionId);
        Assert.Equal(1, created.Version);
        Assert.Equal(Now, created.ModifiedAt);

        var round = await ReadAsync<RoundDto>(await client.GetAsync($"/rounds/{id}", Ct));
        Assert.Equal(id, round.Id);
        Assert.Equal(seeded.Course.Id, round.CourseId);
        Assert.Equal("Quick Links", round.CourseName);
        Assert.Equal(seeded.Tee.Id, round.TeeId);
        Assert.Equal(PlayedOn, round.PlayedOn);
        Assert.Equal(EntryMode.Quick, round.EntryMode);
        Assert.Equal(RoundType.League, round.RoundType);
        Assert.Equal("Windy", round.Notes);
        Assert.Equal(9000, round.StepCount);
        Assert.Equal([1, 2, 3], round.Holes.Select(h => h.HoleNumber));
        Assert.Equal([5, 4, 3], round.Holes.Select(h => h.Score));
        Assert.Equal([4, 4, 3], round.Holes.Select(h => h.Par));
        Assert.All(round.Holes, h => Assert.False(h.IsDerived));

        var list = await ReadAsync<List<RoundSummaryDto>>(await client.GetAsync("/rounds?year=2026", Ct));
        var summary = Assert.Single(list);
        Assert.Equal(id, summary.Id);
        Assert.Equal("Quick Links", summary.CourseName);
        Assert.Equal(3, summary.Holes);
        Assert.Equal(12, summary.Score);
        Assert.Equal(1, summary.ToPar);
        Assert.Empty(await ReadAsync<List<RoundSummaryDto>>(await client.GetAsync("/rounds?year=2025", Ct)));
    }

    [Fact]
    public async Task Quick_hole_errors_are_keyed_by_hole()
    {
        var client = await TestAuth.RegisterAsync(factory, "badquick@example.com");
        var seeded = await SeedCourseAsync();
        var request = Quick(seeded.Tee.Id, Now,
            QuickHole(1, 4),
            QuickHole(2, 3, putts: 3),
            QuickHole(3, 3) with { FairwayHit = true },
            QuickHole(19, 4),
            QuickHole(5, 4, sequence: 1));

        var response = await PutAsync(client, Guid.NewGuid(), request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadAsync<HttpValidationProblemDetails>(response);
        Assert.Equal([nameof(Domain.Holes.HoleSummaryError.PuttsExceedScore)], problem.Errors["holes[1]"]);
        Assert.Equal([nameof(Domain.Holes.HoleSummaryError.TeeFlagsOnPar3)], problem.Errors["holes[2]"]);
        Assert.True(problem.Errors.ContainsKey("holes[3].holeNumber"));
        Assert.True(problem.Errors.ContainsKey("holes[4].entrySequence"));
        Assert.False(problem.Errors.ContainsKey("holes[0]"));
    }

    [Fact]
    public async Task Missing_header_fields_and_unknown_tee_are_rejected()
    {
        var client = await TestAuth.RegisterAsync(factory, "header@example.com");

        var missing = await PutAsync(client, Guid.NewGuid(), Quick(Guid.Empty, default) with { EntryMode = null, PlayedOn = default });
        var problem = await ReadAsync<HttpValidationProblemDetails>(missing);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.True(problem.Errors.ContainsKey("teeId"));
        Assert.True(problem.Errors.ContainsKey("playedOn"));
        Assert.True(problem.Errors.ContainsKey("entryMode"));
        Assert.True(problem.Errors.ContainsKey("clientUpdatedAt"));

        var unknownTee = await PutAsync(client, Guid.NewGuid(), Quick(Guid.NewGuid(), Now, QuickHole(1, 4)));
        Assert.Equal(HttpStatusCode.BadRequest, unknownTee.StatusCode);
        Assert.True((await ReadAsync<HttpValidationProblemDetails>(unknownTee)).Errors.ContainsKey("teeId"));
    }

    [Fact]
    public async Task Shot_round_summaries_are_derived_and_client_summary_values_ignored()
    {
        var client = await TestAuth.RegisterAsync(factory, "shots@example.com");
        await TestAuth.SetAsync(factory, "shots@example.com", a => a.Tier = Tier.Paid);
        var seeded = await SeedCourseAsync();
        var id = Guid.NewGuid();

        var response = await PutAsync(client, id, ShotByShot(seeded.Tee.Id, Now, ShotHole(1, 1)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var round = await ReadAsync<RoundDto>(await client.GetAsync($"/rounds/{id}", Ct));
        var hole = Assert.Single(round.Holes);
        Assert.True(hole.IsDerived);
        Assert.Equal(4, hole.Score);
        Assert.Equal(2, hole.Putts);
        Assert.True(hole.FairwayHit);
        Assert.True(hole.TeeShotLeft);
        Assert.False(hole.TeeShotRight);
        Assert.False(hole.TeeShotLong);
        Assert.False(hole.TeeShotShort);
        Assert.True(hole.Gir);
        Assert.Equal(0, hole.PenaltyStrokes);
        Assert.Equal(0, hole.GreensideSandShots);
        Assert.Equal(0, hole.FairwaySandShots);
        Assert.Equal(ParFourShots(), hole.Shots);

        var shots = await factory.WithDbAsync(db => db.Shots.Where(s => s.RoundId == id).Include(s => s.HoleResult).ToListAsync(Ct));
        Assert.Equal(4, shots.Count);
        Assert.All(shots, s => Assert.Equal(id, s.HoleResult.RoundId));
    }

    [Fact]
    public async Task Invalid_shots_are_rejected_with_keyed_errors()
    {
        var client = await TestAuth.RegisterAsync(factory, "badshots@example.com");
        var seeded = await SeedCourseAsync();
        var notHoled = ParFourShots()[..3];
        var badEnum = ParFourShots();
        badEnum[1] = badEnum[1] with { Club = (Club)999 };
        var request = ShotByShot(seeded.Tee.Id, Now,
            ShotHole(1, 1),
            ShotHole(2, 2, notHoled),
            ShotHole(3, 3, []),
            ShotHole(4, 4, badEnum));

        var response = await PutAsync(client, Guid.NewGuid(), request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadAsync<HttpValidationProblemDetails>(response);
        Assert.Contains(nameof(Domain.Shots.ShotError.NotHoled), problem.Errors["holes[1].shots"]);
        Assert.Contains(nameof(Domain.Shots.ShotError.Empty), problem.Errors["holes[2].shots"]);
        Assert.True(problem.Errors.ContainsKey("holes[3].shots[1]"));
        Assert.DoesNotContain(problem.Errors.Keys, k => k.StartsWith("holes[0]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stale_push_gets_409_with_the_server_copy()
    {
        var client = await TestAuth.RegisterAsync(factory, "stale@example.com");
        var seeded = await SeedCourseAsync();
        var id = Guid.NewGuid();
        var newer = Now;
        await PutAsync(client, id, Quick(seeded.Tee.Id, newer, QuickHole(1, 5)));

        var response = await PutAsync(client, id, Quick(seeded.Tee.Id, newer.AddSeconds(-1), QuickHole(1, 3)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var server = await ReadAsync<RoundDto>(response);
        Assert.Equal(5, Assert.Single(server.Holes).Score);
        Assert.Equal(newer, server.ClientUpdatedAt);
        var stored = await ReadAsync<RoundDto>(await client.GetAsync($"/rounds/{id}", Ct));
        Assert.Equal(5, Assert.Single(stored.Holes).Score);
        Assert.Equal(1, stored.Version);
    }

    [Fact]
    public async Task Newer_or_equal_push_replaces_header_and_holes()
    {
        var client = await TestAuth.RegisterAsync(factory, "newer@example.com");
        var seeded = await SeedCourseAsync();
        var id = Guid.NewGuid();
        var first = Now;
        await PutAsync(client, id, Quick(seeded.Tee.Id, first, QuickHole(1, 5), QuickHole(2, 5)));
        factory.Time.Advance(TimeSpan.FromMinutes(10));

        var same = await PutAsync(client, id, Quick(seeded.Tee.Id, first, QuickHole(1, 6), QuickHole(2, 6)) with { Notes = "same" });
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        Assert.Equal(2, (await ReadAsync<RoundDto>(same)).Version);

        var response = await PutAsync(client, id, Quick(seeded.Tee.Id, first.AddMinutes(5), QuickHole(2, 3, sequence: 1)) with { Notes = "edited" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var round = await ReadAsync<RoundDto>(response);
        Assert.Equal(3, round.Version);
        Assert.Equal("edited", round.Notes);
        Assert.Equal(Now, round.ModifiedAt);
        var hole = Assert.Single(round.Holes);
        Assert.Equal((2, 1, 3), (hole.HoleNumber, hole.EntrySequence, hole.Score));
        Assert.Equal(1, await factory.WithDbAsync(db => db.HoleResults.CountAsync(h => h.RoundId == id, Ct)));
    }

    [Fact]
    public async Task Tee_version_is_kept_on_update_unless_tee_or_date_changes()
    {
        var client = await TestAuth.RegisterAsync(factory, "versions@example.com");
        var seeded = await SeedCourseAsync(effectiveFrom: new DateOnly(2020, 1, 1));
        var id = Guid.NewGuid();
        var at = Now;
        await PutAsync(client, id, Quick(seeded.Tee.Id, at, QuickHole(1, 4)));
        var newer = await factory.WithDbAsync(async db =>
        {
            var tee = await db.Tees.SingleAsync(t => t.Id == seeded.Tee.Id, Ct);
            return await TestData.AddTeeVersionAsync(db, tee, new DateOnly(2026, 1, 1));
        });

        var unchanged = await ReadAsync<RoundDto>(await PutAsync(client, id, Quick(seeded.Tee.Id, at.AddSeconds(1), QuickHole(1, 4))));
        Assert.Equal(seeded.Version.Id, unchanged.TeeVersionId);

        var redated = await ReadAsync<RoundDto>(await PutAsync(client, id,
            Quick(seeded.Tee.Id, at.AddSeconds(2), QuickHole(1, 4)) with { PlayedOn = new DateOnly(2026, 5, 1) }));
        Assert.Equal(newer.Id, redated.TeeVersionId);
    }

    [Fact]
    public async Task Soft_delete_hides_the_round()
    {
        var client = await TestAuth.RegisterAsync(factory, "delete@example.com");
        var seeded = await SeedCourseAsync();
        var id = Guid.NewGuid();
        await PutAsync(client, id, Quick(seeded.Tee.Id, Now, QuickHole(1, 4)));
        factory.Time.Advance(TimeSpan.FromMinutes(1));

        var delete = await client.DeleteAsync($"/rounds/{id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/rounds/{id}", Ct)).StatusCode);
        Assert.Empty(await ReadAsync<List<RoundSummaryDto>>(await client.GetAsync("/rounds", Ct)));
        var stored = await factory.WithDbAsync(db => db.Rounds.SingleAsync(r => r.Id == id, Ct));
        Assert.Equal(Now, stored.DeletedAt);
        Assert.Equal(Now, stored.ModifiedAt);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/rounds/{id}", Ct)).StatusCode);

        // A deleted round stays deleted: a later push gets the tombstone back.
        var push = await PutAsync(client, id, Quick(seeded.Tee.Id, Now.AddDays(1), QuickHole(1, 4)));
        Assert.Equal(HttpStatusCode.Conflict, push.StatusCode);
        Assert.NotNull((await ReadAsync<RoundDto>(push)).DeletedAt);
    }

    [Fact]
    public async Task Another_golfers_round_is_not_found()
    {
        var owner = await TestAuth.RegisterAsync(factory, "owner@example.com");
        var other = await TestAuth.RegisterAsync(factory, "other@example.com");
        var seeded = await SeedCourseAsync();
        var id = Guid.NewGuid();
        await PutAsync(owner, id, Quick(seeded.Tee.Id, Now, QuickHole(1, 4)));

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/rounds/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(other, id, Quick(seeded.Tee.Id, Now.AddDays(1), QuickHole(1, 9)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/rounds/{id}", Ct)).StatusCode);
        Assert.Empty(await ReadAsync<List<RoundSummaryDto>>(await other.GetAsync("/rounds", Ct)));
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/rounds/{Guid.NewGuid()}", Ct)).StatusCode);

        var stored = await ReadAsync<RoundDto>(await owner.GetAsync($"/rounds/{id}", Ct));
        Assert.Equal(4, Assert.Single(stored.Holes).Score);
        Assert.Null(stored.DeletedAt);
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/rounds", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/rounds/{Guid.NewGuid()}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PutAsync(client, Guid.NewGuid(), Quick(Guid.NewGuid(), Now))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync($"/rounds/{Guid.NewGuid()}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/sync/changes", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/weather?lat=1&lon=1&at=2026-06-01T12:00:00Z", Ct)).StatusCode);
    }

    private Task<SeededCourse> SeedCourseAsync(string name = "Test Course", DateOnly? effectiveFrom = null) =>
        factory.WithDbAsync(db => TestData.SeedCourseAsync(db, name, effectiveFrom: effectiveFrom));
}
