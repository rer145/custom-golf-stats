using CustomGolfStats.Domain.Stats;

namespace CustomGolfStats.Domain.Tests.Stats;

public class ApproachStatsTests
{
    private static readonly DateOnly Day = new(2026, 5, 1);

    private static ApproachSection Calc(params Domain.Holes.HoleSummary[] holes)
        => ApproachStats.Calculate([Builders.Round(Day, holes)]);

    [Fact]
    public void Gir_ratio()
    {
        var s = Calc(Builders.Hole(4, 4, gir: true), Builders.Hole(4, 5), Builders.Hole(4, 4, gir: true), Builders.Hole(4, 6));
        Assert.Equal(new Ratio(2, 4), s.Gir);
    }

    [Fact]
    public void Gir_from_fairway_left_right()
    {
        var s = Calc(
            Builders.Hole(4, 4, fairway: true, gir: true),
            Builders.Hole(4, 5, fairway: true),
            Builders.Hole(4, 4, left: true, gir: true),
            Builders.Hole(4, 5, left: true),
            Builders.Hole(4, 5, left: true),
            Builders.Hole(4, 4, right: true));
        Assert.Equal(new Ratio(1, 2), s.GirFromFairway);
        Assert.Equal(new Ratio(1, 3), s.GirFromLeft);
        Assert.Equal(new Ratio(0, 1), s.GirFromRight);
    }

    [Fact]
    public void Sand_save_ignores_fairway_bunkers()
    {
        var s = Calc(
            Builders.Hole(4, 4, fairwaySand: 1),
            Builders.Hole(4, 4, greensideSand: 1),
            Builders.Hole(4, 5, greensideSand: 1));
        Assert.Equal(new Ratio(1, 2), s.SandSave);
    }

    [Fact]
    public void Scrambling_counts_par_or_better_on_missed_greens()
    {
        var s = Calc(
            Builders.Hole(4, 4, gir: true),
            Builders.Hole(4, 4),
            Builders.Hole(4, 3),
            Builders.Hole(4, 5));
        Assert.Equal(new Ratio(2, 3), s.Scrambling);
    }

    [Fact]
    public void Hole_out_is_zero_putts()
    {
        var s = Calc(Builders.Hole(4, 3, putts: 0), Builders.Hole(4, 4), Builders.Hole(3, 3, putts: 0), Builders.Hole(4, 4));
        Assert.Equal(new Ratio(2, 4), s.HoleOut);
    }

    [Fact]
    public void Empty_input_all_null()
    {
        var s = ApproachStats.Calculate([]);
        Assert.Null(s.Gir.Value);
        Assert.Null(s.GirFromFairway.Value);
        Assert.Null(s.GirFromLeft.Value);
        Assert.Null(s.GirFromRight.Value);
        Assert.Null(s.SandSave.Value);
        Assert.Null(s.Scrambling.Value);
        Assert.Null(s.HoleOut.Value);
    }
}
