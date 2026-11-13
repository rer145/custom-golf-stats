using CustomGolfStats.Domain.Holes;
using CustomGolfStats.Domain.Shots;
using static CustomGolfStats.Domain.Tests.ShotBuilders;

namespace CustomGolfStats.Domain.Tests.Shots;

public class HoleSummaryDeriverTests
{
    private static HoleInfo Par(int par) => new(1, par, 400, 1);

    private static Shot Putt(int seq, int from, Lie end, int? endDist)
        => Shot(seq, Lie.Green, from, end, endDist, Club.Putter, ShotType.Putt);

    [Fact]
    public void Par4_fairway_green_two_putts()
    {
        Shot[] shots =
        [
            Shot(1, Lie.Tee, 400, Lie.Fairway, 150, Club.Driver, ShotType.Tee),
            Shot(2, Lie.Fairway, 150, Lie.Green, 20),
            Putt(3, 20, Lie.Green, 2),
            Putt(4, 2, Lie.Holed, null),
        ];

        var result = HoleSummaryDeriver.Derive(Par(4), 1, shots);

        Assert.Equal(Builders.Hole(par: 4, score: 4, putts: 2, fairway: true, gir: true), result);
    }

    [Fact]
    public void Par4_tee_left_rough_then_green()
    {
        Shot[] shots =
        [
            Shot(1, Lie.Tee, 400, Lie.Rough, 150, Club.Driver, ShotType.Tee, Direction.Left),
            Shot(2, Lie.Rough, 150, Lie.Green, 20),
            Putt(3, 20, Lie.Green, 2),
            Putt(4, 2, Lie.Holed, null),
        ];

        var result = HoleSummaryDeriver.Derive(Par(4), 1, shots);

        Assert.Equal(Builders.Hole(par: 4, score: 4, putts: 2, fairway: false, gir: true, left: true), result);
    }

    [Fact]
    public void Tee_shot_out_of_bounds_then_retee()
    {
        Shot[] shots =
        [
            Shot(1, Lie.Tee, 400, Lie.OutOfBounds, 400, Club.Driver, ShotType.Tee, Direction.Right, penalties: 1),
            Shot(2, Lie.Tee, 400, Lie.Fairway, 150, Club.Driver, ShotType.Tee),
            Shot(3, Lie.Fairway, 150, Lie.Green, 20),
            Putt(4, 20, Lie.Green, 2),
            Putt(5, 2, Lie.Holed, null),
        ];

        var result = HoleSummaryDeriver.Derive(Par(4), 1, shots);

        Assert.Equal(Builders.Hole(par: 4, score: 6, putts: 2, fairway: false, gir: false, right: true, penalties: 1),
            result);
    }

    [Fact]
    public void Large_penalty_count()
    {
        Shot[] shots =
        [
            Shot(1, Lie.Tee, 150, Lie.Green, 10, Club.Iron7, ShotType.Tee, penalties: 4),
            Putt(2, 10, Lie.Holed, null),
        ];

        var result = HoleSummaryDeriver.Derive(Par(3), 1, shots);

        Assert.Equal(Builders.Hole(par: 3, score: 6, putts: 1, penalties: 4), result);
    }

    [Fact]
    public void Chip_in_from_rough()
    {
        Shot[] shots =
        [
            Shot(1, Lie.Tee, 400, Lie.Rough, 200, Club.Driver, ShotType.Tee, Direction.Left),
            Shot(2, Lie.Rough, 200, Lie.Rough, 15),
            Shot(3, Lie.Rough, 15, Lie.Holed, null, Club.SandWedge, ShotType.Chip),
        ];

        var result = HoleSummaryDeriver.Derive(Par(4), 1, shots);

        Assert.Equal(Builders.Hole(par: 4, score: 3, putts: 0, gir: false, left: true), result);
    }

    [Fact]
    public void Par3_hole_in_one()
    {
        Shot[] shots = [Shot(1, Lie.Tee, 150, Lie.Holed, null, Club.Iron7, ShotType.Tee)];

        var result = HoleSummaryDeriver.Derive(Par(3), 1, shots);

        Assert.Equal(Builders.Hole(par: 3, score: 1, putts: 0, fairway: false, gir: true), result);
    }

    [Fact]
    public void Par3_ignores_tee_shot_direction_and_fairway()
    {
        Shot[] shots =
        [
            Shot(1, Lie.Tee, 150, Lie.Fairway, 10, Club.Iron7, ShotType.Tee, Direction.LongLeft),
            Shot(2, Lie.Fairway, 10, Lie.Holed, null, Club.Putter, ShotType.Putt),
        ];

        var result = HoleSummaryDeriver.Derive(Par(3), 1, shots);

        Assert.False(result.FairwayHit);
        Assert.False(result.TeeShotLeft);
        Assert.False(result.TeeShotLong);
    }

