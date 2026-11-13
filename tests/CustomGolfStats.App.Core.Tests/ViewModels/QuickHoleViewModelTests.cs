using CustomGolfStats.App.Core.Tests.Support;
using CustomGolfStats.App.Core.ViewModels;
using CustomGolfStats.Domain;
using CustomGolfStats.Domain.Holes;

namespace CustomGolfStats.App.Core.Tests.ViewModels;

public class QuickHoleViewModelTests
{
    private static async Task<(VmRig Rig, QuickHoleViewModel Vm, Guid RoundId)> OpenAsync(int? holeNumber = 1)
    {
        var rig = await VmRig.CreateAsync();
        var roundId = await rig.CreateRoundAsync(EntryMode.Quick);
        var vm = new QuickHoleViewModel(rig.Db, rig.Nav);
        await vm.LoadAsync(new HoleArgs(roundId, HoleNumber: holeNumber));
        return (rig, vm, roundId);
    }

    [Fact]
    public async Task A_new_hole_starts_at_par_with_two_putts_and_the_hole_details_from_the_tee()
    {
        var (rig, vm, _) = await OpenAsync();
        await using var _r = rig;

        Assert.Equal((1, 4, 120, 1, 1), (vm.HoleNumber, vm.Par, vm.Yardage, vm.StrokeIndex, vm.EntrySequence));
        Assert.Equal((4, 2), (vm.Score, vm.Putts));
        Assert.Empty(vm.Errors);
        Assert.True(vm.CanSave);
    }

    [Fact]
    public async Task GIR_is_suggested_by_the_domain_rule_and_follows_score_and_putts()
    {
        var (rig, vm, _) = await OpenAsync(); // par 4
        await using var _r = rig;

        Assert.Equal(GirRule.Suggest(4, 4, 2), vm.Gir);
        Assert.True(vm.Gir);
        vm.Score = 5;
        Assert.Equal(GirRule.Suggest(4, 5, 2), vm.Gir);
        Assert.False(vm.Gir);
        vm.Putts = 1;
        Assert.Equal(GirRule.Suggest(4, 5, 1), vm.Gir);
        Assert.False(vm.GirOverridden);
    }

    [Fact]
    public async Task The_golfer_can_override_GIR_and_the_override_sticks_until_reset()
    {
        var (rig, vm, _) = await OpenAsync();
        await using var _r = rig;

        vm.Gir = false;                       // suggestion is true
        Assert.True(vm.GirOverridden);
        vm.Score = 3;                         // would suggest true again
        Assert.False(vm.Gir);
        Assert.True(vm.GirOverridden);

        vm.ResetGirCommand.Execute(null);
        Assert.False(vm.GirOverridden);
        Assert.Equal(GirRule.Suggest(4, 3, 2), vm.Gir);
    }

    [Fact]
    public async Task Setting_GIR_to_what_is_suggested_is_not_an_override()
    {
        var (rig, vm, _) = await OpenAsync();
        await using var _r = rig;
        vm.Gir = false;

        vm.Gir = true;

        Assert.False(vm.GirOverridden);
    }

    [Theory]
    [InlineData(0, 0, HoleSummaryError.ScoreBelowOne)]
    [InlineData(4, 4, HoleSummaryError.PuttsExceedScore)]
    [InlineData(4, -1, HoleSummaryError.NegativePutts)]
    public async Task Invalid_scores_show_the_domains_messages_and_block_saving(int score, int putts, HoleSummaryError expected)
    {
        var (rig, vm, _) = await OpenAsync();
        await using var _r = rig;

        vm.Score = score;
        vm.Putts = putts;

        Assert.Contains(UiText.Describe(expected), vm.Errors);
        Assert.False(vm.CanSave);
    }

    [Fact]
    public async Task Penalty_and_putts_must_leave_room_for_a_shot_and_negatives_are_refused()
    {
        var (rig, vm, _) = await OpenAsync();
        await using var _r = rig;

        vm.Score = 4; vm.Putts = 2; vm.PenaltyStrokes = 2;
        Assert.Contains(UiText.Describe(HoleSummaryError.PenaltiesAndPuttsExceedScore), vm.Errors);
        vm.PenaltyStrokes = -1;
        Assert.Contains(UiText.Describe(HoleSummaryError.NegativePenalty), vm.Errors);
        vm.PenaltyStrokes = 1; vm.GreensideSandShots = -2;
        Assert.Contains(UiText.Describe(HoleSummaryError.NegativeSand), vm.Errors);
        vm.GreensideSandShots = 0; vm.PenaltyStrokes = 1;
        Assert.Empty(vm.Errors);
    }

    [Fact]
    public async Task Tee_shot_flags_apply_to_par_4s_and_5s_but_not_par_3s()
    {
        var (rig, par4, roundId) = await OpenAsync(1);
        await using var _r = rig;
        var par3 = new QuickHoleViewModel(rig.Db, rig.Nav);
        await par3.LoadAsync(new HoleArgs(roundId, HoleNumber: 3));

        Assert.True(par4.HasTeeShotFlags);
        Assert.False(par3.HasTeeShotFlags);
        Assert.Equal(3, par3.Par);
        Assert.Equal((3, 2), (par3.Score, par3.Putts));
    }

