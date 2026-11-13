using CustomGolfStats.App.Core.Tests.Support;
using CustomGolfStats.App.Core.ViewModels;
using CustomGolfStats.Domain;
using CustomGolfStats.Domain.Holes;
using CustomGolfStats.Domain.Shots;

namespace CustomGolfStats.App.Core.Tests.ViewModels;

/// <summary>Review focus 2: prefill, feet vs yards, penalties, holed shots, chip-ins, re-tees, and the live summary equalling the
/// domain's derivation. The sample course's hole 1 is par 4, 120 yd; hole 2 par 5, 140 yd; hole 3 par 3, 160 yd.</summary>
public class ShotEntryViewModelTests
{
    private static async Task<(VmRig Rig, ShotEntryViewModel Vm, Guid RoundId)> OpenAsync(int? holeNumber = 1)
    {
        var rig = await VmRig.CreateAsync();
        var roundId = await rig.CreateRoundAsync(EntryMode.ShotByShot);
        var vm = new ShotEntryViewModel(rig.Db, rig.Nav);
        await vm.LoadAsync(new HoleArgs(roundId, HoleNumber: holeNumber));
        return (rig, vm, roundId);
    }

    private static void Shoot(ShotEntryViewModel vm, Lie endLie, int? endDistance = null, int? penalty = null, Direction? direction = null, Shape? shape = null)
    {
        vm.EndLie = endLie;
        vm.EndDistance = endDistance;
        if (penalty is { } p)
            vm.PenaltyStrokes = p;
        if (direction is { } d)
            vm.Direction = d;
        vm.Shape = shape;
        vm.AddShotCommand.Execute(null);
    }

    private static IReadOnlyList<Shot> ShotsOf(ShotEntryViewModel vm) => vm.Shots.Select(s => s.Shot).ToList();

    [Fact]
    public async Task The_first_shot_starts_on_the_tee_at_the_hole_yardage_in_yards()
    {
        var (rig, vm, _) = await OpenAsync();
        await using var _r = rig;

        Assert.Equal((Lie.Tee, 120, "yd"), (vm.StartLie, vm.StartDistance, vm.StartUnit));
        Assert.Equal(ShotType.Tee, vm.ShotType);
        Assert.Equal(Club.Driver, vm.Club);
        Assert.Equal(1, vm.EntrySequence);
        Assert.Equal(4, vm.Par);
    }

    [Fact]
    public async Task Each_new_shot_starts_where_the_previous_one_ended()
    {
        var (rig, vm, _) = await OpenAsync();
        await using var _r = rig;

        Shoot(vm, Lie.Fairway, 150);
        Assert.Equal((Lie.Fairway, 150, "yd", ShotType.Approach), (vm.StartLie, vm.StartDistance, vm.StartUnit, vm.ShotType));

        Shoot(vm, Lie.Rough, 18);
        Assert.Equal((Lie.Rough, 18, "yd"), (vm.StartLie, vm.StartDistance, vm.StartUnit));
        Assert.Equal(2, vm.Shots.Count);
        Assert.Equal([1, 2], vm.Shots.Select(s => s.Shot.Sequence));
    }

    [Fact]
    public async Task Putts_are_in_feet_on_the_green_and_everything_else_in_yards()
    {
        var (rig, vm, _) = await OpenAsync();
        await using var _r = rig;
        Shoot(vm, Lie.Fairway, 150);

        vm.EndLie = Lie.Green;
        Assert.Equal("ft", vm.EndUnit);
        vm.EndLie = Lie.Rough;
        Assert.Equal("yd", vm.EndUnit);
        Shoot(vm, Lie.Green, 30);

        Assert.Equal((Lie.Green, 30, "ft", ShotType.Putt, Club.Putter), (vm.StartLie, vm.StartDistance, vm.StartUnit, vm.ShotType, vm.Club));
        Assert.Equal("ft", vm.Shots[1].EndUnit);
        Assert.Equal("yd", vm.Shots[0].StartUnit);
        Assert.Equal(Lie.Holed, vm.EndLie); // a putt usually holes out; the golfer can change it
    }

