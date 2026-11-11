using CustomGolfStats.Domain.Holes;
using CustomGolfStats.Domain.Stats;
using static CustomGolfStats.Domain.Tests.Builders;

namespace CustomGolfStats.Domain.Tests.Stats;

public class PuttingStatsTests
{
    private static readonly DateOnly Day = new(2026, 5, 1);

    private static RoundData R(params HoleSummary[] holes) => Round(Day, holes);

    [Fact]
    public void Putts_per_gir_only_counts_gir_holes()
    {
        var s = PuttingStats.Calculate([R(
            Hole(4, 4, putts: 2, gir: true), Hole(4, 4, putts: 1, gir: true), Hole(4, 6, putts: 3))]);
        Assert.Equal(new Average(3, 2), s.PuttsPerGir);
        Assert.Equal(1.5, s.PuttsPerGir.Value);
        Assert.Equal(new Average(6, 3), s.PuttsPerHole);
    }

    [Fact]
    public void One_and_three_putts()
    {
        var s = PuttingStats.Calculate([R(
            Hole(4, 4, putts: 1), Hole(4, 4, putts: 2), Hole(4, 5, putts: 3), Hole(4, 6, putts: 4))]);
        Assert.Equal(new Ratio(1, 4), s.OnePutt);
        Assert.Equal(new Ratio(2, 4), s.ThreePuttAvoidance);
    }

    [Fact]
    public void Putts_per_round_split_by_length()
    {
        var eighteen = Enumerable.Range(1, 18).Select(n => Hole(4, 4, putts: n <= 14 ? 2 : 1, number: n)).ToArray();
        var twelve = Enumerable.Range(1, 12).Select(n => Hole(4, 4, putts: 3, number: n)).ToArray();
        var s = PuttingStats.Calculate([R(eighteen), R(twelve)]);
        Assert.Equal(32, s.PuttsPerRound18.Value);
        Assert.Equal(new Average(32, 1), s.PuttsPerRound18);
        Assert.Equal(0, s.PuttsPerRound9.Count);
        Assert.Equal(18 + 36, s.PuttsPerHole.Total);
        Assert.Equal(30, s.PuttsPerHole.Count);
    }

    [Fact]
    public void Putts_per_round_nine()
    {
        var nine = Enumerable.Range(1, 9).Select(n => Hole(4, 4, putts: 2, number: n)).ToArray();
        var s = PuttingStats.Calculate([R(nine)]);
        Assert.Equal(18, s.PuttsPerRound9.Value);
        Assert.Null(s.PuttsPerRound18.Value);
    }

    [Fact]
    public void Birdie_conversion()
    {
        var s = PuttingStats.Calculate([R(
            Hole(4, 3, putts: 1, gir: true), Hole(4, 4, putts: 2, gir: true),
            Hole(4, 5, putts: 2), Hole(4, 4, putts: 2, gir: true), Hole(4, 4, putts: 2, gir: true))]);
        Assert.Equal(new Ratio(1, 4), s.BirdieConversion);
    }

    [Fact]
    public void Birdie_conversion_numerator_counts_all_birdie_holes_like_sproc()
    {
        var s = PuttingStats.Calculate([R(Hole(4, 3, putts: 1), Hole(4, 4, putts: 2, gir: true))]);
        Assert.Equal(new Ratio(1, 1), s.BirdieConversion);
    }

    [Fact]
    public void No_gir_gives_null()
    {
        var s = PuttingStats.Calculate([R(Hole(4, 5, putts: 2))]);
        Assert.Null(s.PuttsPerGir.Value);
        Assert.Null(s.BirdieConversion.Value);
        Assert.Equal(2, s.PuttsPerHole.Value);
    }

    [Fact]
    public void Empty_gives_nulls()
    {
        var s = PuttingStats.Calculate([]);
        Assert.Null(s.PuttsPerHole.Value);
        Assert.Null(s.OnePutt.Value);
        Assert.Null(s.ThreePuttAvoidance.Value);
        Assert.Null(s.PuttsPerRound9.Value);
        Assert.Null(s.PuttsPerRound18.Value);
    }
}
