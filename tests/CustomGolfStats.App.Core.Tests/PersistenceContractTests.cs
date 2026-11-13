using System.Text.Json;
using CustomGolfStats.Api.Client;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Tests.Support;
using Dom = CustomGolfStats.Domain;

namespace CustomGolfStats.App.Core.Tests;

/// <summary>Enums are persisted as integers, so their numeric values are a storage contract.</summary>
public class EnumPinningTests
{
    private static void Pin<T>(params string[] namesInOrder) where T : struct, Enum
    {
        var actual = Enum.GetValues<T>().Select(v => $"{v}={Convert.ToInt32(v)}").ToArray();
        var expected = namesInOrder.Select((n, i) => $"{n}={i}").ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Domain_enums_keep_their_persisted_values()
    {
        Pin<Dom.Lie>("Tee", "Fairway", "Rough", "FairwaySand", "GreensideSand", "Recovery", "Green", "Holed", "PenaltyArea", "OutOfBounds");
        Pin<Dom.Club>("Driver", "Wood3", "Wood4", "Wood5", "Wood7", "Hybrid2", "Hybrid3", "Hybrid4", "Hybrid5", "Hybrid6",
            "Iron2", "Iron3", "Iron4", "Iron5", "Iron6", "Iron7", "Iron8", "Iron9", "PitchingWedge", "GapWedge", "SandWedge", "LobWedge", "Putter");
        Pin<Dom.ShotType>("Tee", "Approach", "Layup", "Chip", "Pitch", "Bunker", "Recovery", "Putt");
        Pin<Dom.Direction>("OnTarget", "Left", "Right", "Short", "Long", "ShortLeft", "ShortRight", "LongLeft", "LongRight");
        Pin<Dom.Shape>("Straight", "Draw", "Fade", "Hook", "Slice", "Push", "Pull", "Topped", "Fat");
        Pin<Dom.EntryMode>("Quick", "ShotByShot");
        Pin<Dom.Role>("User", "Admin");
        Pin<Dom.Tier>("Free", "Paid");
    }

    [Fact]
    public void Generated_client_enums_keep_their_persisted_values()
    {
        Pin<RoundType>("Practice", "Casual", "Tournament", "League");
        Pin<Gender>("Male", "Female", "Unspecified");
        Pin<CourseStatus>("Imported", "UserEdited", "Verified");
        Pin<CourseSource>("GolfCourseApi", "Manual", "Golftrack");
    }
}

/// <summary>The API sends dates as plain "yyyy-MM-dd"; the generated converter parses them in the machine's local zone,
/// so the stored date must come from the wall date, never from a UTC conversion.</summary>
public class DateContractTests
{
    private const string RoundJson = """
        {"id":"44444444-4444-4444-4444-444444444444","courseId":"11111111-1111-1111-1111-111111111111","courseName":"Pine Valley",
         "teeId":"22222222-2222-2222-2222-222222222222","teeName":"Blue","teeVersionId":"33333333-3333-3333-3333-333333333333",
         "playedOn":"2026-06-01","startedAt":"2026-06-01T09:00:00Z","entryMode":"Quick","roundType":"Casual",
         "clientUpdatedAt":"2026-06-01T13:05:00Z","modifiedAt":"2026-06-01T13:06:00Z","version":2,"trialCounted":false,"holes":[]}
        """;

    [Fact]
    public void Plain_date_from_the_API_JSON_becomes_the_same_calendar_day_locally()
    {
        var dto = JsonSerializer.Deserialize<RoundDto>(RoundJson)!;

        var record = ApiMapper.ToRecord(dto);

        Assert.Equal(new DateTime(2026, 6, 1), record.Round.PlayedOn);
        Assert.Equal(Dom.EntryMode.Quick, record.Round.EntryMode);
    }

    [Theory]
    [InlineData(-5, 0)]   // west of UTC: local midnight is the same UTC day
    [InlineData(10, 0)]   // east of UTC: local midnight is the previous UTC day
    [InlineData(-5, 23)]  // late evening west of UTC is already the next UTC day
    public void Date_is_the_wall_date_whatever_the_offset(int offsetHours, int hour)
    {
        var dto = JsonSerializer.Deserialize<RoundDto>(RoundJson)!;
        dto.PlayedOn = new DateTimeOffset(2026, 6, 1, hour, 0, 0, TimeSpan.FromHours(offsetHours));

        Assert.Equal(new DateTime(2026, 6, 1), ApiMapper.ToRecord(dto).Round.PlayedOn);
    }

    [Fact]
    public void Upsert_request_serializes_the_date_as_a_plain_day()
    {
        var round = Samples.FullShotRound();
        round.Round.PlayedOn = new DateTime(2026, 6, 1, 23, 59, 0); // time of day must not leak or shift the day

        var json = JsonSerializer.Serialize(ApiMapper.ToUpsertRequest(round));

        Assert.Contains("\"playedOn\":\"2026-06-01\"", json);
    }

    [Fact]
    public void Tee_version_effective_dates_use_the_wall_date_too()
    {
        var dto = new SyncCourseDto
        {
            Id = Samples.CourseId, Name = "X",
            Tees = [new SyncTeeDto { Id = Samples.TeeId, Name = "Blue", Versions = [new SyncTeeVersionDto { Id = Samples.TeeVersionId, EffectiveFrom = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.FromHours(10)) }] }],
        };

        Assert.Equal(new DateTime(2020, 1, 1), ApiMapper.ToRecord(dto).Tees[0].Versions[0].Version.EffectiveFrom);
    }
}
