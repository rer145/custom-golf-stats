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

        var holes = (dto.Holes ?? []).OrderBy(h => h.EntrySequence).Select(h =>
        {
            var row = new HoleRow
            {
                RoundId = dto.Id, HoleNumber = h.HoleNumber, EntrySequence = h.EntrySequence, Par = h.Par, Score = h.Score, Putts = h.Putts,
                FairwayHit = h.FairwayHit, TeeShotLeft = h.TeeShotLeft, TeeShotRight = h.TeeShotRight, TeeShotLong = h.TeeShotLong,
                TeeShotShort = h.TeeShotShort, Gir = h.Gir, PenaltyStrokes = h.PenaltyStrokes, GreensideSandShots = h.GreensideSandShots,
                FairwaySandShots = h.FairwaySandShots, IsDerived = h.IsDerived,
            };
            var shots = (h.Shots ?? []).OrderBy(s => s.Sequence).Select(s => ToRow(dto.Id, h.EntrySequence, s)).ToList();
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
            Holes = record.Holes.OrderBy(h => h.Hole.EntrySequence).Select(h => new ApiClient.UpsertHoleRequest
            {
                HoleNumber = h.Hole.HoleNumber, EntrySequence = h.Hole.EntrySequence, Score = h.Hole.Score, Putts = h.Hole.Putts,
                FairwayHit = h.Hole.FairwayHit, TeeShotLeft = h.Hole.TeeShotLeft, TeeShotRight = h.Hole.TeeShotRight,
                TeeShotLong = h.Hole.TeeShotLong, TeeShotShort = h.Hole.TeeShotShort, Gir = h.Hole.Gir, PenaltyStrokes = h.Hole.PenaltyStrokes,
                GreensideSandShots = h.Hole.GreensideSandShots, FairwaySandShots = h.Hole.FairwaySandShots,
                Shots = h.Shots.Count == 0 ? null : h.Shots.OrderBy(s => s.Sequence).Select(ToDto).ToList(),
            }).ToList(),
        };
    }

    private static ShotRow ToRow(Guid roundId, int entrySequence, ApiClient.ShotDto s) => new()
    {
        RoundId = roundId, EntrySequence = entrySequence, Sequence = s.Sequence, StartLie = Convert<Dom.Lie>(s.StartLie), StartDistance = s.StartDistance,
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

    /// <summary>A server course (from sync) as a not-pending local record. <c>UpdatedAt</c> is the server's.</summary>
    public static CourseRecord ToRecord(ApiClient.SyncCourseDto dto)
    {
        var tees = (dto.Tees ?? []).Select(t => ToTee(dto.Id, t.Id, t.Name, t.Gender,
            (t.Versions ?? []).Select(v => ToVersion(t.Id, v.Id, v.EffectiveFrom, v.Par, v.Rating, v.Slope, v.FrontPar, v.FrontRating, v.FrontSlope,
                v.BackPar, v.BackRating, v.BackSlope, v.Holes)))).ToList();
        var course = ToCourse(dto.Id, dto.Name, dto.ClubName, dto.Address, dto.City, dto.Region, dto.Country, dto.Phone, dto.Website, dto.Latitude,
            dto.Longitude, dto.Status, dto.Source, dto.MergedIntoCourseId, dto.UpdatedAt);
        return new CourseRecord(course, tees);
    }

    /// <summary>A course returned by import/create/read as a not-pending local record. The detail has no modified time, so
    /// <paramref name="receivedAt"/> (the caller's clock) stands in for <c>UpdatedAt</c>.</summary>
    public static CourseRecord ToRecord(ApiClient.CourseDetailDto dto, DateTimeOffset receivedAt)
    {
        var tees = (dto.Tees ?? []).Select(t => ToTee(dto.Id, t.Id, t.Name, t.Gender,
            (t.Versions ?? []).Select(v => ToVersion(t.Id, v.Id, v.EffectiveFrom, v.Par, v.Rating, v.Slope, v.FrontPar, v.FrontRating, v.FrontSlope,
                v.BackPar, v.BackRating, v.BackSlope, v.Holes)))).ToList();
        var course = ToCourse(dto.Id, dto.Name, dto.ClubName, dto.Address, dto.City, dto.Region, dto.Country, dto.Phone, dto.Website, dto.Latitude,
            dto.Longitude, dto.Status, dto.Source, dto.MergedIntoCourseId, receivedAt);
        return new CourseRecord(course, tees);
    }

    private static CourseRow ToCourse(Guid id, string? name, string? clubName, string? address, string? city, string? region, string? country,
        string? phone, string? website, double? latitude, double? longitude, ApiClient.CourseStatus status, ApiClient.CourseSource source,
        Guid? mergedInto, DateTimeOffset updatedAt) => new()
    {
        Id = id, Name = name ?? "", ClubName = clubName, Address = address, City = city, Region = region, Country = country, Phone = phone,
        Website = website, Latitude = latitude, Longitude = longitude, Status = status, Source = source, MergedIntoCourseId = mergedInto,
        UpdatedAt = updatedAt, PendingSync = false,
    };

    private static TeeRecord ToTee(Guid courseId, Guid id, string? name, ApiClient.Gender gender, IEnumerable<TeeVersionRecord> versions) =>
        new(new TeeRow { Id = id, CourseId = courseId, Name = name ?? "", Gender = gender }, versions.ToList());

    private static TeeVersionRecord ToVersion(Guid teeId, Guid id, DateTimeOffset effectiveFrom, int par, double rating, int slope, int? frontPar,
        double? frontRating, int? frontSlope, int? backPar, double? backRating, int? backSlope, ICollection<ApiClient.TeeHoleDto>? holes) =>
        new(new TeeVersionRow
            {
                Id = id, TeeId = teeId, EffectiveFrom = effectiveFrom.DateTime.Date, Par = par, Rating = rating, Slope = slope,
                FrontPar = frontPar, FrontRating = frontRating, FrontSlope = frontSlope, BackPar = backPar, BackRating = backRating, BackSlope = backSlope,
            },
            (holes ?? []).OrderBy(h => h.Number)
                .Select(h => new TeeHoleRow { TeeVersionId = id, Number = h.Number, Par = h.Par, Yardage = h.Yardage, StrokeIndex = h.StrokeIndex })
                .ToList());

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
