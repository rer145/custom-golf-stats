namespace CustomGolfStats.Domain.Stats;

public sealed record ApproachSection(
    Ratio Gir,
    Ratio GirFromFairway,
    Ratio GirFromLeft,
    Ratio GirFromRight,
    Ratio SandSave,
    Ratio Scrambling,
    Ratio HoleOut);

public static class ApproachStats
{
    public static ApproachSection Calculate(IReadOnlyList<RoundData> rounds)
    {
        var holes = rounds.SelectMany(r => r.Holes).ToList();

        // Par 3 tee shots are excluded, consistent with OffTheTeeStats.
        var teeHoles = holes.Where(h => h.Par != 3).ToList();
        var fairway = teeHoles.Where(h => h.FairwayHit).ToList();
        var left = teeHoles.Where(h => h.TeeShotLeft).ToList();
        var right = teeHoles.Where(h => h.TeeShotRight).ToList();
        var sand = holes.Where(h => h.GreensideSandShots > 0).ToList();
        var missed = holes.Where(h => !h.Gir).ToList();

        return new ApproachSection(
            new Ratio(holes.Count(h => h.Gir), holes.Count),
            new Ratio(fairway.Count(h => h.Gir), fairway.Count),
            new Ratio(left.Count(h => h.Gir), left.Count),
            new Ratio(right.Count(h => h.Gir), right.Count),
            new Ratio(sand.Count(h => h.ToPar <= 0), sand.Count),
            new Ratio(missed.Count(h => h.ToPar <= 0), missed.Count),
            new Ratio(holes.Count(h => h.Putts == 0), holes.Count));
    }
}