    [Fact]
    public async Task Holing_out_needs_no_end_distance_and_completes_the_hole_with_the_domains_summary()
    {
        var (rig, vm, _) = await OpenAsync();
        await using var _r = rig;
        Shoot(vm, Lie.Fairway, 150, direction: Direction.OnTarget);
        Shoot(vm, Lie.Green, 30);
        Shoot(vm, Lie.Green, 3);
        Assert.False(vm.IsComplete);
        Assert.Null(vm.Summary);

        vm.EndLie = Lie.Holed;
        Assert.True(vm.IsHoled);
        Assert.Null(vm.EndDistance);
        vm.AddShotCommand.Execute(null);

        Assert.True(vm.IsComplete);
        var expected = HoleSummaryDeriver.Derive(new HoleInfo(1, 4, 120, 1), 1, ShotsOf(vm));
        Assert.Equal(expected, vm.Summary);
        Assert.Equal(4, vm.Summary!.Score);
        Assert.Equal(2, vm.Summary.Putts);
        Assert.True(vm.Summary.Gir);
        Assert.Equal(4, vm.RunningScore);
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_chip_in_has_no_putts_and_the_hole_is_complete()
    {
        var (rig, vm, _) = await OpenAsync();
        await using var _r = rig;
        Shoot(vm, Lie.Fairway, 150);
        Shoot(vm, Lie.Rough, 20);

        Shoot(vm, Lie.Holed);

        Assert.True(vm.IsComplete);
        Assert.Equal(0, vm.Summary!.Putts);
        Assert.Equal(3, vm.Summary.Score);
        Assert.Equal(HoleSummaryDeriver.Derive(new HoleInfo(1, 4, 120, 1), 1, ShotsOf(vm)), vm.Summary);
    }

    [Fact]
    public async Task A_hole_in_one_is_a_single_holed_tee_shot()
    {
        var (rig, vm, _) = await OpenAsync(holeNumber: 3);
        await using var _r = rig;

        Shoot(vm, Lie.Holed);

        Assert.Equal(1, vm.Summary!.Score);
        Assert.Equal(0, vm.Summary.Putts);
        Assert.Equal(160, vm.Shots[0].Shot.StartDistance);
    }

    [Fact]
    public async Task A_missing_end_distance_is_refused_with_a_clear_message_and_nothing_is_added()
    {
        var (rig, vm, _) = await OpenAsync();
        await using var _r = rig;

        Shoot(vm, Lie.Fairway, endDistance: null);

        Assert.Empty(vm.Shots);
        Assert.Contains(UiText.Describe(ShotError.EndDistanceMismatch), vm.DraftErrors);
    }

    [Fact]
    public async Task Negative_distances_and_penalties_are_refused()
    {
        var (rig, vm, _) = await OpenAsync();
        await using var _r = rig;

        Shoot(vm, Lie.Fairway, -5);
        Assert.Contains(UiText.Describe(ShotError.NegativeDistance), vm.DraftErrors);
        Shoot(vm, Lie.Fairway, 100, penalty: -1);
        Assert.Contains(UiText.Describe(ShotError.NegativePenalty), vm.DraftErrors);
        Assert.Empty(vm.Shots);
    }

    [Fact]
    public async Task Nothing_can_be_added_after_the_ball_is_holed()
    {
        var (rig, vm, _) = await OpenAsync(holeNumber: 3);
        await using var _r = rig;
        Shoot(vm, Lie.Holed);

        Shoot(vm, Lie.Fairway, 10);

        Assert.Single(vm.Shots);
        Assert.Contains(UiText.Describe(ShotError.HoledBeforeLastShot), vm.DraftErrors);
    }

    [Fact]
    public async Task Penalty_strokes_are_zero_or_more_with_no_upper_limit_and_count_in_the_score()
    {
        var (rig, vm, _) = await OpenAsync();
        await using var _r = rig;

        Shoot(vm, Lie.Fairway, 150, penalty: 7); // silly but allowed
        Shoot(vm, Lie.Green, 20);
        Shoot(vm, Lie.Holed);

        Assert.Equal(7, vm.Summary!.PenaltyStrokes);
        Assert.Equal(3 + 7, vm.Summary.Score);
        Assert.Equal(10, vm.RunningScore);
        Assert.Equal(HoleSummaryDeriver.Derive(new HoleInfo(1, 4, 120, 1), 1, ShotsOf(vm)), vm.Summary);
    }

    [Fact]
    public async Task Hitting_into_a_penalty_area_suggests_a_stroke_and_drops_in_the_rough_at_the_distance_it_crossed()
    {
        var (rig, vm, _) = await OpenAsync();
        await using var _r = rig;

        vm.EndLie = Lie.PenaltyArea;
        Assert.Equal(1, vm.PenaltyStrokes);   // suggested
        vm.EndDistance = 110;
        vm.AddShotCommand.Execute(null);

        Assert.Equal((Lie.Rough, 110, "yd"), (vm.StartLie, vm.StartDistance, vm.StartUnit));
        Assert.Equal(0, vm.PenaltyStrokes);   // fresh draft
        Shoot(vm, Lie.Green, 12);
        Shoot(vm, Lie.Holed);
        // tee, drop shot, putt = 3 strokes + 1 penalty
        Assert.Equal(4, vm.Summary!.Score);
        Assert.Equal(1, vm.Summary.PenaltyStrokes);
        Assert.Equal(HoleSummaryDeriver.Derive(new HoleInfo(1, 4, 120, 1), 1, ShotsOf(vm)), vm.Summary);
    }

    [Fact]
    public async Task The_penalty_suggestion_can_be_changed_and_is_withdrawn_if_the_end_lie_changes_back()
    {
        var (rig, vm, _) = await OpenAsync();
        await using var _r = rig;

        vm.EndLie = Lie.PenaltyArea;
        vm.EndLie = Lie.Fairway;
        Assert.Equal(0, vm.PenaltyStrokes);

        vm.EndLie = Lie.PenaltyArea;
        vm.PenaltyStrokes = 2; // golfer's own choice survives
        vm.EndLie = Lie.Fairway;
        Assert.Equal(2, vm.PenaltyStrokes);
    }

    [Fact]
    public async Task An_out_of_bounds_tee_shot_is_replayed_from_the_tee()
    {
        var (rig, vm, _) = await OpenAsync();
        await using var _r = rig;
        vm.Club = Club.Wood3; // the golfer's club choice carries to the re-tee

        vm.EndLie = Lie.OutOfBounds;
        Assert.Equal(1, vm.PenaltyStrokes);
        vm.EndDistance = 100;
        vm.AddShotCommand.Execute(null);

        Assert.Equal((Lie.Tee, 120, "yd", ShotType.Tee, Club.Wood3), (vm.StartLie, vm.StartDistance, vm.StartUnit, vm.ShotType, vm.Club));
        Shoot(vm, Lie.Fairway, 60);
        Shoot(vm, Lie.Green, 10);
        Shoot(vm, Lie.Holed);
        Assert.Equal(5, vm.Summary!.Score); // 4 shots + 1 penalty
        Assert.Equal(HoleSummaryDeriver.Derive(new HoleInfo(1, 4, 120, 1), 1, ShotsOf(vm)), vm.Summary);
    }

    [Fact]
    public async Task Sand_shots_and_tee_shot_direction_flow_into_the_summary()
    {
        var (rig, vm, _) = await OpenAsync();
        await using var _r = rig;

        Shoot(vm, Lie.Rough, 150, direction: Direction.Left);
        Shoot(vm, Lie.GreensideSand, 15);
        Assert.Equal((ShotType.Bunker, Club.SandWedge), (vm.ShotType, vm.Club)); // defaults for a greenside bunker
        Shoot(vm, Lie.Green, 6);
        Shoot(vm, Lie.Holed);

        Assert.Equal(1, vm.Summary!.GreensideSandShots);
        Assert.True(vm.Summary.TeeShotLeft);
        Assert.False(vm.Summary.FairwayHit);
        Assert.Equal(HoleSummaryDeriver.Derive(new HoleInfo(1, 4, 120, 1), 1, ShotsOf(vm)), vm.Summary);
    }

    [Fact]
    public async Task Clubs_the_golfer_picked_are_not_overridden_by_suggestions()
    {
        var (rig, vm, _) = await OpenAsync();
        await using var _r = rig;
        Shoot(vm, Lie.Fairway, 150);

        vm.Club = Club.Iron6;
        vm.StartLie = Lie.Rough; // changes the lie: club choice stays, only the untouched type follows
        Assert.Equal(Club.Iron6, vm.Club);
        Assert.Equal(ShotType.Approach, vm.ShotType);
    }

    [Fact]
    public async Task Editing_a_shot_replaces_it_and_deleting_renumbers_the_rest()
    {
        var (rig, vm, _) = await OpenAsync();
        await using var _r = rig;
        Shoot(vm, Lie.Fairway, 150);
        Shoot(vm, Lie.Green, 25);
        Shoot(vm, Lie.Holed);

        vm.EditShotCommand.Execute(vm.Shots[1]);
        Assert.Equal(1, vm.EditingIndex);
        vm.EndDistance = 40;
        vm.AddShotCommand.Execute(null);
        Assert.Equal(40, vm.Shots[1].Shot.EndDistance);
        Assert.Equal(3, vm.Shots.Count);
        Assert.Null(vm.EditingIndex);

        vm.DeleteShotCommand.Execute(vm.Shots[1]);
        Assert.Equal([1, 2], vm.Shots.Select(s => s.Shot.Sequence));
        vm.UndoLastShotCommand.Execute(null);
        Assert.Single(vm.Shots);
        Assert.False(vm.IsComplete);
        Assert.Equal((Lie.Fairway, 150), (vm.StartLie, vm.StartDistance)); // prefill follows what is left
    }

    [Fact]
    public async Task Saving_stores_a_derived_hole_with_its_shots_and_marks_the_round_dirty()
    {
        var (rig, vm, roundId) = await OpenAsync();
        await using var _r = rig;
        Shoot(vm, Lie.Fairway, 150);
        Shoot(vm, Lie.Green, 30);
        Shoot(vm, Lie.Green, 3);
        Shoot(vm, Lie.Holed);
        await rig.Db.Rounds.MarkCleanAsync(roundId);

        await vm.SaveCommand.ExecuteAsync(null);

        var round = (await rig.Db.Rounds.GetAsync(roundId))!;
        var hole = Assert.Single(round.Holes);
        Assert.True(hole.Hole.IsDerived);
        Assert.Equal(vm.Summary, hole.ToSummary());
        Assert.Equal(ShotsOf(vm), hole.Shots.Select(s => s.ToShot()));
        Assert.True(round.Round.Dirty);
        Assert.Equal(1, rig.Nav.Backs);
    }

    [Fact]
    public async Task An_unfinished_hole_cannot_be_saved()
    {
        var (rig, vm, roundId) = await OpenAsync();
        await using var _r = rig;
        Shoot(vm, Lie.Fairway, 150);

        Assert.False(vm.SaveCommand.CanExecute(null));
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty((await rig.Db.Rounds.GetAsync(roundId))!.Holes);
        Assert.Equal(0, rig.Nav.Backs);
    }

    [Fact]
    public async Task An_existing_hole_opens_with_its_shots_for_editing()
    {
        var (rig, vm, roundId) = await OpenAsync();
        await using var _r = rig;
        Shoot(vm, Lie.Fairway, 150);
        Shoot(vm, Lie.Holed);
        await vm.SaveCommand.ExecuteAsync(null);

        var again = new ShotEntryViewModel(rig.Db, rig.Nav);
        await again.LoadAsync(new HoleArgs(roundId, EntrySequence: 1));

        Assert.False(again.IsNewPlaying);
        Assert.Equal(2, again.Shots.Count);
        Assert.True(again.IsComplete);
        Assert.Equal(vm.Summary, again.Summary);
    }

    [Fact]
    public async Task The_same_hole_can_be_played_twice_with_the_next_entry_sequence()
    {
        var (rig, vm, roundId) = await OpenAsync();
        await using var _r = rig;
        Shoot(vm, Lie.Holed);
        await vm.SaveCommand.ExecuteAsync(null);

        var second = new ShotEntryViewModel(rig.Db, rig.Nav);
        await second.LoadAsync(new HoleArgs(roundId, HoleNumber: 1));
        Shoot(second, Lie.Fairway, 100);
        Shoot(second, Lie.Holed);
        await second.SaveCommand.ExecuteAsync(null);

        Assert.Equal(2, second.EntrySequence);
        var round = (await rig.Db.Rounds.GetAsync(roundId))!;
        Assert.Equal([1, 2], round.Holes.Select(h => h.Hole.EntrySequence));
        Assert.Equal([1, 1], round.Holes.Select(h => h.Hole.HoleNumber));
        Assert.Equal([1, 2], round.Holes.Select(h => h.Shots.Count));
    }

    [Fact]
    public async Task Holes_can_be_entered_in_any_order_and_the_suggestion_follows_the_last_hole()
    {
        var (rig, vm, roundId) = await OpenAsync(holeNumber: 10);
        await using var _r = rig;
        Assert.Equal(10, vm.HoleNumber);
        Shoot(vm, Lie.Holed);
        await vm.SaveCommand.ExecuteAsync(null);

        var next = new ShotEntryViewModel(rig.Db, rig.Nav);
        await next.LoadAsync(new HoleArgs(roundId));

        Assert.Equal(11, next.HoleNumber);
        Assert.Equal(2, next.EntrySequence);
        Assert.Equal(100 + 11 * 20, next.StartDistance);
    }

    [Fact]
    public async Task A_quick_entry_round_cannot_be_opened_for_shots_and_a_missing_hole_is_reported()
    {
        await using var rig = await VmRig.CreateAsync();
        var quick = await rig.CreateRoundAsync(EntryMode.Quick);
        var vm = new ShotEntryViewModel(rig.Db, rig.Nav);

        await vm.LoadAsync(new HoleArgs(quick));
        Assert.Equal(UiText.WrongEntryModeShots, vm.ErrorMessage);

        var shots = await rig.CreateRoundAsync(EntryMode.ShotByShot);
        var other = new ShotEntryViewModel(rig.Db, rig.Nav);
        await other.LoadAsync(new HoleArgs(shots, HoleNumber: 99));
        Assert.Equal(UiText.HoleNotOnCourse, other.ErrorMessage);
    }
}
