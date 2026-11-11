using CustomGolfStats.Domain.Holes;

namespace CustomGolfStats.Domain.Stats;

public sealed record LengthSummary(int Rounds, Average Average, int? Low, int? High);

public sealed record CareerRow(int Year, LengthSummary Nine, LengthSummary Eighteen, int Holes, int ToPar,
    Average PuttsPerHole, Ratio Gir, Ratio DrivingAccuracy, DateOnly Earliest, DateOnly Latest);

public sealed record CourseRow(Guid CourseId, string CourseName, Guid TeeId, string TeeName,
    LengthSummary Nine, LengthSummary Eighteen, int Holes, int ToPar, Average PuttsPerHole,
    Ratio Gir, Ratio DrivingAccuracy, Average Par3ToPar, Average Par4ToPar, Average Par5ToPar,
    DateOnly Earliest, DateOnly Latest);

public static class CareerStats
{
    public static IReadOnlyList<CareerRow> ByYear(IReadOnlyList<RoundData> allRounds)
        => allRounds
            .GroupBy(r => r.PlayedOn.Year)
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var rounds = g.ToList();
                var holes = rounds.SelectMany(r => r.Holes).ToList();
                return new CareerRow(g.Key,
                    Summarize(rounds, RoundLength.Nine), Summarize(rounds, RoundLength.Eighteen),
                    holes.Count, holes.Sum(h => h.ToPar),
                    PuttsPerHole(holes), Gir(holes), Driving(holes),
                    rounds.Min(r => r.PlayedOn), rounds.Max(r => r.PlayedOn));
            })
            .ToList();

    public static IReadOnlyList<CourseRow> ByCourseAndTee(IReadOnlyList<RoundData> rounds)
        => rounds
            .GroupBy(r => (r.CourseId, r.TeeId))
            .Select(g =>
            {
                var rs = g.ToList();
                var holes = rs.SelectMany(r => r.Holes).ToList();
                return new CourseRow(rs[0].CourseId, rs[0].CourseName, rs[0].TeeId, rs[0].TeeName,
                    Summarize(rs, RoundLength.Nine), Summarize(rs, RoundLength.Eighteen),
                    holes.Count, holes.Sum(h => h.ToPar),
                    PuttsPerHole(holes), Gir(holes), Driving(holes),
                    ParToPar(holes, 3), ParToPar(holes, 4), ParToPar(holes, 5),
                    rs.Min(r => r.PlayedOn), rs.Max(r => r.PlayedOn));
            })
            .OrderBy(r => r.CourseName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.TeeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static LengthSummary Summarize(IEnumerable<RoundData> rounds, RoundLength length)
    {
        var scores = rounds.Where(r => r.Length == length).Select(r => r.Score).ToList();
        return new LengthSummary(scores.Count, new Average(scores.Sum(), scores.Count),
            scores.Count == 0 ? null : scores.Min(), scores.Count == 0 ? null : scores.Max());
    }

    private static Average PuttsPerHole(List<HoleSummary> holes) => new(holes.Sum(h => h.Putts), holes.Count);

    private static Ratio Gir(List<HoleSummary> holes) => new(holes.Count(h => h.Gir), holes.Count);

    private static Ratio Driving(List<HoleSummary> holes) => DrivingAccuracy.Of(holes);

    private static Average ParToPar(List<HoleSummary> holes, int par)
    {
        var of = holes.Where(h => h.Par == par).ToList();
        return new Average(of.Sum(h => h.ToPar), of.Count);
    }
}
