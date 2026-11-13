using CustomGolfStats.App.Core.Platform;
using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.App.Core.Tests.Support;
using CustomGolfStats.App.Core.ViewModels;
using CustomGolfStats.Domain;

namespace CustomGolfStats.App.Core.Tests.ViewModels;

public class RoundViewModelTests
{
    private static async Task<(VmRig Rig, RoundViewModel Vm, FakePedometer Pedometer, Guid RoundId)> OpenAsync(EntryMode mode = EntryMode.Quick, bool started = true)
    {
        var rig = await VmRig.CreateAsync();
        var roundId = await rig.CreateRoundAsync(mode);
        await rig.Db.Rounds.UpdateHeaderAsync(roundId, r => r.StartedAt = started ? rig.Temp.Clock.UtcNow : null);
        var pedometer = new FakePedometer();
        var vm = new RoundViewModel(rig.Db, rig.Temp.Clock, rig.Nav, pedometer);
        await vm.LoadAsync(new RoundArgs(roundId));
        return (rig, vm, pedometer, roundId);
    }

    private static async Task AddQuickHoleAsync(VmRig rig, Guid roundId, int holeNumber, int? score = null)
    {
        var quick = new QuickHoleViewModel(rig.Db, rig.Nav);
        await quick.LoadAsync(new HoleArgs(roundId, HoleNumber: holeNumber));
        if (score is { } s)
            quick.Score = s;
        await quick.SaveCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task A_new_round_has_no_holes_and_cannot_be_finished()
    {
        var (rig, vm, _, roundId) = await OpenAsync();
        await using var _r = rig;

        Assert.Equal((0, 0, "E"), (vm.HolesPlayed, vm.TotalScore, vm.ToParText));
        Assert.False(vm.CanFinish);
        Assert.False(vm.FinishRoundCommand.CanExecute(null));
        Assert.Equal(1, vm.SuggestedHoleNumber);

        await vm.FinishRoundCommand.ExecuteAsync(null);

        Assert.Equal(UiText.CannotFinishEmptyRound, vm.ErrorMessage);
        Assert.Null((await rig.Db.Rounds.GetAsync(roundId))!.Round.FinishedAt);
        Assert.Empty(rig.Nav.Visits);
    }

    [Fact]
    public async Task A_round_can_have_any_holes_in_any_order_including_the_same_hole_twice()
    {
        var (rig, vm, _, roundId) = await OpenAsync();
        await using var _r = rig;

        await AddQuickHoleAsync(rig, roundId, 10, score: 5);
        await AddQuickHoleAsync(rig, roundId, 2, score: 6);
        await AddQuickHoleAsync(rig, roundId, 10, score: 4);   // 10 is par 4 (10 % 3 == 1)
        await vm.ReloadAsync();

        Assert.Equal([10, 2, 10], vm.Holes.Select(h => h.HoleNumber));
        Assert.Equal([1, 2, 3], vm.Holes.Select(h => h.EntrySequence));
        Assert.Equal(3, vm.HolesPlayed);
        Assert.Equal(15, vm.TotalScore);
        Assert.Equal(4 + 5 + 4, vm.TotalPar);
        Assert.Equal(15 - 13, vm.ToPar);
        Assert.Equal("+2", vm.ToParText);
        Assert.Equal(11, vm.SuggestedHoleNumber); // the hole after the last one entered (hole 10)
    }

    [Fact]
    public async Task The_suggested_next_hole_follows_the_last_one_played_and_wraps()
    {
        var (rig, vm, _, roundId) = await OpenAsync();
        await using var _r = rig;

        await AddQuickHoleAsync(rig, roundId, 18);
        await vm.ReloadAsync();

        Assert.Equal(1, vm.SuggestedHoleNumber);
        Assert.Equal(18, vm.HoleOptions.Count);
    }

    [Fact]
    public async Task Adding_a_hole_opens_the_entry_screen_for_the_rounds_mode()
    {
        var (rig, quick, _, quickId) = await OpenAsync(EntryMode.Quick);
        await using var _r = rig;
        await quick.AddHoleCommand.ExecuteAsync(null);
        Assert.Equal(Route.QuickHole, rig.Nav.Last.Route);
        Assert.Equal(new HoleArgs(quickId, HoleNumber: 1), rig.Nav.Last.Parameter);

        var shotsId = await rig.CreateRoundAsync(EntryMode.ShotByShot);
        var shots = new RoundViewModel(rig.Db, rig.Temp.Clock, rig.Nav, new FakePedometer());
        await shots.LoadAsync(new RoundArgs(shotsId));
        await shots.AddHoleCommand.ExecuteAsync(7);

        Assert.Equal(Route.ShotEntry, rig.Nav.Last.Route);
        Assert.Equal(new HoleArgs(shotsId, HoleNumber: 7), rig.Nav.Last.Parameter);
    }

    [Fact]
    public async Task Editing_and_deleting_a_hole_work_by_entry_sequence()
    {
        var (rig, vm, _, roundId) = await OpenAsync();
        await using var _r = rig;
        await AddQuickHoleAsync(rig, roundId, 1);
        await AddQuickHoleAsync(rig, roundId, 1);
        await vm.ReloadAsync();

        await vm.EditHoleCommand.ExecuteAsync(vm.Holes[1]);
        Assert.Equal(new HoleArgs(roundId, EntrySequence: 2), rig.Nav.Last.Parameter);

        await vm.DeleteHoleCommand.ExecuteAsync(vm.Holes[0]);
        Assert.Equal([2], vm.Holes.Select(h => h.EntrySequence));
        Assert.Equal(1, vm.HolesPlayed);
    }

    // ---- finishing ----

    [Fact]
    public async Task Finishing_sets_the_end_time_the_duration_and_the_pedometer_extras_then_opens_the_summary()
    {
        var (rig, vm, pedometer, roundId) = await OpenAsync();
        await using var _r = rig;
        await AddQuickHoleAsync(rig, roundId, 1);
        await vm.ReloadAsync();
        var start = rig.Temp.Clock.UtcNow;
        rig.Temp.Clock.Advance(TimeSpan.FromMinutes(95));
        pedometer.Reading = new PedometerReading(11200, 4.6);

        await vm.FinishRoundCommand.ExecuteAsync(null);

        var round = (await rig.Db.Rounds.GetAsync(roundId))!.Round;
        Assert.Equal(rig.Temp.Clock.UtcNow, round.FinishedAt);
        Assert.Equal((95, 11200, 4.6), (round.DurationMinutes, round.StepCount, round.DistanceMiles));
        Assert.Equal((start, rig.Temp.Clock.UtcNow), pedometer.Calls[0]);
        Assert.True(round.Dirty);
        Assert.True(vm.IsFinished);
        Assert.Equal(Route.RoundSummary, rig.Nav.Last.Route);
    }

    [Fact]
    public async Task Values_entered_by_hand_are_not_overwritten_by_the_sensors()
    {
        var (rig, vm, pedometer, roundId) = await OpenAsync();
        await using var _r = rig;
        await AddQuickHoleAsync(rig, roundId, 1);
        await rig.Db.Rounds.UpdateHeaderAsync(roundId, r => { r.StepCount = 9000; r.DurationMinutes = 200; });
        await vm.ReloadAsync();
        rig.Temp.Clock.Advance(TimeSpan.FromMinutes(95));
        pedometer.Reading = new PedometerReading(11200, 4.6);

        await vm.FinishRoundCommand.ExecuteAsync(null);

        var round = (await rig.Db.Rounds.GetAsync(roundId))!.Round;
        Assert.Equal((200, 9000, 4.6), (round.DurationMinutes, round.StepCount, round.DistanceMiles));
    }

    [Fact]
    public async Task A_missing_or_failing_pedometer_never_stops_the_round_finishing()
    {
        var (rig, vm, pedometer, roundId) = await OpenAsync();
        await using var _r = rig;
        await AddQuickHoleAsync(rig, roundId, 1);
        await vm.ReloadAsync();
        pedometer.Throws = true;
        rig.Temp.Clock.Advance(TimeSpan.FromMinutes(60));

        await vm.FinishRoundCommand.ExecuteAsync(null);

        var round = (await rig.Db.Rounds.GetAsync(roundId))!.Round;
        Assert.NotNull(round.FinishedAt);
        Assert.Equal(60, round.DurationMinutes);
        Assert.Null(round.StepCount);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task A_slow_pedometer_is_given_up_on()
    {
        var (rig, vm, pedometer, roundId) = await OpenAsync();
        await using var _r = rig;
        await AddQuickHoleAsync(rig, roundId, 1);
        await vm.ReloadAsync();
        pedometer.Delay = TimeSpan.FromSeconds(30);
        vm.PedometerTimeout = TimeSpan.FromMilliseconds(50);

        await vm.FinishRoundCommand.ExecuteAsync(null);

        Assert.NotNull((await rig.Db.Rounds.GetAsync(roundId))!.Round.FinishedAt);
    }

    [Fact]
    public async Task A_round_for_another_day_has_no_start_so_no_duration_and_no_sensor_reading()
    {
        var (rig, vm, pedometer, roundId) = await OpenAsync(started: false);
        await using var _r = rig;
        await AddQuickHoleAsync(rig, roundId, 1);
        await vm.ReloadAsync();
        pedometer.Reading = new PedometerReading(100, 0.1);

        await vm.FinishRoundCommand.ExecuteAsync(null);

        var round = (await rig.Db.Rounds.GetAsync(roundId))!.Round;
        Assert.NotNull(round.FinishedAt);
        Assert.Null(round.DurationMinutes);
        Assert.Empty(pedometer.Calls);
    }

    [Fact]
    public async Task A_missing_round_is_reported()
    {
        await using var rig = await VmRig.CreateAsync();
        var vm = new RoundViewModel(rig.Db, rig.Temp.Clock, rig.Nav, new FakePedometer());

        await vm.LoadAsync(new RoundArgs(Guid.NewGuid()));

        Assert.Equal(UiText.RoundNotFound, vm.ErrorMessage);
    }
}
