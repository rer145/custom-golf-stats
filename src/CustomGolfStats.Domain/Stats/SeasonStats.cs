using CustomGolfStats.Domain.Holes;

namespace CustomGolfStats.Domain.Stats;

public sealed record OverallTiles(Average ScoringAverage18, int HolesPlayed, int ToPar);

public sealed record ScoreBucket(string Label, int Count);

public sealed record SeasonSection(int Rounds9, int Rounds18, int RoundsOtherLength,
    IReadOnlyList<ScoreBucket> Histogram9,
    IReadOnlyList<ScoreBucket> Histogram18);

public sealed record SeasonResultRow(Guid RoundId, DateOnly PlayedOn, string CourseName, string TeeName,
    int HoleCount, int Score, int ToPar, int Putts, Ratio Gir, Ratio DrivingAccuracy,
    int? Par3ToPar, int? Par4ToPar, int? Par5ToPar);

public static class SeasonStats
{
    public static IReadOnlyList<RoundData> ForYear(IEnumerable<RoundData> rounds, int year)
        => rounds.Where(r => r.PlayedOn.Year == year).ToList();

    public static OverallTiles Overall(IReadOnlyList<RoundData> rounds)
    {
        var eighteen = rounds.Where(r => r.Length == RoundLength.Eighteen).ToList();
        return new OverallTiles(
            new Average(eighteen.Sum(r => r.Score), eighteen.Count),
            rounds.Sum(r => r.Holes.Count),
            rounds.Sum(r => r.ToPar));
    }

    public static SeasonSection Season(IReadOnlyList<RoundData> rounds)
    {
        var nine = rounds.Where(r => r.Length == RoundLength.Nine).Select(r => r.Score).ToList();
        var eighteen = rounds.Where(r => r.Length == RoundLength.Eighteen).Select(r => r.Score).ToList();
        return new SeasonSection(
            nine.Count, eighteen.Count, rounds.Count(r => r.Length is null),
            [
                new("<40", nine.Count(s => s < 40)),
                new("40-49", nine.Count(s => s is >= 40 and < 50)),
                new("50+", nine.Count(s => s >= 50)),
            ],
            [
                new("<80", eighteen.Count(s => s < 80)),
                new("80-89", eighteen.Count(s => s is >= 80 and < 90)),
                new("90-99", eighteen.Count(s => s is >= 90 and < 100)),
                new("100+", eighteen.Count(s => s >= 100)),
            ]);
    }

    public static IReadOnlyList<SeasonResultRow> Results(IReadOnlyList<RoundData> rounds)
        => rounds.OrderByDescending(r => r.PlayedOn).ThenByDescending(r => r.Id).Select(ToRow).ToList();

    private static SeasonResultRow ToRow(RoundData r)
    {
        return new SeasonResultRow(r.Id, r.PlayedOn, r.CourseName, r.TeeName,
            r.Holes.Count, r.Score, r.ToPar, r.Putts,
            new Ratio(r.Holes.Count(h => h.Gir), r.Holes.Count),
            DrivingAccuracy.Of(r.Holes),
            ToParFor(r.Holes, 3), ToParFor(r.Holes, 4), ToParFor(r.Holes, 5));
    }

    private static int? ToParFor(IReadOnlyList<HoleSummary> holes, int par)
    {
        var matching = holes.Where(h => h.Par == par).ToList();
        return matching.Count == 0 ? null : matching.Sum(h => h.ToPar);
    }
}
