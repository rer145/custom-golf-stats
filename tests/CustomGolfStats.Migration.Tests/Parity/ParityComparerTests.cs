using CustomGolfStats.Domain.Holes;
using CustomGolfStats.Domain.Stats;
using CustomGolfStats.Migration.Parity;
using CustomGolfStats.Migration.Source;

namespace CustomGolfStats.Migration.Tests.Parity;

public sealed class ParityComparerTests
{
    public const string ComputedHere = "new value computed by the parity tool (no domain stat)";

    private static readonly LegacyResult Empty = new("Stats_X", []);

    private static HoleSummary Hole(
        int number, int par = 4, int score = 4, int putts = 2, bool gir = true, int greensideSand = 0, int fairwaySand = 0) =>
        new(number, number, par, score, putts, true, false, false, false, false, gir, 0, greensideSand, fairwaySand);

    private static RoundData Round(IEnumerable<HoleSummary> holes, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), new DateOnly(2024, 6, 1), Guid.NewGuid(), "Links", Guid.NewGuid(), "White", holes.ToList());

    private static RoundData Eighteen(Func<int, HoleSummary>? hole = null) =>
        Round(Enumerable.Range(1, 18).Select(n => hole?.Invoke(n) ?? Hole(n)));

    private static Dictionary<string, object?> Golftrack(params (string Column, object? Value)[] values) =>
        values.ToDictionary(v => v.Column, v => v.Value, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<ParityRow> Rows(
        string section, RoundData round, Dictionary<string, object?>? golftrack, LegacyResult? result = null, ParityLegacyRounds? legacy = null)
    {
        var r = result ?? new LegacyResult("Stats_X", golftrack is null ? [] : [golftrack]);
        var results = section switch
        {
            "Scoring" => new LegacyYearResults(r, Empty, Empty, Empty, Empty, Empty, Empty),
            "Approach" => new LegacyYearResults(Empty, Empty, r, Empty, Empty, Empty, Empty),
            "Putting" => new LegacyYearResults(Empty, Empty, Empty, r, Empty, Empty, Empty),
            _ => throw new ArgumentOutOfRangeException(nameof(section)),
        };
        return ParityComparer.Compare(2024, [round], legacy ?? ParityLegacyRounds.None, results).Single(s => s.Title == section).Rows;
    }

    private static ParityRow Row(IReadOnlyList<ParityRow> rows, string column) => rows.Single(r => r.Column == column);

    // ---- The honesty property: intended needs both sides explained ----

    [Fact]
    public void Rule_covered_difference_is_unexplained_when_golftrack_does_not_follow_its_own_definition()
    {
        // The data has an eagle (-2): golftrack's own definition would count it, but golftrack says 0.
        var round = Eighteen(n => n == 2 ? Hole(n, par: 5, score: 3) : Hole(n));

        var eagles = Row(Rows("Scoring", round, Golftrack(("Eagles", 0))), "Eagles");

        Assert.Equal((0d, 1d), (eagles.Golftrack!.Value, eagles.New!.Value));
        Assert.Equal(Verdict.Unexplained, eagles.Verdict);
        Assert.Empty(eagles.Rules);
    }

    [Theory]
    [InlineData(1.0, Verdict.Intended)]   // the new value is what the rule gives
    [InlineData(2.0, Verdict.Unexplained)] // a domain bug: golftrack is explained, the new value is not
    public void Rule_covered_difference_needs_the_new_value_to_follow_the_rule(double newValue, Verdict expected)
    {
        IntendedRule[] rules = [IntendedRules.EaglesIncludeAlbatrosses];

        var verdict = ParityComparer.Judge(golftrack: 0, newValue, ValueKind.Count, rules, legacyDefinition: 0, ruleDefinition: 1);

        Assert.Equal(expected, verdict);
    }

    [Fact]
    public void Without_rules_a_difference_is_unexplained_even_if_definitions_agree()
    {
        Assert.Equal(Verdict.Unexplained, ParityComparer.Judge(0, 1, ValueKind.Count, [], 0, 1));
    }

    [Fact]
    public void Albatross_is_intended()
    {
        var round = Eighteen(n => n == 2 ? Hole(n, par: 5, score: 2, putts: 0) : Hole(n));

        var eagles = Row(Rows("Scoring", round, Golftrack(("Eagles", 0))), "Eagles");

        Assert.Equal(Verdict.Intended, eagles.Verdict);
        Assert.Equal([IntendedRules.EaglesIncludeAlbatrosses], eagles.Rules);
    }

    [Fact]
    public void Birdie_conversion_counting_only_gir_birdies_is_intended()
    {
        // A chip-in birdie (no GIR) and a GIR par: golftrack counts the birdie, the new engine doesn't.
        var round = Eighteen(n => n == 1 ? Hole(n, score: 3, putts: 0, gir: false) : Hole(n));

        var rows = Rows("Putting", round, Golftrack(("TotalBirdiesPlus", 1), ("BirdieConversion", 1.0 / 17)));

        Assert.Equal((Verdict.Intended, 0d), (Row(rows, "TotalBirdiesPlus").Verdict, Row(rows, "TotalBirdiesPlus").New!.Value));
        Assert.Equal(Verdict.Intended, Row(rows, "BirdieConversion").Verdict);
        Assert.Equal([IntendedRules.BirdieConversionGirOnly], Row(rows, "BirdieConversion").Rules);
    }

    [Fact]
    public void Sand_saves_counting_only_greenside_sand_are_intended()
    {
        // A par saved from a fairway bunker: golftrack counted it, the new engine counts greenside sand only.
        var round = Eighteen(n => n == 1 ? Hole(n, fairwaySand: 1, gir: false) : Hole(n));

        var rows = Rows("Approach", round, Golftrack(("HolesWithSandShots", 1), ("SandSaves", 1), ("SandSaveAvg", 1.0)));

        Assert.All(new[] { "HolesWithSandShots", "SandSaves", "SandSaveAvg" }, c => Assert.Equal(Verdict.Intended, Row(rows, c).Verdict));
        Assert.Equal([IntendedRules.SandSavesGreensideOnly], Row(rows, "SandSaves").Rules);
    }

    [Fact]
    public void Round_counted_by_holes_recorded_not_holes_played_is_intended()
    {
        // golftrack says 18 holes played; only 12 were recorded, so the new engine leaves it out of 18-hole figures.
        var id = Guid.NewGuid();
        var round = Round(Enumerable.Range(1, 12).Select(n => Hole(n)), id);
        var legacy = new ParityLegacyRounds(
            new Dictionary<Guid, LegacyRound> { [id] = new(1, 7, 2, null, null, null, null, 18, 48, null, null, new DateTime(2024, 6, 1), null) },
            new Dictionary<int, LegacyTee>());

        var rows = Rows("Scoring", round, Golftrack(("Total18HoleRounds", 1), ("Avg18HoleScore", 48.0), ("Low18HoleRound", 48)), legacy: legacy);

        Assert.All(new[] { "Total18HoleRounds", "Avg18HoleScore", "Low18HoleRound" }, c => Assert.Equal(Verdict.Intended, Row(rows, c).Verdict));
        Assert.Equal([IntendedRules.ExactRoundLength], Row(rows, "Total18HoleRounds").Rules);
    }

    // ---- Reading golftrack's values ----

    [Fact]
    public void Missing_column_and_failed_procedure_are_missing_and_extra_columns_not_compared()
    {
        var rows = Rows("Scoring", Eighteen(), Golftrack(("TotalHoles", 18), ("SomethingNew", 3)));

        var totalHoles = Row(rows, "TotalHoles");
        Assert.Equal((Verdict.Match, ComputedHere), (totalHoles.Verdict, totalHoles.Note));
        Assert.Equal((Verdict.Missing, "column missing in golftrack"), (Row(rows, "Birdies").Verdict, Row(rows, "Birdies").Note));
        Assert.Equal((Verdict.NotCompared, "no counterpart"), (Row(rows, "SomethingNew").Verdict, Row(rows, "SomethingNew").Note));

        var failed = Rows("Scoring", Eighteen(), null, new LegacyResult("Stats_ScoringStats", [], "Could not find stored procedure"));
        Assert.Equal(Verdict.Missing, Assert.Single(failed).Verdict);
        Assert.Contains("Could not find stored procedure", failed[0].Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_row_shows_every_new_value_as_unexplained()
    {
        var rows = Rows("Scoring", Eighteen(), null);

        Assert.All(rows, r => Assert.Equal(Verdict.Unexplained, r.Verdict));
        Assert.Equal(18, Row(rows, "TotalHoles").New);
    }

    [Fact]
    public void Unparseable_value_is_not_compared()
    {
        var birdies = Row(Rows("Scoring", Eighteen(), Golftrack(("Birdies", "lots"))), "Birdies");

        Assert.Equal(Verdict.NotCompared, birdies.Verdict);
        Assert.Equal("unparseable golftrack value: lots", birdies.Note);
    }

    [Theory]
    [InlineData(4.0, Verdict.Match)]
    [InlineData(4.006, Verdict.Match)]
    [InlineData(4.007, Verdict.Unexplained)]
    public void Averages_only_tolerate_rounding(double golftrackPar4Average, Verdict expected)
    {
        var par4 = Row(Rows("Scoring", Eighteen(), Golftrack(("Par4Avg", golftrackPar4Average))), "Par4Avg");

        Assert.Equal(expected, par4.Verdict);
    }

    [Fact]
    public void Counts_have_no_tolerance()
    {
        Assert.Equal(Verdict.Unexplained, Row(Rows("Scoring", Eighteen(), Golftrack(("TotalPar4Score", 71))), "TotalPar4Score").Verdict);
    }

    [Fact]
    public void Other_golfers_rounds_are_noted_on_the_year_only_sections()
    {
        var sections = ParityComparer.Compare(
            2024, [Eighteen()], ParityLegacyRounds.None, new LegacyYearResults(Empty, Empty, Empty, Empty, Empty, Empty, Empty), otherGolfersRounds: 3);

        Assert.Contains("3 active rounds of other golfers", sections.Single(s => s.Title == "Scoring").Note, StringComparison.Ordinal);
        Assert.Null(sections.Single(s => s.Title == "Career (this year)").Note);
    }
}
