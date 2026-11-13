using CustomGolfStats.App.Core.Storage.Rows;
using ApiClient = CustomGolfStats.Api.Client;
using Dom = CustomGolfStats.Domain;

namespace CustomGolfStats.App.Core.Storage;

/// <summary>Maps between the generated API DTOs and the local records. The API and Domain enums share names, so they convert by name.</summary>
public static class ApiMapper
{
    // ---- rounds ----

    /// <summary>A server round as a clean local record. A deleted round has no holes (a tombstone).</summary>
    public static RoundRecord ToRecord(ApiClient.RoundDto dto)
    {
        var round = new RoundRow
        {
            Id = dto.Id, CourseId = dto.CourseId, CourseName = dto.CourseName ?? "", TeeId = dto.TeeId, TeeName = dto.TeeName ?? "",
            TeeVersionId = dto.TeeVersionId, PlayedOn = dto.PlayedOn.DateTime.Date, StartedAt = dto.StartedAt, FinishedAt = dto.FinishedAt,
            EntryMode = Convert<Dom.EntryMode>(dto.EntryMode), RoundType = dto.RoundType, Notes = dto.Notes,
            WeatherConditions = dto.WeatherConditions, TemperatureF = dto.TemperatureF, WindSpeedMph = dto.WindSpeedMph,
            WindDirectionDegrees = dto.WindDirectionDegrees, LegacyWeather = dto.LegacyWeather, DurationMinutes = dto.DurationMinutes,
            DistanceMiles = dto.DistanceMiles, StepCount = dto.StepCount, ClientUpdatedAt = dto.ClientUpdatedAt, ModifiedAt = dto.ModifiedAt,
            DeletedAt = dto.DeletedAt, Version = dto.Version, TrialCounted = dto.TrialCounted, Dirty = false,
        };

        if (dto.DeletedAt is not null)
            return new RoundRecord(round, []);

        var holes = (dto.Holes ?? []).OrderBy(h => h.HoleNumber).Select(h =>
        {
            var row = new HoleRow
            {
                RoundId = dto.Id, HoleNumber = h.HoleNumber, EntrySequence = h.EntrySequence, Par = h.Par, Score = h.Score, Putts = h.Putts,
                FairwayHit = h.FairwayHit, TeeShotLeft = h.TeeShotLeft, TeeShotRight = h.TeeShotRight, TeeShotLong = h.TeeShotLong,
                TeeShotShort = h.TeeShotShort, Gir = h.Gir, PenaltyStrokes = h.PenaltyStrokes, GreensideSandShots = h.GreensideSandShots,
                FairwaySandShots = h.FairwaySandShots, IsDerived = h.IsDerived,
            };
            var shots = (h.Shots ?? []).OrderBy(s => s.Sequence).Select(s => ToRow(dto.Id, h.HoleNumber, s)).ToList();
            return new HoleRecord(row, shots);
        }).ToList();
        return new RoundRecord(round, holes);
    }

    /// <summary>The body for <c>PUT /rounds/{id}</c>. Holes with no shots are sent as quick-entry holes (Shots null).</summary>
    public static ApiClient.UpsertRoundRequest ToUpsertRequest(RoundRecord record)
    {
        var r = record.Round;
        return new ApiClient.UpsertRoundRequest
        {
            TeeId = r.TeeId, PlayedOn = new DateTimeOffset(DateTime.SpecifyKind(r.PlayedOn.Date, DateTimeKind.Unspecified), TimeSpan.Zero),
            StartedAt = r.StartedAt, FinishedAt = r.FinishedAt, EntryMode = Convert<ApiClient.EntryMode>(r.EntryMode), RoundType = r.RoundType,
            Notes = r.Notes, WeatherConditions = r.WeatherConditions, TemperatureF = r.TemperatureF, WindSpeedMph = r.WindSpeedMph,
            WindDirectionDegrees = r.WindDirectionDegrees, DurationMinutes = r.DurationMinutes, DistanceMiles = r.DistanceMiles,
            StepCount = r.StepCount, ClientUpdatedAt = r.ClientUpdatedAt,
            Holes = record.Holes.OrderBy(h => h.Hole.HoleNumber).Select(h => new ApiClient.UpsertHoleRequest
            {
                HoleNumber = h.Hole.HoleNumber, EntrySequence = h.Hole.EntrySequence, Score = h.Hole.Score, Putts = h.Hole.Putts,
                FairwayHit = h.Hole.FairwayHit, TeeShotLeft = h.Hole.TeeShotLeft, TeeShotRight = h.Hole.TeeShotRight,
                TeeShotLong = h.Hole.TeeShotLong, TeeShotShort = h.Hole.TeeShotShort, Gir = h.Hole.Gir, PenaltyStrokes = h.Hole.PenaltyStrokes,
                GreensideSandShots = h.Hole.GreensideSandShots, FairwaySandShots = h.Hole.FairwaySandShots,
                Shots = h.Shots.Count == 0 ? null : h.Shots.OrderBy(s => s.Sequence).Select(ToDto).ToList(),
            }).ToList(),
        };
    }

