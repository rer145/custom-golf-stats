using System.Net;
using CustomGolfStats.Api.Client;
using CustomGolfStats.App.Core.Platform;
using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.App.Core.Tests.Support;
using CustomGolfStats.App.Core.ViewModels;
using Dom = CustomGolfStats.Domain;

namespace CustomGolfStats.App.Core.Tests.ViewModels;

/// <summary>Review focus 3: offline trial logic (spec section 2), plus weather that never blocks the round.</summary>
public class StartRoundViewModelTests
{
    private sealed class Setup : IAsyncDisposable
    {
        public VmRig Rig { get; init; } = null!;
        public StubHandler Server { get; init; } = null!;
        public StartRoundViewModel Vm { get; init; } = null!;
        public Func<Recorded, HttpResponseMessage> Weather { get; set; } =
            _ => StubHandler.Json(HttpStatusCode.OK, new WeatherDto { Conditions = "Sunny", TemperatureF = 71, WindSpeedMph = 6, WindDirectionDegrees = 200 });

        public ValueTask DisposeAsync() => Rig.DisposeAsync();
    }

    private static async Task<(Setup S, FakeConnectivity Net, FakeLocation Loc)> OpenWithFakesAsync(
        Dom.Tier? tier = null, int? trialLeft = null, double? lat = 39.78, double? lon = -74.98)
    {
        var rig = await VmRig.CreateAsync();
        var course = Samples.Course();
        course.Course.Latitude = lat;
        course.Course.Longitude = lon;
        await rig.Db.Courses.SaveFromServerAsync(course);
        if (tier is { } t)
            await rig.Db.Entitlements.SaveAsync(new EntitlementsRow { Tier = t, Role = Dom.Role.User, ShotByShotEntry = true, TrialShotRoundsRemaining = trialLeft });
        var net = new FakeConnectivity();
        var loc = new FakeLocation();
        Setup? setup = null;
        var server = new StubHandler(r => r.Path == "/weather" ? setup!.Weather(r) : StubHandler.Status(HttpStatusCode.NotFound));
        var vm = new StartRoundViewModel(rig.Db, rig.Temp.Clock, rig.Nav, net, loc, server.Client());
        setup = new Setup { Rig = rig, Server = server, Vm = vm };
        await vm.LoadAsync(new CourseArgs(Samples.CourseId));
        return (setup, net, loc);
    }

    private static async Task<Guid> StartedRoundIdAsync(Setup s)
    {
        await s.Vm.StartCommand.ExecuteAsync(null);
        return ((RoundArgs)s.Rig.Nav.Last.Parameter!).RoundId;
    }

    // ---- entitlement gating ----

    [Fact]
    public async Task Without_cached_entitlements_shot_by_shot_is_disabled_with_a_reason_and_quick_entry_still_starts_a_round()
    {
        var (s, _, _) = await OpenWithFakesAsync(tier: null);
        await using var _s = s;

        Assert.False(s.Vm.ShotByShotAllowed);
        Assert.Equal(UiText.ShotByShotNeedsAccount, s.Vm.ShotByShotDisabledReason);
        s.Vm.Mode = Dom.EntryMode.ShotByShot;
        Assert.Equal(Dom.EntryMode.Quick, s.Vm.Mode);   // the disabled option cannot be chosen

        var id = await StartedRoundIdAsync(s);
        Assert.Equal(Dom.EntryMode.Quick, (await s.Rig.Db.Rounds.GetAsync(id))!.Round.EntryMode);
    }

    [Fact]
    public async Task A_free_golfer_with_trial_rounds_left_can_choose_shot_by_shot_and_sees_the_count()
    {
        var (s, _, _) = await OpenWithFakesAsync(Dom.Tier.Free, trialLeft: 2);
        await using var _s = s;

        Assert.True(s.Vm.ShotByShotAllowed);
        Assert.Null(s.Vm.ShotByShotDisabledReason);
        Assert.Equal(UiText.TrialRoundsLeft(2), s.Vm.TrialRoundsText);
        s.Vm.Mode = Dom.EntryMode.ShotByShot;
        Assert.Equal(Dom.EntryMode.ShotByShot, s.Vm.Mode);
    }

    [Fact]
    public async Task Starting_a_shot_by_shot_round_offline_uses_the_cached_count_and_decrements_it_locally()
    {
        var (s, net, _) = await OpenWithFakesAsync(Dom.Tier.Free, trialLeft: 2);
        await using var _s = s;
        net.IsOnline = false;
        s.Vm.Mode = Dom.EntryMode.ShotByShot;

        var id = await StartedRoundIdAsync(s);

        Assert.Equal(1, (await s.Rig.Db.Entitlements.GetAsync())!.TrialShotRoundsRemaining);
        var round = (await s.Rig.Db.Rounds.GetAsync(id))!.Round;
        Assert.Equal(Dom.EntryMode.ShotByShot, round.EntryMode);
        Assert.True(round.Dirty);
        Assert.Empty(s.Server.Requests); // nothing needed the network
    }