    [Theory]
    [InlineData(Direction.Left, true, false, false, false)]
    [InlineData(Direction.ShortLeft, true, false, true, false)]
    [InlineData(Direction.LongLeft, true, false, false, true)]
    [InlineData(Direction.Right, false, true, false, false)]
    [InlineData(Direction.ShortRight, false, true, true, false)]
    [InlineData(Direction.LongRight, false, true, false, true)]
    [InlineData(Direction.Short, false, false, true, false)]
    [InlineData(Direction.Long, false, false, false, true)]
    [InlineData(Direction.OnTarget, false, false, false, false)]
    public void Tee_shot_direction_maps_to_miss_flags(Direction dir, bool left, bool right, bool shortMiss, bool longMiss)
    {
        Shot[] shots =
        [
            Shot(1, Lie.Tee, 400, Lie.Rough, 150, Club.Driver, ShotType.Tee, dir),
            Shot(2, Lie.Rough, 150, Lie.Holed, null),
        ];

        var result = HoleSummaryDeriver.Derive(Par(4), 1, shots);

        Assert.Equal(left, result.TeeShotLeft);
        Assert.Equal(right, result.TeeShotRight);
        Assert.Equal(shortMiss, result.TeeShotShort);
        Assert.Equal(longMiss, result.TeeShotLong);
    }

    [Fact]
    public void Putt_from_fringe_is_not_a_putt()
    {
        Shot[] shots =
        [
            Shot(1, Lie.Tee, 400, Lie.Fairway, 150, Club.Driver, ShotType.Tee),
            Shot(2, Lie.Fairway, 150, Lie.Fairway, 10),
            Shot(3, Lie.Fairway, 10, Lie.Green, 1, Club.Putter, ShotType.Putt),
            Putt(4, 1, Lie.Holed, null),
        ];

        var result = HoleSummaryDeriver.Derive(Par(4), 1, shots);

        Assert.Equal(Builders.Hole(par: 4, score: 4, putts: 1, fairway: true, gir: false), result);
    }

    [Fact]
    public void Greenside_and_fairway_sand_counted_separately()
    {
        Shot[] shots =
        [
            Shot(1, Lie.Tee, 500, Lie.FairwaySand, 250, Club.Driver, ShotType.Tee),
            Shot(2, Lie.FairwaySand, 250, Lie.GreensideSand, 20, Club.Hybrid4),
            Shot(3, Lie.GreensideSand, 20, Lie.GreensideSand, 15, Club.SandWedge, ShotType.Bunker),
            Shot(4, Lie.GreensideSand, 15, Lie.Green, 5, Club.SandWedge, ShotType.Bunker),
            Putt(5, 5, Lie.Holed, null),
        ];

        var result = HoleSummaryDeriver.Derive(Par(5), 1, shots);

        Assert.Equal(Builders.Hole(par: 5, score: 5, putts: 1, gir: false, greensideSand: 2, fairwaySand: 1), result);
    }

    [Fact]
    public void Invalid_shots_throw()
    {
        Shot[] shots = [Shot(1, Lie.Tee, 400, Lie.Fairway, 150, Club.Driver, ShotType.Tee)];

        Assert.Throws<ArgumentException>(() => HoleSummaryDeriver.Derive(Par(4), 1, shots));
    }

    [Fact]
    public void EntrySequence_and_number_copied()
    {
        Shot[] shots = [Shot(1, Lie.Tee, 150, Lie.Holed, null, Club.Iron7, ShotType.Tee)];

        var result = HoleSummaryDeriver.Derive(new HoleInfo(3, 3, 150, 5), 7, shots);

        Assert.Equal(3, result.HoleNumber);
        Assert.Equal(7, result.EntrySequence);
        Assert.Equal(3, result.Par);
    }
}

public class HoleStrokesTests
{
    [Fact]
    public void Strokes_counts_each_shot_and_every_penalty_stroke_even_before_the_hole_is_finished()
    {
        var shots = new[]
        {
            ShotBuilders.Shot(1, Lie.Tee, 400, Lie.PenaltyArea, 250, penalties: 1),
            ShotBuilders.Shot(2, Lie.Rough, 250, Lie.Fairway, 100, penalties: 2),
        };

        Assert.Equal(5, HoleSummaryDeriver.Strokes(shots));
        Assert.Equal(0, HoleSummaryDeriver.Strokes([]));
    }
}