    private static ShotRow ToRow(Guid roundId, int holeNumber, ApiClient.ShotDto s) => new()
    {
        RoundId = roundId, HoleNumber = holeNumber, Sequence = s.Sequence, StartLie = Convert<Dom.Lie>(s.StartLie), StartDistance = s.StartDistance,
        Club = Convert<Dom.Club>(s.Club), ShotType = Convert<Dom.ShotType>(s.ShotType), EndLie = Convert<Dom.Lie>(s.EndLie),
        EndDistance = s.EndDistance, Direction = Convert<Dom.Direction>(s.Direction),
        Shape = s.Shape is { } shape ? Convert<Dom.Shape>(shape) : null, PenaltyStrokes = s.PenaltyStrokes,
    };

    private static ApiClient.ShotDto ToDto(ShotRow s) => new()
    {
        Sequence = s.Sequence, StartLie = Convert<ApiClient.Lie>(s.StartLie), StartDistance = s.StartDistance, Club = Convert<ApiClient.Club>(s.Club),
        ShotType = Convert<ApiClient.ShotType>(s.ShotType), EndLie = Convert<ApiClient.Lie>(s.EndLie), EndDistance = s.EndDistance,
        Direction = Convert<ApiClient.Direction>(s.Direction), Shape = s.Shape is { } shape ? Convert<ApiClient.Shape>(shape) : null,
        PenaltyStrokes = s.PenaltyStrokes,
    };

    // ---- courses ----

    /// <summary>A server course (from sync) as a not-pending local record.</summary>
    public static CourseRecord ToRecord(ApiClient.SyncCourseDto dto)
    {
        var course = new CourseRow
        {
            Id = dto.Id, Name = dto.Name ?? "", ClubName = dto.ClubName, Address = dto.Address, City = dto.City, Region = dto.Region,
            Country = dto.Country, Phone = dto.Phone, Website = dto.Website, Latitude = dto.Latitude, Longitude = dto.Longitude,
            Status = dto.Status, Source = dto.Source, MergedIntoCourseId = dto.MergedIntoCourseId, UpdatedAt = dto.UpdatedAt, PendingSync = false,
        };
        var tees = (dto.Tees ?? []).Select(t => new TeeRecord(
            new TeeRow { Id = t.Id, CourseId = dto.Id, Name = t.Name ?? "", Gender = t.Gender },
            (t.Versions ?? []).Select(v => new TeeVersionRecord(
                new TeeVersionRow
                {
                    Id = v.Id, TeeId = t.Id, EffectiveFrom = v.EffectiveFrom.DateTime.Date, Par = v.Par, Rating = v.Rating, Slope = v.Slope,
                    FrontPar = v.FrontPar, FrontRating = v.FrontRating, FrontSlope = v.FrontSlope,
                    BackPar = v.BackPar, BackRating = v.BackRating, BackSlope = v.BackSlope,
                },
                (v.Holes ?? []).OrderBy(h => h.Number)
                    .Select(h => new TeeHoleRow { TeeVersionId = v.Id, Number = h.Number, Par = h.Par, Yardage = h.Yardage, StrokeIndex = h.StrokeIndex })
                    .ToList())).ToList())).ToList();
        return new CourseRecord(course, tees);
    }

    /// <summary>The body for <c>POST /courses</c> of a course created on this device: client ids, each tee's latest version.</summary>
    public static ApiClient.CreateCourseRequest ToCreateRequest(CourseRecord record)
    {
        var c = record.Course;
        return new ApiClient.CreateCourseRequest
        {
            Id = c.Id, Name = c.Name, ClubName = c.ClubName, Address = c.Address, City = c.City, Region = c.Region, Country = c.Country,
            Phone = c.Phone, Website = c.Website, Latitude = c.Latitude, Longitude = c.Longitude,
            Tees = record.Tees.Select(t =>
            {
                var v = t.Versions.OrderByDescending(x => x.Version.EffectiveFrom).FirstOrDefault();
                return new ApiClient.CreateTeeRequest
                {
                    Id = t.Tee.Id, Name = t.Tee.Name, Gender = t.Tee.Gender,
                    EffectiveFrom = v is null ? null : new DateTimeOffset(DateTime.SpecifyKind(v.Version.EffectiveFrom.Date, DateTimeKind.Unspecified), TimeSpan.Zero),
                    Rating = v?.Version.Rating, Slope = v?.Version.Slope, FrontRating = v?.Version.FrontRating, FrontSlope = v?.Version.FrontSlope,
                    BackRating = v?.Version.BackRating, BackSlope = v?.Version.BackSlope,
                    Holes = v?.Holes.OrderBy(h => h.Number)
                        .Select(h => new ApiClient.TeeHoleInput { Number = h.Number, Par = h.Par, Yardage = h.Yardage, StrokeIndex = h.StrokeIndex }).ToList(),
                };
            }).ToList(),
        };
    }

    // ---- account ----

    public static EntitlementsRow ToRow(ApiClient.EntitlementsResponse dto) => new()
    {
        Tier = Enum.Parse<Dom.Tier>(dto.Tier, ignoreCase: true), Role = Enum.Parse<Dom.Role>(dto.Role, ignoreCase: true),
        ShotByShotEntry = dto.Features.ShotByShotEntry, TrialShotRoundsRemaining = dto.TrialShotRoundsRemaining,
    };

    public static HandicapRow ToRow(ApiClient.SyncHandicapEntryDto dto) => new() { Id = dto.Id, EffectiveOn = dto.EffectiveOn.DateTime.Date, Index = dto.Index };

    private static TOut Convert<TOut>(Enum value) where TOut : struct, Enum => Enum.Parse<TOut>(value.ToString());
}
