using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Infrastructure;
using CustomGolfStats.Domain;
using CustomGolfStats.Domain.Holes;
using CustomGolfStats.Domain.Shots;

namespace CustomGolfStats.Api.Features.Rounds;

/// <summary>
/// Checks a pushed round and turns its holes into entities. Quick holes go through <see cref="HoleSummaryValidator"/>;
/// shot-by-shot holes through <see cref="ShotValidator"/>, then <see cref="HoleSummaryDeriver"/> (client summary fields ignored).
/// </summary>
internal static class RoundRequestValidation
{
    public const string Required = "Required";
    public const string OutOfRange = "OutOfRange";
    public const string TooLong = "TooLong";

    private const int NotesMaxLength = 4000;
    private const int WeatherConditionsMaxLength = 100;

    public static void ValidateHeader(UpsertRoundRequest request, ValidationErrors errors)
    {
        if (request.TeeId == Guid.Empty)
            errors.Add("teeId", Required);
        if (request.PlayedOn == default)
            errors.Add("playedOn", Required);
        if (request.EntryMode is null)
            errors.Add("entryMode", Required);
        else if (!Enum.IsDefined(request.EntryMode.Value))
            errors.Add("entryMode", OutOfRange);
        if (request.RoundType is { } roundType && !Enum.IsDefined(roundType))
            errors.Add("roundType", OutOfRange);
        if (request.ClientUpdatedAt == default)
            errors.Add("clientUpdatedAt", Required);
        if (request.StartedAt is { } started && request.FinishedAt is { } finished && finished < started)
            errors.Add("finishedAt", OutOfRange);
        if (request.Notes?.Length > NotesMaxLength)
            errors.Add("notes", TooLong);
        if (request.WeatherConditions?.Length > WeatherConditionsMaxLength)
            errors.Add("weatherConditions", TooLong);
        if (request.TemperatureF is < -200m or > 200m)
            errors.Add("temperatureF", OutOfRange);
        if (request.WindSpeedMph is < 0m or > 500m)
            errors.Add("windSpeedMph", OutOfRange);
        if (request.WindDirectionDegrees is < 0 or > 360)
            errors.Add("windDirectionDegrees", OutOfRange);
        if (request.DurationMinutes is < 0)
            errors.Add("durationMinutes", OutOfRange);
        if (request.DistanceMiles is < 0m or > 9999m)
            errors.Add("distanceMiles", OutOfRange);
        if (request.StepCount is < 0)
            errors.Add("stepCount", OutOfRange);
    }

    /// <summary>
    /// The hole results for <paramref name="request"/> on <paramref name="version"/>, with their shots. Entity ids are
    /// assigned here so each shot carries both its round id and its hole result id.
    /// </summary>
    public static List<HoleResult> BuildHoles(Guid roundId, UpsertRoundRequest request, TeeVersion version, ValidationErrors errors)
    {
        var holesOnTee = version.Holes.ToDictionary(h => h.Number);
        var sequences = new HashSet<int>();
        var results = new List<HoleResult>();
        var holes = request.Holes ?? [];

        for (var i = 0; i < holes.Count; i++)
        {
            var hole = holes[i];
            var key = $"holes[{i}]";
            if (hole is null)
            {
                errors.Add(key, Required);
                continue;
            }

            if (hole.EntrySequence < 1)
                errors.Add($"{key}.entrySequence", OutOfRange);
            else if (!sequences.Add(hole.EntrySequence))
                errors.Add($"{key}.entrySequence", "Duplicate");

            if (!holesOnTee.TryGetValue(hole.HoleNumber, out var teeHole))
            {
                errors.Add($"{key}.holeNumber", "NotOnTee");
                continue;
            }

            var info = new HoleInfo(teeHole.Number, teeHole.Par, teeHole.Yardage, teeHole.StrokeIndex);
            var result = request.EntryMode == EntryMode.ShotByShot
                ? BuildShotHole(roundId, hole, info, key, errors)
                : BuildQuickHole(roundId, hole, info, key, errors);
            if (result is not null)
            {
                results.Add(result);
            }
        }

        return results;
    }

