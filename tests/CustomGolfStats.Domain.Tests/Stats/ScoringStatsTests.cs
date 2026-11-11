using CustomGolfStats.Domain.Holes;
using CustomGolfStats.Domain.Stats;

namespace CustomGolfStats.Domain.Tests.Stats;

public class ScoringStatsTests
{
    private static readonly DateOnly D = new(2026, 5, 1);

    [Fact]
    public void Empty_input_has_null_values_and_zero_counts()
    {
        var s = ScoringStats.Calculate([]);
        Assert.Null(s.ScoringAverage9.Value);
        Assert.Null(s.ScoringAverage18.Value);
        Assert.Null(s.Lowest9);
        Assert.Null(s.Highest18);
        Assert.Equal(0, s.Birdies);
        Assert.Equal(0, s.Eagles);
        Assert.Null(s.ParBreakers.Value);
        Assert.Null(s.BirdieOrBetter.Value);
        Assert.Null(s.Par3Average.Value);
    }

    [Fact]
    public void Averages_split_by_round_length()
    {
        var s = ScoringStats.Calculate([
            Builders.Round(D, Builders.Holes(18, 4, 4).Select((h, i) => h with { Score = i == 0 ? 12 : 4 }).ToArray()),
            Builders.Round(D, Builders.Holes(18, 4, 5)),
            Builders.Round(D, Builders.Holes(9, 4, 5)),
        ]);
        Assert.Equal(85, s.ScoringAverage18.Value);
        Assert.Equal(45, s.ScoringAverage9.Value);
        Assert.Equal(80, s.Lowest18);
        Assert.Equal(90, s.Highest18);
        Assert.Equal(45, s.Lowest9);
        Assert.Equal(45, s.Highest9);
    }

    [Fact]
    public void Twelve_hole_round_excluded_from_9_18_but_counted_per_hole()
    {
        var holes = Builders.Holes(12, 4, 4);
        holes[0] = holes[0] with { Score = 3 };
        var s = ScoringStats.Calculate([Builders.Round(D, holes)]);
        Assert.Equal(0, s.ScoringAverage18.Count);
        Assert.Equal(0, s.ScoringAverage9.Count);
        Assert.Equal(1, s.Birdies);
        Assert.Equal(12, s.ParBreakers.Denominator);
        Assert.Equal(1, s.ParBreakers.Numerator);
    }

    [Fact]
    public void Eighteen_entries_with_a_repeated_hole_count_as_18()
    {
        var holes = Builders.Holes(18, 4, 5);
        holes[17] = holes[17] with { HoleNumber = 7 };
        var s = ScoringStats.Calculate([Builders.Round(D, holes)]);
        Assert.Equal(1, s.ScoringAverage18.Count);
        Assert.Equal(90, s.ScoringAverage18.Value);
    }

    [Fact]
    public void Par_averages()
    {
        var s = ScoringStats.Calculate([Builders.Round(D, Builders.Hole(3, 3), Builders.Hole(3, 4), Builders.Hole(4, 4))]);
        Assert.Equal(3.5, s.Par3Average.Value);
        Assert.Equal(4.0, s.Par4Average.Value);
        Assert.Null(s.Par5Average.Value);
    }

    [Fact]
    public void Albatross_counts_as_eagle()
    {
        var s = ScoringStats.Calculate([Builders.Round(D, Builders.Hole(5, 2), Builders.Hole(4, 2), Builders.Hole(4, 3))]);
        Assert.Equal(2, s.Eagles);
        Assert.Equal(1, s.Birdies);
        Assert.Equal(3, s.BirdieOrBetter.Numerator);
    }
}
