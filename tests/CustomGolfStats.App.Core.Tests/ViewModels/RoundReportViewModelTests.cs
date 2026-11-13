using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.App.Core.Tests.Support;
using CustomGolfStats.App.Core.ViewModels;
using CustomGolfStats.Domain;
using CustomGolfStats.Domain.Holes;
using CustomGolfStats.Domain.Stats;
using Client = CustomGolfStats.Api.Client;

namespace CustomGolfStats.App.Core.Tests.ViewModels;

/// <summary>Round summary, scorecard and overall stats.</summary>
public class RoundReportViewModelTests
{
    private static int Par(int hole) => hole % 3 == 0 ? 3 : hole % 3 == 1 ? 4 : 5; // the sample course

    /// <summary>A quick-entry round on the sample course with the given holes, each scored par plus <paramref name="over"/>.</summary>
    private static async Task<Guid> RoundAsync(VmRig rig, DateTime playedOn, IEnumerable<int> holes, int over = 0, int putts = 2)
    {
        var round = Samples.Round(mode: EntryMode.Quick);
        round.PlayedOn = playedOn;
        var records = holes.Select((h, i) => new HoleRecord(new HoleRow
        {
            RoundId = round.Id, HoleNumber = h, EntrySequence = i + 1, Par = Par(h), Score = Par(h) + over, Putts = putts,
            FairwayHit = Par(h) >= 4, Gir = GirRule.Suggest(Par(h), Par(h) + over, putts),
        }, [])).ToList();
        await rig.Db.Rounds.SaveAsync(new RoundRecord(round, records));
        return round.Id;
    }

    // ---- summary ----

    [Fact]
    public async Task The_summary_shows_the_score_the_holes_and_the_domains_per_round_stats()
    {
        await using var rig = await VmRig.CreateAsync();
        var id = await RoundAsync(rig, new DateTime(2026, 6, 1), Enumerable.Range(1, 9), over: 1);
        var vm = new RoundSummaryViewModel(rig.Db, rig.Nav);

        await vm.LoadAsync(new RoundArgs(id));

        Assert.Equal(("Pine Valley", "Blue", 9), (vm.CourseName, vm.TeeName, vm.HolesPlayed));
        Assert.Equal((45, 36, 9, "+9", 18), (vm.Score, vm.Par, vm.ToPar, vm.ToParText, vm.Putts));
        Assert.Equal(9, vm.Holes.Count);
        var stored = await rig.Db.Rounds.GetAsync(id);
        var expected = SeasonStats.Results([stored!.ToRoundData()])[0];
        Assert.Equal(expected, vm.Result);
    }

    [Fact]
    public async Task The_extras_load_edit_validate_and_save_dirtying_the_round()
    {
        await using var rig = await VmRig.CreateAsync();
        var id = await RoundAsync(rig, new DateTime(2026, 6, 1), [1]);
        await rig.Db.Rounds.MarkCleanAsync(id);
        var vm = new RoundSummaryViewModel(rig.Db, rig.Nav);
        await vm.LoadAsync(new RoundArgs(id));
        Assert.Equal(("windy", 72.5, 270), (vm.Notes, vm.TemperatureF, vm.WindDirectionDegrees));

        vm.Notes = "  great day ";
        vm.RoundType = Client.RoundType.League;
        vm.TemperatureF = 80;
        vm.DurationMinutes = 250;
        vm.StepCount = 12000;
        vm.DistanceMiles = 5.2;
        vm.WeatherConditions = "";
        await vm.SaveExtrasCommand.ExecuteAsync(null);

        Assert.Empty(vm.ExtrasErrors);
        var r = (await rig.Db.Rounds.GetAsync(id))!.Round;
        Assert.Equal(("great day", Client.RoundType.League, 80.0, 250, 12000, 5.2), (r.Notes, r.RoundType, r.TemperatureF, r.DurationMinutes, r.StepCount, r.DistanceMiles));
        Assert.Null(r.WeatherConditions);
        Assert.True(r.Dirty);
    }

    [Fact]
    public async Task Invalid_extras_are_refused_with_messages_and_nothing_is_saved()
    {
        await using var rig = await VmRig.CreateAsync();
        var id = await RoundAsync(rig, new DateTime(2026, 6, 1), [1]);
        var vm = new RoundSummaryViewModel(rig.Db, rig.Nav);
        await vm.LoadAsync(new RoundArgs(id));
        vm.Notes = "changed";
        vm.StepCount = -5;
        vm.WindDirectionDegrees = 400;

        await vm.SaveExtrasCommand.ExecuteAsync(null);

        Assert.Contains(UiText.NegativeNumber, vm.ExtrasErrors);
        Assert.Contains(UiText.WindDirectionInvalid, vm.ExtrasErrors);
        Assert.Equal("windy", (await rig.Db.Rounds.GetAsync(id))!.Round.Notes);
    }

    [Fact]
    public async Task Deleting_a_round_leaves_a_tombstone_and_goes_home()
    {
        await using var rig = await VmRig.CreateAsync();
        var id = await RoundAsync(rig, new DateTime(2026, 6, 1), [1]);
        await rig.Db.Rounds.SaveFromServerAsync(Samples.Synced(new RoundRecord((await rig.Db.Rounds.GetAsync(id))!.Round, (await rig.Db.Rounds.GetAsync(id))!.Holes)), Storage.Repositories.ServerWriteMode.Pull);
        var vm = new RoundSummaryViewModel(rig.Db, rig.Nav);
        await vm.LoadAsync(new RoundArgs(id));

        await vm.DeleteRoundCommand.ExecuteAsync(null);

        Assert.Null(await rig.Db.Rounds.GetAsync(id));
        Assert.True((await rig.Db.Rounds.GetAsync(id, includeDeleted: true))!.Round.Dirty);
        Assert.Equal(Route.Home, rig.Nav.Last.Route);
    }

