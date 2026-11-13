using CustomGolfStats.Api.Client;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.App.Core.Tests.Support;
using Domain = CustomGolfStats.Domain;

namespace CustomGolfStats.App.Core.Tests;

public class ApiMappingTests
{
    private static RoundDto ServerRound(Guid id) => new()
    {
        Id = id, CourseId = Samples.CourseId, CourseName = "Pine Valley", TeeId = Samples.TeeId, TeeName = "Blue", TeeVersionId = Samples.TeeVersionId,
        PlayedOn = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        StartedAt = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), FinishedAt = new DateTimeOffset(2026, 6, 1, 13, 0, 0, TimeSpan.Zero),
        EntryMode = EntryMode.ShotByShot, RoundType = RoundType.League, Notes = "n", WeatherConditions = "Cloudy", TemperatureF = 60, WindSpeedMph = 5,
        WindDirectionDegrees = 90, LegacyWeather = "old", DurationMinutes = 240, DistanceMiles = 5.5, StepCount = 12000,
        ClientUpdatedAt = new DateTimeOffset(2026, 6, 1, 13, 5, 0, TimeSpan.Zero), ModifiedAt = new DateTimeOffset(2026, 6, 1, 13, 6, 0, TimeSpan.Zero),
        Version = 3, TrialCounted = true,
        Holes =
        [
            new HoleResultDto
            {
                HoleNumber = 1, EntrySequence = 1, Par = 4, Score = 4, Putts = 2, FairwayHit = true, Gir = true, IsDerived = true,
                Shots =
                [
                    new ShotDto { Sequence = 1, StartLie = Lie.Tee, StartDistance = 380, Club = Club.Driver, ShotType = ShotType.Tee, EndLie = Lie.Fairway, EndDistance = 150, Direction = Direction.OnTarget, Shape = Shape.Draw, PenaltyStrokes = 0 },
                    new ShotDto { Sequence = 2, StartLie = Lie.Fairway, StartDistance = 150, Club = Club.Iron7, ShotType = ShotType.Approach, EndLie = Lie.Green, EndDistance = 18, Direction = Direction.Short, Shape = null, PenaltyStrokes = 0 },
                    new ShotDto { Sequence = 3, StartLie = Lie.Green, StartDistance = 18, Club = Club.Putter, ShotType = ShotType.Putt, EndLie = Lie.Green, EndDistance = 2, Direction = Direction.OnTarget, PenaltyStrokes = 0 },
                    new ShotDto { Sequence = 4, StartLie = Lie.Green, StartDistance = 2, Club = Club.Putter, ShotType = ShotType.Putt, EndLie = Lie.Holed, EndDistance = null, Direction = Direction.OnTarget, PenaltyStrokes = 0 },
                ],
            },
            new HoleResultDto { HoleNumber = 2, EntrySequence = 2, Par = 5, Score = 6, Putts = 2, Gir = false, PenaltyStrokes = 1, GreensideSandShots = 1 },
        ],
    };

    [Fact]
    public void Server_round_maps_to_a_clean_record_with_every_field()
    {
        var id = Guid.NewGuid();

        var record = ApiMapper.ToRecord(ServerRound(id));

        var r = record.Round;
        Assert.Equal(id, r.Id);
        Assert.False(r.Dirty);
        Assert.Equal(new DateTime(2026, 6, 1), r.PlayedOn);
        Assert.Equal(Domain.EntryMode.ShotByShot, r.EntryMode);
        Assert.Equal(RoundType.League, r.RoundType);
        Assert.Equal("old", r.LegacyWeather);
        Assert.Equal(3, r.Version);
        Assert.True(r.TrialCounted);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 13, 6, 0, TimeSpan.Zero), r.ModifiedAt);
        Assert.Equal(2, record.Holes.Count);
        Assert.Equal(4, record.Holes[0].Shots.Count);
        Assert.True(record.Holes[0].Hole.IsDerived);
        Assert.Null(record.Holes[0].Shots[3].EndDistance);
        Assert.Null(record.Holes[0].Shots[1].Shape);
        Assert.Equal(Domain.Shape.Draw, record.Holes[0].Shots[0].Shape);
        Assert.Equal(Domain.Lie.Holed, record.Holes[0].Shots[3].EndLie);
        Assert.Equal(1, record.Holes[1].Hole.GreensideSandShots);
    }

    [Fact]
    public void Server_deleted_round_maps_to_a_tombstone_record()
    {
        var dto = ServerRound(Guid.NewGuid());
        dto.DeletedAt = new DateTimeOffset(2026, 6, 4, 0, 0, 0, TimeSpan.Zero);

        var record = ApiMapper.ToRecord(dto);

        Assert.Equal(dto.DeletedAt, record.Round.DeletedAt);
        Assert.Empty(record.Holes);
    }

    [Fact]
    public void Local_round_maps_to_an_upsert_request_for_the_API()
    {
        var local = Samples.FullShotRound();
        local.Round.ClientUpdatedAt = new DateTimeOffset(2026, 6, 1, 14, 0, 0, TimeSpan.Zero);
        local.Round.DurationMinutes = 200;

        var req = ApiMapper.ToUpsertRequest(local);

        Assert.Equal(Samples.TeeId, req.TeeId);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), req.PlayedOn);
        Assert.Equal(EntryMode.ShotByShot, req.EntryMode);
        Assert.Equal(RoundType.Casual, req.RoundType);
        Assert.Equal(local.Round.ClientUpdatedAt, req.ClientUpdatedAt);
        Assert.Equal(200, req.DurationMinutes);
        Assert.Equal(72.5, req.TemperatureF);
        var holes = req.Holes!.ToList();
        Assert.Equal([1, 2], holes.Select(h => h.HoleNumber));
        Assert.Equal(4, holes[0].Shots!.Count);
        Assert.Equal(Lie.Holed, holes[0].Shots!.Last().EndLie);
        Assert.Null(holes[0].Shots!.Last().EndDistance);
    }

    [Fact]
    public void Upsert_request_round_trips_back_to_equal_shots()
    {
        var local = Samples.FullShotRound();

        var req = ApiMapper.ToUpsertRequest(local);
        var dto = new RoundDto { Id = local.Round.Id, TeeId = req.TeeId, Holes = [.. req.Holes!.Select(h => new HoleResultDto { HoleNumber = h.HoleNumber, Shots = h.Shots! })] };
        var back = ApiMapper.ToRecord(dto);

        Assert.Equal(local.Holes[0].Shots.Select(s => s.ToShot()), back.Holes[0].Shots.Select(s => s.ToShot()));
        Assert.Equal(local.Holes[1].Shots.Select(s => s.ToShot()), back.Holes[1].Shots.Select(s => s.ToShot()));
    }

    [Fact]
    public void Sync_course_maps_with_tees_versions_and_holes()
    {
        var dto = new SyncCourseDto
        {
            Id = Samples.CourseId, Name = "Pine Valley", City = "Clementon", Latitude = 39.7, Longitude = -74.9,
            Status = CourseStatus.UserEdited, Source = CourseSource.Manual, UpdatedAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
            Tees =
            [
                new SyncTeeDto
                {
                    Id = Samples.TeeId, Name = "White", Gender = Gender.Female,
                    Versions = [new SyncTeeVersionDto { Id = Samples.TeeVersionId, EffectiveFrom = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), Par = 72, Rating = 70.1, Slope = 120, BackPar = 36, Holes = [new TeeHoleDto { Number = 1, Par = 4, Yardage = 350, StrokeIndex = 5 }] }],
                },
            ],
        };

        var record = ApiMapper.ToRecord(dto);

        Assert.Equal(CourseSource.Manual, record.Course.Source);
        Assert.False(record.Course.PendingSync);
        var tee = Assert.Single(record.Tees);
        Assert.Equal(Samples.CourseId, tee.Tee.CourseId);
        Assert.Equal(Gender.Female, tee.Tee.Gender);
        var v = Assert.Single(tee.Versions);
        Assert.Equal(Samples.TeeId, v.Version.TeeId);
        Assert.Equal(36, v.Version.BackPar);
        Assert.Equal(Samples.TeeVersionId, v.Holes[0].TeeVersionId);
        Assert.Equal(350, v.Holes[0].Yardage);
    }

    [Fact]
    public void Local_course_maps_to_a_create_request_using_client_generated_ids()
    {
        var course = Samples.Course("My Track");

        var req = ApiMapper.ToCreateRequest(course);

        Assert.Equal(Samples.CourseId, req.Id);
        Assert.Equal("My Track", req.Name);
        var tee = Assert.Single(req.Tees!);
        Assert.Equal(Samples.TeeId, tee.Id);
        Assert.Equal(130, tee.Slope);
        Assert.Equal(71.2, tee.Rating);
        Assert.Equal(18, tee.Holes!.Count);
        Assert.Equal(1, tee.Holes.First().Number);
    }

    [Fact]
    public void Entitlements_and_handicap_map_from_the_API()
    {
        var e = ApiMapper.ToRow(new EntitlementsResponse { Tier = "Free", Role = "Admin", Features = new EntitlementFeatures { ShotByShotEntry = true }, TrialShotRoundsRemaining = 3 });
        Assert.Equal(Domain.Tier.Free, e.Tier);
        Assert.Equal(Domain.Role.Admin, e.Role);
        Assert.True(e.ShotByShotEntry);
        Assert.Equal(3, e.TrialShotRoundsRemaining);

        var paid = ApiMapper.ToRow(new EntitlementsResponse { Tier = "paid", Role = "user", Features = new EntitlementFeatures { ShotByShotEntry = true }, TrialShotRoundsRemaining = null });
        Assert.Equal(Domain.Tier.Paid, paid.Tier);
        Assert.Null(paid.TrialShotRoundsRemaining);

        var id = Guid.NewGuid();
        var h = ApiMapper.ToRow(new SyncHandicapEntryDto { Id = id, EffectiveOn = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), Index = 12.3 });
        Assert.Equal(id, h.Id);
        Assert.Equal(new DateTime(2026, 3, 1), h.EffectiveOn);
        Assert.Equal(12.3, h.Index);
    }

    [Fact]
    public void Hole_row_and_summary_convert_both_ways()
    {
        var summary = new Domain.Holes.HoleSummary(5, 5, 4, 5, 2, true, false, true, false, false, true, 1, 0, 1);

        var row = HoleRow.FromSummary(Guid.NewGuid(), summary, isDerived: false);

        Assert.Equal(summary, row.ToSummary());
        Assert.False(row.IsDerived);
    }
}
