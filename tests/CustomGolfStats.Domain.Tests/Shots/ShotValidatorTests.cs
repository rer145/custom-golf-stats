using CustomGolfStats.Domain.Shots;
using static CustomGolfStats.Domain.Tests.ShotBuilders;

namespace CustomGolfStats.Domain.Tests.Shots;

public class ShotValidatorTests
{
    private static Shot[] Par4TwoPutt() =>
    [
        Shot(1, Lie.Tee, 400, Lie.Fairway, 150, Club.Driver, ShotType.Tee),
        Shot(2, Lie.Fairway, 150, Lie.Green, 20),
        Shot(3, Lie.Green, 20, Lie.Green, 2, Club.Putter, ShotType.Putt),
        Shot(4, Lie.Green, 2, Lie.Holed, null, Club.Putter, ShotType.Putt),
    ];

    [Fact]
    public void Valid_par4_two_putt_has_no_errors()
        => Assert.Empty(ShotValidator.Validate(Par4TwoPutt()));

    [Fact]
    public void Empty_list_returns_Empty()
        => Assert.Equal(new[] { ShotError.Empty }, ShotValidator.Validate([]));

    [Fact]
    public void Gap_in_sequence_returns_SequenceNotContiguous()
    {
        Shot[] shots =
        [
            Shot(1, Lie.Tee, 150, Lie.Green, 5, Club.Iron7, ShotType.Tee),
            Shot(3, Lie.Green, 5, Lie.Holed, null, Club.Putter, ShotType.Putt),
        ];
        Assert.Equal(new[] { ShotError.SequenceNotContiguous }, ShotValidator.Validate(shots));
    }

    [Fact]
    public void Holed_in_middle_returns_HoledBeforeLastShot()
    {
        Shot[] shots =
        [
            Shot(1, Lie.Tee, 150, Lie.Holed, null, Club.Iron7, ShotType.Tee),
            Shot(2, Lie.Green, 5, Lie.Holed, null, Club.Putter, ShotType.Putt),
        ];
        Assert.Equal(new[] { ShotError.HoledBeforeLastShot }, ShotValidator.Validate(shots));
    }

    [Fact]
    public void Unfinished_hole_returns_NotHoled()
    {
        Shot[] shots = [Shot(1, Lie.Tee, 400, Lie.Fairway, 150, Club.Driver, ShotType.Tee)];
        Assert.Equal(new[] { ShotError.NotHoled }, ShotValidator.Validate(shots));
    }

    [Fact]
    public void Holed_with_distance_returns_EndDistanceMismatch()
    {
        Shot[] shots = [Shot(1, Lie.Tee, 150, Lie.Holed, 3, Club.Iron7, ShotType.Tee)];
        Assert.Equal(new[] { ShotError.EndDistanceMismatch }, ShotValidator.Validate(shots));
    }

    [Fact]
    public void Missing_end_distance_when_not_holed_returns_EndDistanceMismatch()
    {
        Shot[] shots =
        [
            Shot(1, Lie.Tee, 150, Lie.Green, null, Club.Iron7, ShotType.Tee),
            Shot(2, Lie.Green, 5, Lie.Holed, null, Club.Putter, ShotType.Putt),
        ];
        Assert.Equal(new[] { ShotError.EndDistanceMismatch }, ShotValidator.Validate(shots));
    }

    [Fact]
    public void Negative_penalty_returns_NegativePenalty()
    {
        Shot[] shots = [Shot(1, Lie.Tee, 150, Lie.Holed, null, Club.Iron7, ShotType.Tee, penalties: -1)];
        Assert.Equal(new[] { ShotError.NegativePenalty }, ShotValidator.Validate(shots));
    }

    [Fact]
    public void Negative_distance_returns_NegativeDistance()
    {
        Shot[] shots = [Shot(1, Lie.Tee, -150, Lie.Holed, null, Club.Iron7, ShotType.Tee)];
        Assert.Equal(new[] { ShotError.NegativeDistance }, ShotValidator.Validate(shots));
    }

    [Fact]
    public void Start_from_penalty_area_returns_InvalidStartLie()
    {
        Shot[] shots =
        [
            Shot(1, Lie.Tee, 400, Lie.PenaltyArea, 300, Club.Driver, ShotType.Tee, penalties: 1),
            Shot(2, Lie.PenaltyArea, 300, Lie.Holed, null, Club.Iron7),
        ];
        Assert.Equal(new[] { ShotError.InvalidStartLie }, ShotValidator.Validate(shots));
    }

    [Fact]
    public void Out_of_bounds_retee_with_four_penalty_strokes_is_valid()
    {
        Shot[] shots =
        [
            Shot(1, Lie.Tee, 400, Lie.OutOfBounds, 400, Club.Driver, ShotType.Tee, penalties: 1),
            Shot(2, Lie.Tee, 400, Lie.OutOfBounds, 400, Club.Driver, ShotType.Tee, penalties: 1),
            Shot(3, Lie.Tee, 400, Lie.Fairway, 150, Club.Driver, ShotType.Tee),
            Shot(4, Lie.Fairway, 150, Lie.Holed, null),
        ];
        Assert.Empty(ShotValidator.Validate(shots));

        Shot[] big = [Shot(1, Lie.Tee, 150, Lie.Holed, null, Club.Iron7, ShotType.Tee, penalties: 4)];
        Assert.Empty(ShotValidator.Validate(big));
    }
}
