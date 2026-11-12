using CustomGolfStats.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Features.Rounds;

/// <summary>Loads rounds with everything a <see cref="RoundDto"/> needs, and maps entities to DTOs by hand.</summary>
internal static class RoundMapping
{
    /// <summary>Tee version (with holes, tee and course) and hole results with shots.</summary>
    public static IQueryable<Round> WithDetails(this IQueryable<Round> rounds) =>
        rounds
            .Include(r => r.TeeVersion).ThenInclude(v => v.Holes)
            .Include(r => r.TeeVersion).ThenInclude(v => v.Tee).ThenInclude(t => t.Course)
            .Include(r => r.HoleResults).ThenInclude(h => h.Shots)
            .AsSplitQuery();

    public static RoundDto ToDto(this Round round)
    {
        var pars = round.TeeVersion.Holes.ToDictionary(h => h.Number, h => h.Par);
        var tee = round.TeeVersion.Tee;
        return new RoundDto(
            round.Id,
            tee.CourseId,
            tee.Course.Name,
            tee.Id,
            tee.Name,
            round.TeeVersionId,
            round.PlayedOn,
            round.StartedAt,
            round.FinishedAt,
            round.EntryMode,
            round.RoundType,
            round.Notes,
            round.WeatherConditions,
            round.TemperatureF,
            round.WindSpeedMph,
            round.WindDirectionDegrees,
            round.LegacyWeather,
            round.DurationMinutes,
            round.DistanceMiles,
            round.StepCount,
            round.ClientUpdatedAt,
            round.ModifiedAt,
            round.DeletedAt,
            round.Version,
            round.TrialCounted,
            [.. round.HoleResults.OrderBy(h => h.EntrySequence).Select(h => h.ToDto(pars.GetValueOrDefault(h.HoleNumber)))]);
    }

    public static ShotDto ToDto(this ShotEntity shot) =>
        new(shot.Sequence, shot.StartLie, shot.StartDistance, shot.Club, shot.ShotType, shot.EndLie, shot.EndDistance,
            shot.Direction, shot.Shape, shot.PenaltyStrokes);

    public static SyncCourseDto ToSyncDto(this Course course) =>
        new(course.Id, course.Name, course.ClubName, course.Address, course.City, course.Region, course.Country, course.Phone,
            course.Website, course.Latitude, course.Longitude, course.Status, course.Source, course.MergedIntoCourseId, course.UpdatedAt,
            [.. course.Tees.OrderBy(t => t.Name, StringComparer.Ordinal).ThenBy(t => t.Id).Select(t => new SyncTeeDto(
                t.Id,
                t.Name,
                t.Gender,
                [.. t.Versions.OrderBy(v => v.EffectiveFrom).Select(v => new SyncTeeVersionDto(
                    v.Id, v.EffectiveFrom, v.Par, v.Rating, v.Slope, v.FrontPar, v.FrontRating, v.FrontSlope,
                    v.BackPar, v.BackRating, v.BackSlope,
                    [.. v.Holes.OrderBy(h => h.Number).Select(h => new SyncTeeHoleDto(h.Number, h.Par, h.Yardage, h.StrokeIndex))]))]))]);

    private static HoleResultDto ToDto(this HoleResult hole, int par) =>
        new(hole.HoleNumber, hole.EntrySequence, par, hole.Score, hole.Putts, hole.FairwayHit, hole.TeeShotLeft, hole.TeeShotRight,
            hole.TeeShotLong, hole.TeeShotShort, hole.Gir, hole.PenaltyStrokes, hole.GreensideSandShots, hole.FairwaySandShots,
            hole.IsDerived, [.. hole.Shots.OrderBy(s => s.Sequence).Select(s => s.ToDto())]);
}
