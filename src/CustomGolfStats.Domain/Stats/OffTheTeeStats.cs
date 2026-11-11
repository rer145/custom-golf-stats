using CustomGolfStats.Domain.Holes;

namespace CustomGolfStats.Domain.Stats;

public sealed record OffTheTeeSection(
    Ratio DrivingAccuracy,
    Ratio LeftTendency,
    Ratio RightTendency,
    Average Par4ToParFromFairway, Average Par4ToParFromLeft, Average Par4ToParFromRight,
    Average Par5ToParFromFairway, Average Par5ToParFromLeft, Average Par5ToParFromRight);

public static class OffTheTeeStats
{
    public static OffTheTeeSection Calculate(IReadOnlyList<RoundData> rounds)
    {
        var holes = rounds.SelectMany(r => r.Holes).ToList();
        var driving = holes.Count(h => h.Par != 3);

        Average ToPar(int par, Func<HoleSummary, bool> lie)
        {
            var of = holes.Where(h => h.Par == par && lie(h)).ToList();
            return new Average(of.Sum(h => h.ToPar), of.Count);
        }

        return new OffTheTeeSection(
            new Ratio(holes.Count(h => h.FairwayHit), driving),
            new Ratio(holes.Count(h => h.TeeShotLeft), driving),
            new Ratio(holes.Count(h => h.TeeShotRight), driving),
            ToPar(4, h => h.FairwayHit), ToPar(4, h => h.TeeShotLeft), ToPar(4, h => h.TeeShotRight),
            ToPar(5, h => h.FairwayHit), ToPar(5, h => h.TeeShotLeft), ToPar(5, h => h.TeeShotRight));
    }
}
