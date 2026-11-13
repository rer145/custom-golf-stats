using CustomGolfStats.Domain.Holes;
using CustomGolfStats.Domain.Stats;
using CustomGolfStats.Migration.Parity;
using CustomGolfStats.Migration.Source;

namespace CustomGolfStats.Migration.Tests.Parity;

public sealed class ParityComparerTests
{
    private static readonly RoundData Round = new(
        Guid.NewGuid(), new DateOnly(2024, 6, 1), Guid.NewGuid(), "Links", Guid.NewGuid(), "White",
        Enumerable.Range(1, 18).Select(n => new HoleSummary(n, n, 4, 4, 2, true, false, false, false, false, true, 0, 0, 0)).ToList());

    private static readonly LegacyResult Empty = new("Stats_X", []);

    private static LegacyYearResults Results(LegacyResult scoring) => new(scoring, Empty, Empty, Empty, Empty, Empty, Empty);

    private static IReadOnlyList<ParityRow> ScoringRows(LegacyResult scoring) =>
        ParityComparer.Compare(2024, [Round], new Dictionary<Guid, LegacyRound>(), Results(scoring)).Single(s => s.Title == "Scoring").Rows;

    [Fact]
    public void Missing_column_is_listed_as_not_compared_and_extra_columns_too()
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["TotalHoles"] = 18, ["SomethingNew"] = 3 };

        var rows = ScoringRows(new LegacyResult("Stats_ScoringStats", [row]));

        Assert.Equal(Verdict.Match, rows.Single(r => r.Column == "TotalHoles").Verdict);
        var birdies = rows.Single(r => r.Column == "Birdies");
        Assert.Equal((Verdict.NotCompared, "column missing in golftrack"), (birdies.Verdict, birdies.Note));
        Assert.Equal((Verdict.NotCompared, "no counterpart"), rows.Single(r => r.Column == "SomethingNew") is var extra ? (extra.Verdict, extra.Note) : default);
    }

    [Fact]
    public void Failed_procedure_and_missing_row_are_reported_not_hidden()
    {
        var failed = ScoringRows(new LegacyResult("Stats_ScoringStats", [], "Could not find stored procedure"));
        Assert.Equal(Verdict.NotCompared, Assert.Single(failed).Verdict);
        Assert.Contains("Could not find stored procedure", failed[0].Note, StringComparison.Ordinal);

        var noRow = ScoringRows(new LegacyResult("Stats_ScoringStats", []));
        Assert.All(noRow, r => Assert.Equal(Verdict.Unexplained, r.Verdict));
        Assert.Equal(18, noRow.Single(r => r.Column == "TotalHoles").New);
    }

    [Theory]
    [InlineData(4.0, Verdict.Match)]
    [InlineData(4.05, Verdict.Match)]
    [InlineData(4.06, Verdict.Unexplained)]
    public void Averages_only_tolerate_rounding(double golftrackPar4Average, Verdict expected)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Par4Avg"] = golftrackPar4Average };

        Assert.Equal(expected, ScoringRows(new LegacyResult("Stats_ScoringStats", [row])).Single(r => r.Column == "Par4Avg").Verdict);
    }

    [Fact]
    public void Counts_have_no_tolerance()
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["TotalPar4Score"] = 71 };

        Assert.Equal(Verdict.Unexplained, ScoringRows(new LegacyResult("Stats_ScoringStats", [row])).Single(r => r.Column == "TotalPar4Score").Verdict);
    }
}