    [Fact]
    public async Task Using_the_last_trial_round_closes_the_gate_with_the_used_up_reason()
    {
        var (s, _, _) = await OpenWithFakesAsync(Dom.Tier.Free, trialLeft: 1);
        await using var _s = s;
        s.Vm.Mode = Dom.EntryMode.ShotByShot;
        await StartedRoundIdAsync(s);

        await s.Vm.RefreshEntitlementsAsync();

        Assert.False(s.Vm.ShotByShotAllowed);
        Assert.Equal(UiText.ShotByShotTrialUsedUp, s.Vm.ShotByShotDisabledReason);
        Assert.Equal(Dom.EntryMode.Quick, s.Vm.Mode);
        Assert.Null(s.Vm.TrialRoundsText);
        Assert.Equal(0, (await s.Rig.Db.Entitlements.GetAsync())!.TrialShotRoundsRemaining);
    }

    [Fact]
    public async Task A_free_golfer_with_no_trial_rounds_is_refused_with_the_reason()
    {
        var (s, _, _) = await OpenWithFakesAsync(Dom.Tier.Free, trialLeft: 0);
        await using var _s = s;

        Assert.False(s.Vm.ShotByShotAllowed);
        Assert.Equal(UiText.ShotByShotTrialUsedUp, s.Vm.ShotByShotDisabledReason);
        s.Vm.Mode = Dom.EntryMode.ShotByShot;
        Assert.Equal(Dom.EntryMode.Quick, s.Vm.Mode);
    }

    [Fact]
    public async Task A_paid_golfer_has_shot_by_shot_without_a_trial_count_and_nothing_is_consumed()
    {
        var (s, _, _) = await OpenWithFakesAsync(Dom.Tier.Paid, trialLeft: null);
        await using var _s = s;
        s.Vm.Mode = Dom.EntryMode.ShotByShot;

        Assert.True(s.Vm.ShotByShotAllowed);
        Assert.Null(s.Vm.TrialRoundsText);
        await StartedRoundIdAsync(s);

        Assert.Null((await s.Rig.Db.Entitlements.GetAsync())!.TrialShotRoundsRemaining);
        Assert.True((await s.Rig.Db.Entitlements.GetAsync())!.ShotByShotEntry);
    }

    [Fact]
    public async Task Quick_entry_never_consumes_a_trial_round()
    {
        var (s, _, _) = await OpenWithFakesAsync(Dom.Tier.Free, trialLeft: 3);
        await using var _s = s;

        await StartedRoundIdAsync(s);

        Assert.Equal(3, (await s.Rig.Db.Entitlements.GetAsync())!.TrialShotRoundsRemaining);
    }

    // ---- creating the round ----

    [Fact]
    public async Task A_round_needs_a_tee_and_the_single_tee_is_preselected()
    {
        var (s, _, _) = await OpenWithFakesAsync();
        await using var _s = s;
        Assert.Equal("Blue", s.Vm.SelectedTee!.Name);

        s.Vm.SelectedTee = null;
        await s.Vm.StartCommand.ExecuteAsync(null);

        Assert.Equal(UiText.ChooseTee, s.Vm.ErrorMessage);
        Assert.Empty(s.Rig.Nav.Visits);
    }

    [Fact]
    public async Task Starting_creates_a_dirty_round_stamped_now_with_the_tee_version_and_opens_it()
    {
        var (s, _, _) = await OpenWithFakesAsync();
        await using var _s = s;
        s.Vm.RoundType = RoundType.Tournament;

        var id = await StartedRoundIdAsync(s);

        Assert.Equal(Route.Round, s.Rig.Nav.Last.Route);
        var round = (await s.Rig.Db.Rounds.GetAsync(id))!.Round;
        Assert.True(round.Dirty);
        Assert.Equal(s.Rig.Temp.Clock.UtcNow, round.StartedAt);
        Assert.Equal(s.Rig.Temp.Clock.UtcNow, round.ClientUpdatedAt);
        Assert.Equal((Samples.TeeVersionId, "Pine Valley", "Blue", RoundType.Tournament), (round.TeeVersionId, round.CourseName, round.TeeName, round.RoundType));
    }

    [Fact]
    public async Task The_tee_version_in_effect_on_the_date_played_is_used()
    {
        var (s, _, _) = await OpenWithFakesAsync();
        await using var _s = s;
        var course = Samples.Course();
        var newer = new TeeVersionRow { Id = Guid.NewGuid(), TeeId = Samples.TeeId, EffectiveFrom = new DateTime(2025, 1, 1), Par = 72, Rating = 72, Slope = 133 };
        course.Tees[0].Versions.Add(new Storage.TeeVersionRecord(newer, [new TeeHoleRow { TeeVersionId = newer.Id, Number = 1, Par = 4, Yardage = 400, StrokeIndex = 1 }]));
        await s.Rig.Db.Courses.SaveFromServerAsync(course);
        await s.Vm.LoadAsync(new CourseArgs(Samples.CourseId));

        s.Vm.PlayedOn = new DateTime(2024, 6, 1);
        var old = await StartedRoundIdAsync(s);
        s.Vm.PlayedOn = new DateTime(2026, 5, 1);
        var recent = await StartedRoundIdAsync(s);

        Assert.Equal(Samples.TeeVersionId, (await s.Rig.Db.Rounds.GetAsync(old))!.Round.TeeVersionId);
        Assert.Equal(newer.Id, (await s.Rig.Db.Rounds.GetAsync(recent))!.Round.TeeVersionId);
    }

