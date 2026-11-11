namespace CustomGolfStats.Domain.Stats;

public sealed record PuttingSection(
    Average PuttsPerGir,
    Average PuttsPerHole,
    Ratio OnePutt,
    Ratio ThreePuttAvoidance,
    Average PuttsPerRound9, Average PuttsPerRound18,
    Ratio BirdieConversion);

public static class PuttingStats
{
    public static PuttingSection Calculate(IReadOnlyList<RoundData> rounds)
    {
        var holes = rounds.SelectMany(r => r.Holes).ToList();
        var girHoles = holes.Where(h => h.Gir).ToList();

        Average PerRound(RoundLength length)
        {
            var of = rounds.Where(r => r.Length == length).ToList();
            return new Average(of.Sum(r => r.Putts), of.Count);
        }

        // Mirrors Stats_PuttingStats: numerator counts every birdie-or-better hole
        // (not only GIR holes); denominator is GIR holes.
        var birdies = holes.Count(h => h.ToPar <= -1);

        return new PuttingSection(
            new Average(girHoles.Sum(h => h.Putts), girHoles.Count),
            new Average(holes.Sum(h => h.Putts), holes.Count),
            new Ratio(holes.Count(h => h.Putts == 1), holes.Count),
            new Ratio(holes.Count(h => h.Putts >= 3), holes.Count),
            PerRound(RoundLength.Nine), PerRound(RoundLength.Eighteen),
            new Ratio(birdies, girHoles.Count));
    }
}
