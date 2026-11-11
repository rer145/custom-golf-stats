namespace CustomGolfStats.Domain.Stats;

/// <param name="Eagles">Holes at two or more under par. Albatross counts as an eagle (deliberate deviation from golftrack's == -2).</param>
/// <param name="ParBreakers">(Birdies + eagles) / holes. Identical to <paramref name="BirdieOrBetter"/> (as in golftrack).</param>
/// <param name="BirdieOrBetter">(Birdies + eagles) / holes. Identical to <paramref name="ParBreakers"/> (as in golftrack).</param>
public sealed record ScoringSection(
    Average ScoringAverage9, Average ScoringAverage18,
    int? Lowest9, int? Highest9, int? Lowest18, int? Highest18,
    int Birdies, int Eagles,
    Ratio ParBreakers,
    Average Par3Average, Average Par4Average, Average Par5Average,
    Ratio BirdieOrBetter);

public static class ScoringStats
{
    public static ScoringSection Calculate(IReadOnlyList<RoundData> rounds)
    {
        var nines = rounds.Where(r => r.Length == RoundLength.Nine).Select(r => r.Score).ToList();
        var eighteens = rounds.Where(r => r.Length == RoundLength.Eighteen).Select(r => r.Score).ToList();
        var holes = rounds.SelectMany(r => r.Holes).ToList();

        var birdies = holes.Count(h => h.ToPar == -1);
        // ToPar <= -2: albatross and better count as eagles.
        var eagles = holes.Count(h => h.ToPar <= -2);
        var breakers = new Ratio(birdies + eagles, holes.Count);

        Average ParAverage(int par)
        {
            var of = holes.Where(h => h.Par == par).ToList();
            return new Average(of.Sum(h => h.Score), of.Count);
        }

        return new ScoringSection(
            new Average(nines.Sum(), nines.Count),
            new Average(eighteens.Sum(), eighteens.Count),
            nines.Count == 0 ? null : nines.Min(), nines.Count == 0 ? null : nines.Max(),
            eighteens.Count == 0 ? null : eighteens.Min(), eighteens.Count == 0 ? null : eighteens.Max(),
            birdies, eagles,
            breakers,
            ParAverage(3), ParAverage(4), ParAverage(5),
            breakers);
    }
}