    // ---- weather ----

    [Fact]
    public async Task Weather_uses_the_course_coordinates_when_online_and_lands_on_the_round()
    {
        var (s, _, loc) = await OpenWithFakesAsync();
        await using var _s = s;

        var id = await StartedRoundIdAsync(s);
        await s.Vm.WeatherTask;

        var get = Assert.Single(s.Server.To("GET", "/weather"));
        Assert.Contains("lat=39.78", get.PathAndQuery);
        Assert.Contains("lon=-74.98", get.PathAndQuery);
        Assert.Equal(0, loc.Calls);
        var round = (await s.Rig.Db.Rounds.GetAsync(id))!.Round;
        Assert.Equal(("Sunny", 71.0, 6.0, 200), (round.WeatherConditions, round.TemperatureF, round.WindSpeedMph, round.WindDirectionDegrees));
    }

    [Fact]
    public async Task Without_course_coordinates_one_coarse_location_reading_is_used()
    {
        var (s, _, loc) = await OpenWithFakesAsync(lat: null, lon: null);
        await using var _s = s;
        loc.Point = new GeoPoint(40.1, -75.2);

        await StartedRoundIdAsync(s);
        await s.Vm.WeatherTask;

        Assert.Equal(1, loc.Calls);
        Assert.Contains("lat=40.1", Assert.Single(s.Server.To("GET", "/weather")).PathAndQuery);
    }

    [Fact]
    public async Task No_location_means_no_weather_request_and_the_round_still_starts()
    {
        var (s, _, loc) = await OpenWithFakesAsync(lat: null, lon: null);
        await using var _s = s;
        loc.Point = null;

        var id = await StartedRoundIdAsync(s);
        await s.Vm.WeatherTask;

        Assert.Empty(s.Server.To("GET", "/weather"));
        Assert.Null((await s.Rig.Db.Rounds.GetAsync(id))!.Round.WeatherConditions);
    }

    [Fact]
    public async Task Offline_no_weather_is_requested()
    {
        var (s, net, _) = await OpenWithFakesAsync();
        await using var _s = s;
        net.IsOnline = false;

        var id = await StartedRoundIdAsync(s);
        await s.Vm.WeatherTask;

        Assert.Empty(s.Server.Requests);
        Assert.NotNull(await s.Rig.Db.Rounds.GetAsync(id));
    }

    [Fact]
    public async Task A_failing_weather_service_never_blocks_or_breaks_the_round()
    {
        var (s, _, _) = await OpenWithFakesAsync();
        await using var _s = s;
        s.Weather = _ => StubHandler.Status(HttpStatusCode.InternalServerError);

        var id = await StartedRoundIdAsync(s);
        await s.Vm.WeatherTask; // completes without throwing

        Assert.Null(s.Vm.ErrorMessage);
        Assert.Null((await s.Rig.Db.Rounds.GetAsync(id))!.Round.WeatherConditions);
    }

    [Fact]
    public async Task A_slow_weather_service_does_not_delay_the_round()
    {
        var (s, _, _) = await OpenWithFakesAsync();
        await using var _s = s;
        using var release = new SemaphoreSlim(0);
        s.Weather = r =>
        {
            release.Wait(TimeSpan.FromSeconds(10));
            return StubHandler.Json(HttpStatusCode.OK, new WeatherDto { Conditions = "Rain", TemperatureF = 50, WindSpeedMph = 1, WindDirectionDegrees = 10 });
        };

        var id = await StartedRoundIdAsync(s);          // returns while the weather call is still waiting
        Assert.Equal(Route.Round, s.Rig.Nav.Last.Route);
        Assert.False(s.Vm.WeatherTask.IsCompleted);
        release.Release();
        await s.Vm.WeatherTask;

        Assert.Equal("Rain", (await s.Rig.Db.Rounds.GetAsync(id))!.Round.WeatherConditions);
    }

    [Fact]
    public async Task A_round_for_another_day_has_no_start_time_and_no_weather()
    {
        var (s, _, _) = await OpenWithFakesAsync();
        await using var _s = s;
        s.Vm.PlayedOn = new DateTime(2026, 5, 20);

        var id = await StartedRoundIdAsync(s);
        await s.Vm.WeatherTask;

        Assert.Null((await s.Rig.Db.Rounds.GetAsync(id))!.Round.StartedAt);
        Assert.Empty(s.Server.To("GET", "/weather"));
    }
}
