using CustomGolfStats.Domain.Holes;

namespace CustomGolfStats.Domain.Shots;

public static class HoleSummaryDeriver
{
    /// <summary>Strokes taken so far: one per shot plus every penalty stroke. Works on a hole still in progress.</summary>
    public static int Strokes(IReadOnlyList<Shot> shots) => shots.Count + shots.Sum(s => s.PenaltyStrokes);

    /// <summary>
    /// Derives a hole summary from shot-by-shot data. For shot-entered holes this is the GIR authority:
    /// a penalty taken after the ball reached the green does not affect GIR, unlike
    /// <see cref="GirRule.Suggest"/>, which works from totals only.
    /// </summary>
    /// <exception cref="ArgumentException">The shots fail <see cref="ShotValidator.Validate"/>.</exception>
    public static HoleSummary Derive(HoleInfo hole, int entrySequence, IReadOnlyList<Shot> shots)
    {
        var errors = ShotValidator.Validate(shots);
        if (errors.Count > 0)
            throw new ArgumentException($"Invalid shots: {string.Join(", ", errors)}", nameof(shots));

        var ordered = shots.OrderBy(s => s.Sequence).ToList();
        var first = ordered[0];
        var penalties = ordered.Sum(s => s.PenaltyStrokes);
        var score = Strokes(ordered);
        var drivingHole = DrivingHole.IsDrivingHole(hole.Par);

        var running = 0;
        var gir = false;
        foreach (var shot in ordered)
        {
            running += 1 + shot.PenaltyStrokes;
            if (shot.EndLie is Lie.Green or Lie.Holed)
            {
                gir = running <= hole.Par - 2;
                break;
            }
        }

        var dir = first.Direction;
        return new HoleSummary(
            HoleNumber: hole.Number,
            EntrySequence: entrySequence,
            Par: hole.Par,
            Score: score,
            Putts: ordered.Count(s => s.StartLie == Lie.Green),
            FairwayHit: drivingHole && first.EndLie == Lie.Fairway,
            TeeShotLeft: drivingHole && dir is Direction.Left or Direction.ShortLeft or Direction.LongLeft,
            TeeShotRight: drivingHole && dir is Direction.Right or Direction.ShortRight or Direction.LongRight,
            TeeShotLong: drivingHole && dir is Direction.Long or Direction.LongLeft or Direction.LongRight,
            TeeShotShort: drivingHole && dir is Direction.Short or Direction.ShortLeft or Direction.ShortRight,
            Gir: gir,
            PenaltyStrokes: penalties,
            GreensideSandShots: ordered.Count(s => s.StartLie == Lie.GreensideSand),
            FairwaySandShots: ordered.Count(s => s.StartLie == Lie.FairwaySand));
    }
}