    [Fact]
    public async Task Tee_shot_flags_are_independent_as_in_golftrack()
    {
        var (rig, vm, _) = await OpenAsync();
        await using var _r = rig;

        vm.TeeShotLeft = true; vm.TeeShotLong = true; vm.FairwayHit = false;

        Assert.True(vm.Summary().TeeShotLeft && vm.Summary().TeeShotLong && !vm.Summary().TeeShotRight && !vm.Summary().TeeShotShort);
        Assert.Empty(vm.Errors);
    }

    [Fact]
    public async Task Saving_stores_a_non_derived_hole_without_shots_and_goes_back()
    {
        var (rig, vm, roundId) = await OpenAsync();
        await using var _r = rig;
        vm.Score = 5; vm.Putts = 2; vm.FairwayHit = true; vm.PenaltyStrokes = 1; vm.FairwaySandShots = 1;
        await rig.Db.Rounds.MarkCleanAsync(roundId);

        await vm.SaveCommand.ExecuteAsync(null);

        var round = (await rig.Db.Rounds.GetAsync(roundId))!;
        var hole = Assert.Single(round.Holes);
        Assert.False(hole.Hole.IsDerived);
        Assert.Empty(hole.Shots);
        Assert.Equal((5, 2, true, 1, 1, 1), (hole.Hole.Score, hole.Hole.Putts, hole.Hole.FairwayHit, hole.Hole.PenaltyStrokes, hole.Hole.FairwaySandShots, hole.Hole.EntrySequence));
        Assert.True(round.Round.Dirty);
        Assert.Equal(1, rig.Nav.Backs);
    }

    [Fact]
    public async Task An_invalid_hole_is_not_saved()
    {
        var (rig, vm, roundId) = await OpenAsync();
        await using var _r = rig;
        vm.Score = 0;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty((await rig.Db.Rounds.GetAsync(roundId))!.Holes);
        Assert.Equal(0, rig.Nav.Backs);
    }

    [Fact]
    public async Task An_existing_hole_reopens_with_its_values_and_a_GIR_override_is_remembered()
    {
        var (rig, vm, roundId) = await OpenAsync();
        await using var _r = rig;
        vm.Score = 4; vm.Putts = 2; vm.Gir = false;
        await vm.SaveCommand.ExecuteAsync(null);

        var again = new QuickHoleViewModel(rig.Db, rig.Nav);
        await again.LoadAsync(new HoleArgs(roundId, EntrySequence: 1));

        Assert.False(again.IsNewPlaying);
        Assert.Equal((4, 2, false, true), (again.Score, again.Putts, again.Gir, again.GirOverridden));
    }

    [Fact]
    public async Task A_hole_can_be_played_twice_and_deleted()
    {
        var (rig, vm, roundId) = await OpenAsync();
        await using var _r = rig;
        await vm.SaveCommand.ExecuteAsync(null);

        var second = new QuickHoleViewModel(rig.Db, rig.Nav);
        await second.LoadAsync(new HoleArgs(roundId, HoleNumber: 1));
        Assert.Equal(2, second.EntrySequence);
        await second.SaveCommand.ExecuteAsync(null);
        Assert.Equal([1, 2], (await rig.Db.Rounds.GetAsync(roundId))!.Holes.Select(h => h.Hole.EntrySequence));

        var edit = new QuickHoleViewModel(rig.Db, rig.Nav);
        await edit.LoadAsync(new HoleArgs(roundId, EntrySequence: 1));
        await edit.DeleteCommand.ExecuteAsync(null);
        Assert.Equal([2], (await rig.Db.Rounds.GetAsync(roundId))!.Holes.Select(h => h.Hole.EntrySequence));
    }

    [Fact]
    public async Task A_shot_by_shot_round_is_refused_and_a_missing_hole_is_reported()
    {
        await using var rig = await VmRig.CreateAsync();
        var shots = await rig.CreateRoundAsync(EntryMode.ShotByShot);
        var vm = new QuickHoleViewModel(rig.Db, rig.Nav);
        await vm.LoadAsync(new HoleArgs(shots));
        Assert.Equal(UiText.WrongEntryModeQuick, vm.ErrorMessage);

        var quick = await rig.CreateRoundAsync(EntryMode.Quick);
        var other = new QuickHoleViewModel(rig.Db, rig.Nav);
        await other.LoadAsync(new HoleArgs(quick, HoleNumber: 40));
        Assert.Equal(UiText.HoleNotOnCourse, other.ErrorMessage);
    }
}

public class QuickHoleSuggestionNotificationTests
{
    [Fact]
    public async Task Changing_score_or_putts_announces_the_new_gir_suggestion()
    {
        await using var rig = await VmRig.CreateAsync();
        var roundId = await rig.CreateRoundAsync(EntryMode.Quick);
        var vm = new QuickHoleViewModel(rig.Db, rig.Nav);
        await vm.LoadAsync(new HoleArgs(roundId, HoleNumber: 1));
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Putts = 3;

        Assert.Contains(nameof(QuickHoleViewModel.SuggestedGir), changed);
    }
}