    [Fact]
    public async Task The_summary_opens_the_scorecard()
    {
        await using var rig = await VmRig.CreateAsync();
        var id = await RoundAsync(rig, new DateTime(2026, 6, 1), [1]);
        var vm = new RoundSummaryViewModel(rig.Db, rig.Nav);
        await vm.LoadAsync(new RoundArgs(id));

        await vm.OpenScorecardCommand.ExecuteAsync(null);

        Assert.Equal((Route.Scorecard, new RoundArgs(id)), rig.Nav.Last);
    }

    [Fact]
    public async Task A_round_with_no_holes_has_no_result_row()
    {
        await using var rig = await VmRig.CreateAsync();
        var id = await rig.CreateRoundAsync(EntryMode.Quick);
        var vm = new RoundSummaryViewModel(rig.Db, rig.Nav);

        await vm.LoadAsync(new RoundArgs(id));

        Assert.Null(vm.Result);
        Assert.Equal(0, vm.HolesPlayed);
    }

    // ---- scorecard ----

    [Fact]
    public async Task The_scorecard_lists_holes_in_play_order_with_tee_details_and_totals()
    {
        await using var rig = await VmRig.CreateAsync();
        var id = await RoundAsync(rig, new DateTime(2026, 6, 1), [10, 2, 10], over: 0);
        var vm = new ScorecardViewModel(rig.Db);

        await vm.LoadAsync(new RoundArgs(id));

        Assert.Equal([10, 2, 10], vm.Rows.Select(r => r.HoleNumber));
        Assert.Equal((300, 10), (vm.Rows[0].Yardage, vm.Rows[0].StrokeIndex));
        Assert.Equal(4 + 5 + 4, vm.TotalScore);
        Assert.Equal("E", vm.ToParText);
        Assert.Equal(6, vm.TotalPutts);
        Assert.Equal(300 + 140 + 300, vm.TotalYardage);
        Assert.Equal((71.2, 130), (vm.Rating, vm.Slope));
    }

    // ---- overall ----

    [Fact]
    public async Task Overall_uses_the_domain_stats_for_the_chosen_year_and_leaves_odd_lengths_out_of_the_9_and_18_figures()
    {
        await using var rig = await VmRig.CreateAsync();
        await RoundAsync(rig, new DateTime(2026, 5, 1), Enumerable.Range(1, 18), over: 1);   // 90
        await RoundAsync(rig, new DateTime(2026, 5, 8), Enumerable.Range(1, 18), over: 2);   // 108
        await RoundAsync(rig, new DateTime(2026, 5, 15), Enumerable.Range(1, 9), over: 0);   // 36
        await RoundAsync(rig, new DateTime(2026, 5, 22), Enumerable.Range(1, 5), over: 0);   // an odd length
        await RoundAsync(rig, new DateTime(2025, 5, 22), Enumerable.Range(1, 18), over: 0);
        await rig.Db.Handicap.UpsertAsync([new HandicapRow { Id = Guid.NewGuid(), EffectiveOn = new DateTime(2026, 4, 1), Index = 14.3 }]);
        var vm = new OverallStatsViewModel(rig.Db, rig.Temp.Clock);

        await vm.LoadAsync();

        Assert.Equal([2026, 2025], vm.Years);
        Assert.Equal(2026, vm.SelectedYear);
        Assert.Equal(4, vm.RoundsInYear);
        Assert.Equal("99.0", vm.ScoringAverage18Text);               // (90 + 108) / 2: the 9-hole and odd rounds are excluded
        Assert.Equal((2, 1, 1), (vm.Season!.Rounds18, vm.Season.Rounds9, vm.Season.RoundsOtherLength));
        Assert.Equal(18 + 36 + 0 + 0, vm.Tiles!.ToPar);
        Assert.Equal(18 + 18 + 9 + 5, vm.Tiles.HolesPlayed);
        Assert.Equal(4, vm.Results.Count);
        Assert.Equal(14.3, vm.HandicapIndex);

        vm.SelectedYear = 2025;
        Assert.Equal("72.0", vm.ScoringAverage18Text);
        Assert.Equal(1, vm.RoundsInYear);
        Assert.Equal("E", vm.ToParText);
    }

    [Fact]
    public async Task Overall_with_no_rounds_shows_dashes_and_the_current_year()
    {
        await using var rig = await VmRig.CreateAsync();
        var vm = new OverallStatsViewModel(rig.Db, rig.Temp.Clock);

        await vm.LoadAsync();

        Assert.Empty(vm.Years);
        Assert.Equal(2026, vm.SelectedYear);
        Assert.Equal("-", vm.ScoringAverage18Text);
        Assert.Empty(vm.Results);
        Assert.Null(vm.HandicapIndex);
    }

    [Fact]
    public async Task Deleted_rounds_do_not_count()
    {
        await using var rig = await VmRig.CreateAsync();
        var keep = await RoundAsync(rig, new DateTime(2026, 5, 1), Enumerable.Range(1, 18));
        var gone = await RoundAsync(rig, new DateTime(2026, 5, 8), Enumerable.Range(1, 18), over: 5);
        await rig.Db.Rounds.MarkDeletedAsync(gone);
        var vm = new OverallStatsViewModel(rig.Db, rig.Temp.Clock);

        await vm.LoadAsync();

        Assert.Equal(1, vm.RoundsInYear);
        Assert.Equal("72.0", vm.ScoringAverage18Text);
        Assert.Equal(keep, vm.Results.Single().RoundId);
    }
}