    private static HoleResult? BuildQuickHole(Guid roundId, UpsertHoleRequest hole, HoleInfo info, string key, ValidationErrors errors)
    {
        if (hole.Shots is { Count: > 0 })
            errors.Add($"{key}.shots", "NotAllowedForQuickEntry");
        if (hole.Score is null)
        {
            errors.Add($"{key}.score", Required);
            return null;
        }

        var summary = new HoleSummary(
            info.Number, hole.EntrySequence, info.Par, hole.Score.Value, hole.Putts ?? 0,
            hole.FairwayHit ?? false, hole.TeeShotLeft ?? false, hole.TeeShotRight ?? false, hole.TeeShotLong ?? false,
            hole.TeeShotShort ?? false, hole.Gir ?? false, hole.PenaltyStrokes ?? 0, hole.GreensideSandShots ?? 0,
            hole.FairwaySandShots ?? 0);
        var holeErrors = HoleSummaryValidator.Validate(summary);
        foreach (var error in holeErrors)
        {
            errors.Add(key, error.ToString());
        }

        return holeErrors.Count == 0 ? ToEntity(roundId, summary, isDerived: false) : null;
    }

    private static HoleResult? BuildShotHole(Guid roundId, UpsertHoleRequest hole, HoleInfo info, string key, ValidationErrors errors)
    {
        var requested = hole.Shots ?? [];
        var valid = true;
        for (var j = 0; j < requested.Count; j++)
        {
            if (requested[j] is not { } shot)
            {
                errors.Add($"{key}.shots[{j}]", Required);
                valid = false;
            }
            else if (!HasDefinedEnums(shot))
            {
                errors.Add($"{key}.shots[{j}]", OutOfRange);
                valid = false;
            }
        }

        if (!valid)
            return null;

        var shots = requested.Select(s => new Shot(s.Sequence, s.StartLie, s.StartDistance, s.Club, s.ShotType, s.EndLie,
            s.EndDistance, s.Direction, s.Shape, s.PenaltyStrokes)).ToList();
        var shotErrors = ShotValidator.Validate(shots);
        foreach (var error in shotErrors)
        {
            errors.Add($"{key}.shots", error.ToString());
        }

        if (shotErrors.Count > 0)
            return null;

        var result = ToEntity(roundId, HoleSummaryDeriver.Derive(info, hole.EntrySequence, shots), isDerived: true);
        result.Shots.AddRange(requested.OrderBy(s => s.Sequence).Select(s => new ShotEntity
        {
            Id = Guid.CreateVersion7(),
            RoundId = roundId,
            HoleResultId = result.Id,
            Sequence = s.Sequence,
            StartLie = s.StartLie,
            StartDistance = s.StartDistance,
            Club = s.Club,
            ShotType = s.ShotType,
            EndLie = s.EndLie,
            EndDistance = s.EndDistance,
            Direction = s.Direction,
            Shape = s.Shape,
            PenaltyStrokes = s.PenaltyStrokes,
        }));
        return result;
    }

    private static bool HasDefinedEnums(ShotDto shot) =>
        Enum.IsDefined(shot.StartLie) && Enum.IsDefined(shot.EndLie) && Enum.IsDefined(shot.Club)
        && Enum.IsDefined(shot.ShotType) && Enum.IsDefined(shot.Direction) && (shot.Shape is null || Enum.IsDefined(shot.Shape.Value));

    private static HoleResult ToEntity(Guid roundId, HoleSummary summary, bool isDerived) => new()
    {
        Id = Guid.CreateVersion7(),
        RoundId = roundId,
        HoleNumber = summary.HoleNumber,
        EntrySequence = summary.EntrySequence,
        Score = summary.Score,
        Putts = summary.Putts,
        FairwayHit = summary.FairwayHit,
        TeeShotLeft = summary.TeeShotLeft,
        TeeShotRight = summary.TeeShotRight,
        TeeShotLong = summary.TeeShotLong,
        TeeShotShort = summary.TeeShotShort,
        Gir = summary.Gir,
        PenaltyStrokes = summary.PenaltyStrokes,
        GreensideSandShots = summary.GreensideSandShots,
        FairwaySandShots = summary.FairwaySandShots,
        IsDerived = isDerived,
    };
}
