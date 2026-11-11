using CustomGolfStats.Domain.Stats;
using static CustomGolfStats.Domain.Tests.Builders;

namespace CustomGolfStats.Domain.Tests.Stats;

public class OffTheTeeStatsTests
{
    private static readonly DateOnly Day = new(2026, 5, 1);

    [Fact]
    public void Par3s_are_not_driving_holes()
    {
        var round = Round(Day, Hole(4, 4, fairway: true), Hole(4, 5), Hole(3, 3));
        var s = OffTheTeeStats.Calculate([round]);
        Assert.Equal(new Ratio(1, 2), s.DrivingAccuracy);
        Assert.Equal(0.5, s.DrivingAccuracy.Value);
    }

    [Fact]
    public void Left_and_right_tendency()
    {
        var round = Round(Day, Hole(4, 4, left: true), Hole(4, 4, left: true), Hole(5, 5, right: true),
            Hole(4, 4, fairway: true), Hole(3, 3, left: true));
        var s = OffTheTeeStats.Calculate([round]);
        Assert.Equal(new Ratio(2, 4), s.LeftTendency);
        Assert.Equal(new Ratio(1, 4), s.RightTendency);
    }

    [Fact]
    public void Score_from_fairway_on_par4()
    {
        var round = Round(Day, Hole(4, 4, fairway: true), Hole(4, 5, fairway: true), Hole(4, 9));
        var s = OffTheTeeStats.Calculate([round]);
        Assert.Equal(0.5, s.Par4ToParFromFairway.Value);
    }

    [Fact]
    public void Score_from_left_and_right_by_par()
    {
        var round = Round(Day,
            Hole(4, 6, left: true), Hole(4, 4, right: true), Hole(4, 5, right: true),
            Hole(5, 7, left: true), Hole(5, 5, fairway: true), Hole(5, 4, fairway: true));
        var s = OffTheTeeStats.Calculate([round]);
        Assert.Equal(2.0, s.Par4ToParFromLeft.Value);
        Assert.Equal(0.5, s.Par4ToParFromRight.Value);
        Assert.Equal(2.0, s.Par5ToParFromLeft.Value);
        Assert.Null(s.Par5ToParFromRight.Value);
        Assert.Equal(-0.5, s.Par5ToParFromFairway.Value);
    }

    [Fact]
    public void No_par5s_gives_null()
    {
        var round = Round(Day, Hole(4, 4, fairway: true));
        var s = OffTheTeeStats.Calculate([round]);
        Assert.Null(s.Par5ToParFromFairway.Value);
        Assert.Null(s.Par5ToParFromLeft.Value);
        Assert.Null(s.Par5ToParFromRight.Value);
    }

    [Fact]
    public void Includes_all_rounds_regardless_of_length()
    {
        var nine = Round(Day, Builders.Holes(9, 4, 4));
        var odd = Round(Day, Hole(4, 4, fairway: true), Hole(4, 4));
        var s = OffTheTeeStats.Calculate([nine, odd]);
        Assert.Equal(new Ratio(1, 11), s.DrivingAccuracy);
    }

    [Fact]
    public void Empty_input_all_null()
    {
        var s = OffTheTeeStats.Calculate([]);
        Assert.Null(s.DrivingAccuracy.Value);
        Assert.Null(s.LeftTendency.Value);
        Assert.Null(s.RightTendency.Value);
        Assert.Null(s.Par4ToParFromFairway.Value);
        Assert.Null(s.Par4ToParFromLeft.Value);
        Assert.Null(s.Par4ToParFromRight.Value);
        Assert.Null(s.Par5ToParFromFairway.Value);
        Assert.Null(s.Par5ToParFromLeft.Value);
        Assert.Null(s.Par5ToParFromRight.Value);
    }
}
