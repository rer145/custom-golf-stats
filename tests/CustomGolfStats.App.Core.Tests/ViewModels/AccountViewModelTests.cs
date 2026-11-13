using System.Net;
using System.Text.Json;
using CustomGolfStats.App.Core.ApiAccess;
using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.App.Core.Sync;
using CustomGolfStats.App.Core.Tests.Support;
using CustomGolfStats.App.Core.ViewModels;
using Client = CustomGolfStats.Api.Client;

namespace CustomGolfStats.App.Core.Tests.ViewModels;

public class SignInViewModelTests
{
    internal sealed record Built(SignInViewModel Vm, FakeNavigator Nav, FakeSecureStore Store, StubHandler Stub, TempDb Temp, ApiAccess.ApiSession Session);

    internal static Built BuildFull(Func<Recorded, HttpResponseMessage> respond, string profileEmail = "a@b.c")
    {
        var stub = new StubHandler(r => r.Path == "/me"
            ? StubHandler.Json(HttpStatusCode.OK, new Client.ProfileResponse { Id = Guid.NewGuid(), Email = profileEmail, FirstName = "A", LastName = "B", Gender = "Male", Units = "Imperial" })
            : respond(r));
        var store = new FakeSecureStore();
        var session = ApiClientFactory.Create(StubHandler.BaseAddress, store, stub);
        var nav = new FakeNavigator();
        var temp = TempDb.CreateAsync().GetAwaiter().GetResult();
        var vm = new SignInViewModel(session.Tokens, new Storage.AccountSwitchService(temp.Db), session.Client, nav);
        return new Built(vm, nav, store, stub, temp, session);
    }

    private static (SignInViewModel Vm, FakeNavigator Nav, FakeSecureStore Store, StubHandler Stub) Build(Func<Recorded, HttpResponseMessage> respond)
    {
        var b = BuildFull(respond);
        return (b.Vm, b.Nav, b.Store, b.Stub);
    }

    private static HttpResponseMessage Ok(Recorded _) => StubHandler.Json(HttpStatusCode.OK, Payloads.Tokens("acc", "ref"));

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("@x")]
    public async Task A_bad_email_is_refused_without_touching_the_network(string email)
    {
        var (vm, nav, _, stub) = Build(Ok);
        vm.Email = email;
        vm.Password = "pw";

        await vm.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(UiText.EmailInvalid, vm.ErrorMessage);
        Assert.Empty(stub.Requests);
        Assert.Empty(nav.Visits);
    }

    [Fact]
    public async Task A_missing_password_is_refused()
    {
        var (vm, _, _, stub) = Build(Ok);
        vm.Email = "a@b.c";

        await vm.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(UiText.PasswordMissing, vm.ErrorMessage);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Signing_in_stores_the_session_clears_the_password_and_goes_home()
    {
        var (vm, nav, store, stub) = Build(Ok);
        vm.Email = " a@b.c ";
        vm.Password = "secret";

        await vm.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("/auth/login", stub.Requests[0].Path);
        Assert.Contains("\"a@b.c\"", stub.Requests[0].Body);
        Assert.NotEmpty(store.Values);
        Assert.Equal("", vm.Password);
        Assert.Equal(Route.Home, nav.Last.Route);
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Wrong_credentials_show_a_message_and_stay_on_the_screen()
    {
        var (vm, nav, store, _) = Build(_ => StubHandler.Status(HttpStatusCode.Unauthorized));
        vm.Email = "a@b.c";
        vm.Password = "bad";

        await vm.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(UiText.SignInFailed, vm.ErrorMessage);
        Assert.Empty(nav.Visits);
        Assert.Empty(store.Values);
        Assert.Equal("bad", vm.Password);
    }

    [Fact]
    public async Task No_connection_is_reported_as_such()
    {
        var (vm, _, _, _) = Build(_ => throw new HttpRequestException("offline"));
        vm.Email = "a@b.c";
        vm.Password = "pw";

        await vm.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(UiText.SignInOffline, vm.ErrorMessage);
    }

    [Fact]
    public async Task Register_mode_creates_the_account_and_a_rejected_one_says_so()
    {
        var (vm, nav, _, stub) = Build(r => r.Path == "/auth/register" ? StubHandler.Status(HttpStatusCode.OK) : Ok(r));
        vm.ToggleModeCommand.Execute(null);
        vm.Email = "new@b.c";
        vm.Password = "pw";

        await vm.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(["/auth/register", "/auth/login", "/me"], stub.Requests.Select(r => r.Path)); // then the profile, to learn which account this is
        Assert.Equal(Route.Home, nav.Last.Route);

        var (rejected, _, _, _) = Build(_ => StubHandler.Status(HttpStatusCode.BadRequest));
        rejected.ToggleModeCommand.Execute(null);
        rejected.Email = "x@y.z";
        rejected.Password = "pw";
        await rejected.SubmitCommand.ExecuteAsync(null);
        Assert.Equal(UiText.RegisterFailed, rejected.ErrorMessage);
    }

    [Fact]
    public async Task External_sign_in_sends_the_platforms_id_token_and_goes_home()
    {
        var (vm, nav, store, stub) = Build(Ok);

        await vm.ExternalSignInAsync("Google", "id-token", "Ada", "Lovelace");

        Assert.Equal("/auth/external", stub.Requests[0].Path);
        Assert.Contains("id-token", stub.Requests[0].Body);
        Assert.NotEmpty(store.Values);
        Assert.Equal(Route.Home, nav.Last.Route);
    }

    [Fact]
    public async Task An_existing_session_skips_the_form()
    {
        var (vm, nav, store, _) = Build(Ok);
        store.Values[TokenService.RefreshTokenKey] = "r";

        await vm.InitializeAsync();

        Assert.Equal(Route.Home, nav.Last.Route);
    }
}

public class HomeViewModelTests
{
    private sealed class Rig : IAsyncDisposable
    {
        public VmRig Vm { get; init; } = null!;
        public StubHandler Stub { get; init; } = null!;
        public HomeViewModel Home { get; init; } = null!;
        public Func<Recorded, HttpResponseMessage> PutRound { get; set; } = r => StubHandler.Json(HttpStatusCode.OK, Payloads.Round(SyncRig_IdOf(r), 1, "pushed"));
        public Func<Recorded, HttpResponseMessage> Changes { get; set; } = _ => StubHandler.Json(HttpStatusCode.OK, Payloads.Changes(DateTimeOffset.UnixEpoch.AddDays(100)));
        public ValueTask DisposeAsync() => Vm.DisposeAsync();
    }

    private static Guid SyncRig_IdOf(Recorded r) => Guid.Parse(r.Path.Split('/')[2]);

    private static async Task<Rig> CreateAsync()
    {
        var vm = await VmRig.CreateAsync();
        await new Storage.AccountSwitchService(vm.Db).CheckAsync("home@example.com"); // a verified account: sync may run
        Rig? rig = null;
        var stub = new StubHandler(r => r.Method.Method == "PUT" ? rig!.PutRound(r) : r.Path == "/sync/changes" ? rig!.Changes(r) : StubHandler.Status(HttpStatusCode.NotFound));
        var home = new HomeViewModel(vm.Db, new SyncEngine(vm.Db, stub.Client()), vm.Nav);
        rig = new Rig { Vm = vm, Stub = stub, Home = home };
        return rig;
    }

    private static SyncNotice Notice(SyncNoticeKind kind, Guid? round = null, Guid? course = null, string text = "n") => new(kind, round, text) { CourseId = course };

    [Fact]
    public async Task Home_lists_rounds_newest_first_with_scores_and_pending_flags()
    {
        await using var rig = await CreateAsync();
        var older = Samples.FullShotRound(); older.Round.PlayedOn = new DateTime(2026, 5, 1);
        var newer = Samples.FullShotRound(); newer.Round.PlayedOn = new DateTime(2026, 6, 9); newer.Round.FinishedAt = new DateTimeOffset(2026, 6, 9, 14, 0, 0, TimeSpan.Zero);
        await rig.Vm.Db.Rounds.SaveFromServerAsync(Samples.Synced(older), Storage.Repositories.ServerWriteMode.Pull);
        await rig.Vm.Db.Rounds.SaveAsync(newer);

        await rig.Home.LoadAsync();

        Assert.Equal([newer.Round.Id, older.Round.Id], rig.Home.Rounds.Select(r => r.Id));
        Assert.Equal((2, true, true), (rig.Home.Rounds[0].Holes, rig.Home.Rounds[0].IsFinished, rig.Home.Rounds[0].IsWaitingToSync));
        Assert.False(rig.Home.Rounds[1].IsWaitingToSync);
        Assert.Equal(1, rig.Home.PendingCount);
        Assert.Equal(UiText.StatusWaiting(1), rig.Home.SyncStatus);
    }

    [Fact]
    public async Task Opening_a_round_goes_to_the_round_or_its_summary_when_finished()
    {
        await using var rig = await CreateAsync();
        var open = new RoundListItem(Guid.NewGuid(), "A", DateTime.Today, 3, 12, 0, IsFinished: false, IsWaitingToSync: false);
        var done = open with { Id = Guid.NewGuid(), IsFinished = true };

        await rig.Home.OpenRoundCommand.ExecuteAsync(open);
        Assert.Equal((Route.Round, new RoundArgs(open.Id)), rig.Vm.Nav.Last);
        await rig.Home.OpenRoundCommand.ExecuteAsync(done);
        Assert.Equal((Route.RoundSummary, new RoundArgs(done.Id)), rig.Vm.Nav.Last);
        await rig.Home.StartRoundCommand.ExecuteAsync(null);
        Assert.Equal(Route.CoursePicker, rig.Vm.Nav.Last.Route);
        await rig.Home.OpenStatsCommand.ExecuteAsync(null);
        Assert.Equal(Route.OverallStats, rig.Vm.Nav.Last.Route);
        await rig.Home.OpenProfileCommand.ExecuteAsync(null);
        Assert.Equal(Route.Profile, rig.Vm.Nav.Last.Route);
    }

    [Fact]
    public async Task Syncing_pushes_pending_rounds_and_reports_everything_saved()
    {
        await using var rig = await CreateAsync();
        await rig.Vm.Db.Courses.SaveFromServerAsync(Samples.Course());
        await rig.Vm.Db.Rounds.SaveAsync(Samples.FullShotRound());
        await rig.Home.LoadAsync();

        await rig.Home.SyncCommand.ExecuteAsync(null);

        Assert.Equal(UiText.StatusSynced, rig.Home.SyncStatus);
        Assert.Equal(0, rig.Home.PendingCount);
        Assert.False(rig.Home.IsSyncing);
        Assert.False(rig.Home.NeedsSignIn);
        Assert.False(rig.Home.Rounds.Single().IsWaitingToSync);
    }

    [Fact]
    public async Task Offline_and_signed_out_syncs_say_so_and_keep_the_pending_count()
    {
        await using var rig = await CreateAsync();
        await rig.Vm.Db.Courses.SaveFromServerAsync(Samples.Course());
        await rig.Vm.Db.Rounds.SaveAsync(Samples.FullShotRound());
        rig.PutRound = _ => throw new HttpRequestException("offline");

        await rig.Home.SyncCommand.ExecuteAsync(null);
        Assert.Equal(UiText.StatusOffline, rig.Home.SyncStatus);
        Assert.Equal(1, rig.Home.PendingCount);

        rig.PutRound = _ => StubHandler.Status(HttpStatusCode.Unauthorized);
        await rig.Home.SyncCommand.ExecuteAsync(null);
        Assert.Equal(UiText.StatusSignedOut, rig.Home.SyncStatus);
        Assert.True(rig.Home.NeedsSignIn);
        await rig.Home.SignInAgainCommand.ExecuteAsync(null);
        Assert.Equal(Route.SignIn, rig.Vm.Nav.Last.Route);
    }

    [Fact]
    public async Task Reloading_recomputes_the_status_from_what_is_pending_now()
    {
        await using var rig = await CreateAsync();
        await rig.Vm.Db.Courses.SaveFromServerAsync(Samples.Course());
        await rig.Vm.Db.Rounds.SaveAsync(Samples.FullShotRound());
        await rig.Home.SyncCommand.ExecuteAsync(null);
        Assert.Equal(UiText.StatusSynced, rig.Home.SyncStatus);

        await rig.Vm.Db.Rounds.SaveAsync(Samples.FullShotRound()); // the golfer logs another round
        await rig.Home.LoadAsync();

        Assert.Equal(UiText.StatusWaiting(1), rig.Home.SyncStatus);
    }

    [Fact]
    public async Task An_unexpected_sync_error_is_reported_without_crashing_and_the_pending_work_stays()
    {
        await using var rig = await CreateAsync();
        await rig.Vm.Db.Courses.SaveFromServerAsync(Samples.Course());
        await rig.Vm.Db.Rounds.SaveAsync(Samples.FullShotRound());
        rig.PutRound = _ => StubHandler.Status(HttpStatusCode.InternalServerError);
        rig.Changes = _ => throw new InvalidOperationException("boom"); // outside the per-item handlers

        await rig.Home.SyncCommand.ExecuteAsync(null);

        Assert.Equal(UiText.SyncFailedUnexpectedly, rig.Home.ErrorMessage);
        Assert.False(rig.Home.IsSyncing);
        Assert.Equal(1, rig.Home.PendingCount);
        Assert.Equal(UiText.StatusWaiting(1), rig.Home.SyncStatus);
    }

    // ---- notices: once each until the condition clears ----

    [Fact]
    public async Task The_same_notice_reported_by_every_sync_is_shown_once()
    {
        await using var rig = await CreateAsync();
        var round = Guid.NewGuid();
        var result = new SyncResult { PullCompleted = true, Notices = [Notice(SyncNoticeKind.CouldNotSync, round)] };

        rig.Home.Apply(result);
        rig.Home.Apply(result);
        rig.Home.Apply(result);

        Assert.Single(rig.Home.Notices);
    }

    [Fact]
    public async Task Different_rounds_and_courses_each_get_their_own_notice()
    {
        await using var rig = await CreateAsync();
        var (r1, r2, c1) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        rig.Home.Apply(new SyncResult
        {
            PullCompleted = true,
            Notices = [Notice(SyncNoticeKind.CouldNotSync, r1), Notice(SyncNoticeKind.CouldNotSync, r2), Notice(SyncNoticeKind.CouldNotSync, null, c1),
                       Notice(SyncNoticeKind.UpdatedFromAnotherDevice, r1)],
        });

        Assert.Equal(4, rig.Home.Notices.Count);
    }

    [Fact]
    public async Task A_dismissed_notice_stays_away_while_the_condition_persists_and_returns_after_it_clears()
    {
        await using var rig = await CreateAsync();
        var round = Guid.NewGuid();
        var problem = new SyncResult { PullCompleted = true, Notices = [Notice(SyncNoticeKind.CouldNotSync, round)] };
        var clear = new SyncResult { PullCompleted = true };

        rig.Home.Apply(problem);
        rig.Home.DismissNoticeCommand.Execute(rig.Home.Notices[0]);
        Assert.Empty(rig.Home.Notices);

        rig.Home.Apply(problem);                 // still failing: not nagged again
        Assert.Empty(rig.Home.Notices);

        rig.Home.Apply(clear);                   // fixed
        rig.Home.Apply(problem);                 // a new failure later: shown again
        Assert.Single(rig.Home.Notices);
    }

    [Fact]
    public async Task A_sync_that_stopped_early_does_not_clear_a_condition()
    {
        await using var rig = await CreateAsync();
        var round = Guid.NewGuid();
        var problem = new SyncResult { PullCompleted = true, Notices = [Notice(SyncNoticeKind.CouldNotSync, round)] };
        rig.Home.Apply(problem);
        rig.Home.DismissNoticeCommand.Execute(rig.Home.Notices[0]);

        rig.Home.Apply(new SyncResult { Offline = true });   // offline says nothing about the problem
        rig.Home.Apply(problem);

        Assert.Empty(rig.Home.Notices);
    }
}

public class CoursePickerViewModelTests
{
    private static async Task<(VmRig Rig, CoursePickerViewModel Vm, StubHandler Stub, FakeConnectivity Net)> BuildAsync(Func<Recorded, HttpResponseMessage>? respond = null)
    {
        var rig = await VmRig.CreateAsync();
        var stub = new StubHandler(respond ?? (_ => StubHandler.Status(HttpStatusCode.NotFound)));
        var net = new FakeConnectivity();
        return (rig, new CoursePickerViewModel(rig.Db, stub.Client(), net, rig.Temp.Clock, rig.Nav), stub, net);
    }

    private static Client.CourseDetailDto Detail(Guid id, string name) => new()
    {
        Id = id, Name = name, Status = Client.CourseStatus.Imported, Source = Client.CourseSource.GolfCourseApi,
        Tees = [new Client.TeeDto { Id = Guid.NewGuid(), Name = "Blue", Gender = Client.Gender.Male, Versions = [new Client.TeeVersionDto { Id = Guid.NewGuid(), EffectiveFrom = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), Par = 72, Rating = 71, Slope = 130, Holes = [new Client.TeeHoleDto { Number = 1, Par = 4, Yardage = 400, StrokeIndex = 1 }] }] }],
    };

    [Fact]
    public async Task Saved_courses_are_found_instantly_and_offline_without_any_request()
    {
        var (rig, vm, stub, net) = await BuildAsync();
        await using var _r = rig;
        net.IsOnline = false;
        await rig.Db.Courses.SaveFromServerAsync(Samples.Course("Pine Valley"));
        vm.Query = "pine";

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal(["Pine Valley"], vm.LocalResults.Select(c => c.Name));
        Assert.Equal(UiText.ProviderSearchOffline, vm.Message);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Online_search_adds_shared_and_provider_results_and_hides_courses_already_saved()
    {
        var summary = new Client.CourseSummaryDto { Id = Guid.NewGuid(), Name = "Shared Links", Status = Client.CourseStatus.Verified };
        var saved = new Client.CourseSummaryDto { Id = Samples.CourseId, Name = "Pine Valley", Status = Client.CourseStatus.Verified };
        var provider = new Client.ProviderSearchResultDto { ProviderCourseId = "p1", ClubName = "Provider GC", CourseName = "Provider Course" };
        var (rig, vm, stub, _) = await BuildAsync(r => r.Path switch
        {
            "/courses" => StubHandler.Json(HttpStatusCode.OK, new List<Client.CourseSummaryDto> { summary, saved }),
            "/courses/provider-search" => StubHandler.Json(HttpStatusCode.OK, new List<Client.ProviderSearchResultDto> { provider }),
            _ => StubHandler.Status(HttpStatusCode.NotFound),
        });
        await using var _r = rig;
        await rig.Db.Courses.SaveFromServerAsync(Samples.Course("Pine Valley"));
        vm.Query = "pine";

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal(["Shared Links"], vm.OnlineResults.Select(c => c.Name));
        Assert.Equal(["p1"], vm.ProviderResults.Select(p => p.ProviderCourseId));
        Assert.Null(vm.Message);
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task A_failing_online_search_keeps_the_saved_results_and_explains()
    {
        var (rig, vm, _, _) = await BuildAsync(_ => StubHandler.Status(HttpStatusCode.InternalServerError));
        await using var _r = rig;
        await rig.Db.Courses.SaveFromServerAsync(Samples.Course("Pine Valley"));
        vm.Query = "pine";

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Single(vm.LocalResults);
        Assert.Equal(UiText.ProviderSearchFailed, vm.Message);
        Assert.False(vm.IsSearchingOnline);
    }

    [Fact]
    public async Task Picking_a_saved_course_starts_a_round_there()
    {
        var (rig, vm, _, _) = await BuildAsync();
        await using var _r = rig;
        await rig.Db.Courses.SaveFromServerAsync(Samples.Course());
        await vm.LoadAsync();

        await vm.SelectLocalCommand.ExecuteAsync(vm.LocalResults[0]);

        Assert.Equal((Route.StartRound, new CourseArgs(Samples.CourseId)), rig.Nav.Last);
    }

    [Fact]
    public async Task Importing_a_provider_course_saves_it_locally_then_picks_it()
    {
        var newId = Guid.NewGuid();
        var (rig, vm, stub, _) = await BuildAsync(r => r.Path == "/courses/import" ? StubHandler.Json(HttpStatusCode.Created, Detail(newId, "Imported GC")) : StubHandler.Status(HttpStatusCode.NotFound));
        await using var _r = rig;

        await vm.ImportCommand.ExecuteAsync(new Client.ProviderSearchResultDto { ProviderCourseId = "p9", AlreadyImported = false });

        Assert.Contains("p9", Assert.Single(stub.Requests).Body);
        var saved = (await rig.Db.Courses.GetAsync(newId))!;
        Assert.Equal("Imported GC", saved.Course.Name);
        Assert.False(saved.Course.PendingSync);
        Assert.Single(saved.Tees[0].Versions[0].Holes);
        Assert.Equal((Route.StartRound, new CourseArgs(newId)), rig.Nav.Last);
    }

    [Fact]
    public async Task A_provider_course_someone_already_imported_is_fetched_not_imported_again()
    {
        var id = Guid.NewGuid();
        var (rig, vm, stub, _) = await BuildAsync(r => r.Path == $"/courses/{id}" ? StubHandler.Json(HttpStatusCode.OK, Detail(id, "Already There")) : StubHandler.Status(HttpStatusCode.NotFound));
        await using var _r = rig;

        await vm.ImportCommand.ExecuteAsync(new Client.ProviderSearchResultDto { ProviderCourseId = "p1", AlreadyImported = true, CourseId = id });

        Assert.Equal("GET", Assert.Single(stub.Requests).Method.Method);
        Assert.NotNull(await rig.Db.Courses.GetAsync(id));
        Assert.Equal(Route.StartRound, rig.Nav.Last.Route);
    }

    [Fact]
    public async Task Selecting_an_online_course_downloads_it()
    {
        var id = Guid.NewGuid();
        var (rig, vm, _, _) = await BuildAsync(r => r.Path == $"/courses/{id}" ? StubHandler.Json(HttpStatusCode.OK, Detail(id, "Shared Links")) : StubHandler.Status(HttpStatusCode.NotFound));
        await using var _r = rig;

        await vm.SelectOnlineCommand.ExecuteAsync(new Client.CourseSummaryDto { Id = id, Name = "Shared Links" });

        Assert.NotNull(await rig.Db.Courses.GetAsync(id));
        Assert.Equal((Route.StartRound, new CourseArgs(id)), rig.Nav.Last);
    }

    [Fact]
    public async Task A_failed_or_offline_import_says_so_and_goes_nowhere()
    {
        var (rig, vm, _, net) = await BuildAsync(_ => StubHandler.Status(HttpStatusCode.InternalServerError));
        await using var _r = rig;

        await vm.ImportCommand.ExecuteAsync(new Client.ProviderSearchResultDto { ProviderCourseId = "p9" });
        Assert.Equal(UiText.ImportFailed, vm.ErrorMessage);
        Assert.Empty(rig.Nav.Visits);

        net.IsOnline = false;
        await vm.ImportCommand.ExecuteAsync(new Client.ProviderSearchResultDto { ProviderCourseId = "p9" });
        Assert.Equal(UiText.NeedConnection, vm.ErrorMessage);
    }

    [Fact]
    public async Task Favorites_sort_first_and_adding_a_course_by_hand_opens_the_form()
    {
        var (rig, vm, _, _) = await BuildAsync();
        await using var _r = rig;
        await rig.Db.Courses.SaveFromServerAsync(Samples.Course("Zeta Course"));
        await vm.LoadAsync();

        await vm.ToggleFavoriteCommand.ExecuteAsync(vm.LocalResults[0]);
        Assert.True(vm.LocalResults[0].IsFavorite);

        await vm.AddManualCourseCommand.ExecuteAsync(null);
        Assert.Equal(Route.ManualCourse, rig.Nav.Last.Route);
    }
}

public class ManualCourseViewModelTests
{
    private static async Task<(VmRig Rig, ManualCourseViewModel Vm)> BuildAsync(int holes = 18)
    {
        var rig = await VmRig.CreateAsync();
        var vm = new ManualCourseViewModel(rig.Db, rig.Temp.Clock, rig.Nav);
        vm.Initialize(holes);
        return (rig, vm);
    }

    private static void Fill(ManualCourseViewModel vm)
    {
        vm.Name = "  My Home Track ";
        vm.TeeName = "Blue";
        vm.Rating = 70.5;
        vm.Slope = 125;
    }

    private static string Msg(CustomGolfStats.Domain.Courses.CourseError error) => UiText.Describe(error);

    [Fact]
    public async Task An_empty_form_lists_what_is_missing()
    {
        var (rig, vm) = await BuildAsync();
        await using var _r = rig;
        vm.TeeName = "";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Contains(UiText.CourseNameMissing, vm.Errors);
        Assert.Contains(UiText.TeeNameMissing, vm.Errors);
        Assert.Contains(Msg(CustomGolfStats.Domain.Courses.CourseError.RatingMissing), vm.Errors);
        Assert.Contains(Msg(CustomGolfStats.Domain.Courses.CourseError.SlopeMissing), vm.Errors);
        Assert.Empty(await rig.Db.Courses.GetPendingAsync());
        Assert.Empty(rig.Nav.Visits);
    }

    // The limits are the domain's CourseLimits, the same ones the API enforces: a course accepted here is accepted on sync.

    [Theory]
    [InlineData(1.0, true)]
    [InlineData(99.9, true)]
    [InlineData(0.9, false)]
    [InlineData(100.0, false)]
    public async Task Rating_boundaries_match_the_API(double rating, bool valid)
    {
        var (rig, vm) = await BuildAsync(9);
        await using var _r = rig;
        Fill(vm);
        vm.Rating = rating;

        Assert.Equal(valid, vm.Validate().Count == 0);
    }

    [Theory]
    [InlineData(55, true)]
    [InlineData(155, true)]
    [InlineData(54, false)]
    [InlineData(156, false)]
    public async Task Slope_boundaries_match_the_API(int slope, bool valid)
    {
        var (rig, vm) = await BuildAsync(9);
        await using var _r = rig;
        Fill(vm);
        vm.Slope = slope;

        Assert.Equal(valid, vm.Validate().Count == 0);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(6, true)]   // the API accepts par 6
    [InlineData(2, false)]
    [InlineData(7, false)]
    public async Task Par_boundaries_match_the_API(int par, bool valid)
    {
        var (rig, vm) = await BuildAsync(9);
        await using var _r = rig;
        Fill(vm);
        vm.Holes[0].Par = par;

        Assert.Equal(valid, vm.Validate().Count == 0);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1000, true)]
    [InlineData(-1, false)]
    [InlineData(1001, false)]
    public async Task Yardage_boundaries_match_the_API(int yardage, bool valid)
    {
        var (rig, vm) = await BuildAsync(9);
        await using var _r = rig;
        Fill(vm);
        vm.Holes[0].Yardage = yardage;

        Assert.Equal(valid, vm.Validate().Count == 0);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(18, true)]
    [InlineData(0, false)]
    [InlineData(19, false)]
    public async Task Hole_count_boundaries_match_the_API(int count, bool valid)
    {
        var (rig, vm) = await BuildAsync(count);
        await using var _r = rig;
        Fill(vm);

        Assert.Equal(valid, vm.Validate().Count == 0);
    }

    [Fact]
    public async Task Stroke_indexes_must_use_1_to_n_once()
    {
        var (rig, vm) = await BuildAsync(9);
        await using var _r = rig;
        Fill(vm);
        vm.Holes[2].StrokeIndex = 1; // duplicates hole 1's

        var errors = vm.Validate();

        Assert.Contains(Msg(CustomGolfStats.Domain.Courses.CourseError.StrokeIndexRepeated), errors);
    }

    [Fact]
    public async Task The_messages_quote_the_domains_limits()
    {
        Assert.Contains("3 and 6", Msg(CustomGolfStats.Domain.Courses.CourseError.ParOutOfRange));
        Assert.Contains("0 and 1000", Msg(CustomGolfStats.Domain.Courses.CourseError.YardageOutOfRange));
        Assert.Contains("55 and 155", Msg(CustomGolfStats.Domain.Courses.CourseError.SlopeOutOfRange));
        Assert.Contains("1.0 and 99.9", Msg(CustomGolfStats.Domain.Courses.CourseError.RatingOutOfRange));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Changing_the_hole_count_keeps_what_was_typed()
    {
        var (rig, vm) = await BuildAsync(9);
        await using var _r = rig;
        vm.Holes[0].Yardage = 410;

        vm.SetHoleCountCommand.Execute(18);
        Assert.Equal(18, vm.Holes.Count);
        Assert.Equal(410, vm.Holes[0].Yardage);
        vm.SetHoleCountCommand.Execute(9);
        Assert.Equal(9, vm.Holes.Count);
    }

    [Fact]
    public async Task Saving_creates_a_pending_course_with_client_ids_and_opens_start_round()
    {
        var (rig, vm) = await BuildAsync(9);
        await using var _r = rig;
        Fill(vm);
        vm.City = " Springfield ";
        vm.Holes[3].Par = 3;
        vm.Holes[3].Yardage = 160;

        await vm.SaveCommand.ExecuteAsync(null);

        var pending = Assert.Single(await rig.Db.Courses.GetPendingAsync());
        Assert.NotEqual(Guid.Empty, pending.Course.Id);
        Assert.Equal(("My Home Track", "Springfield", Client.CourseSource.Manual, Client.CourseStatus.UserEdited), (pending.Course.Name, pending.Course.City, pending.Course.Source, pending.Course.Status));
        var tee = Assert.Single(pending.Tees);
        Assert.Equal(pending.Course.Id, tee.Tee.CourseId);
        var version = Assert.Single(tee.Versions);
        Assert.Equal((70.5, 125, 9), (version.Version.Rating, version.Version.Slope, version.Holes.Count));
        Assert.Equal(version.Holes.Sum(h => h.Par), version.Version.Par);
        Assert.Equal((3, 160), (version.Holes[3].Par, version.Holes[3].Yardage));
        Assert.Equal((Route.StartRound, new CourseArgs(pending.Course.Id)), rig.Nav.Last);
        Assert.Equal(version.Holes.Count, (await rig.Db.Courses.GetHoleInfosAsync(version.Version.Id)).Count);
    }
}

public class ProfileViewModelTests
{
    private static async Task<(VmRig Rig, ProfileViewModel Vm, StubHandler Stub, FakeConnectivity Net, FakeSecureStore Store)> BuildAsync(Func<Recorded, HttpResponseMessage> respond)
    {
        var rig = await VmRig.CreateAsync();
        var stub = new StubHandler(respond);
        var store = new FakeSecureStore();
        var session = ApiClientFactory.Create(StubHandler.BaseAddress, store, stub);
        var net = new FakeConnectivity();
        return (rig, new ProfileViewModel(rig.Db, session.Client, session.Tokens, net, rig.Temp.Clock, rig.Nav), stub, net, store);
    }

    private static readonly Guid Home = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static HttpResponseMessage Profile() => StubHandler.Json(HttpStatusCode.OK, new Client.ProfileResponse
    {
        Id = Guid.NewGuid(), Email = "a@b.c", FirstName = "Ada", LastName = "L", Gender = "Female", GhinNumber = "1234567", HomeCourseId = Home, Units = "Imperial",
    });

    [Fact]
    public async Task The_profile_loads_when_online_and_the_handicap_history_comes_from_the_device()
    {
        var (rig, vm, _, _, _) = await BuildAsync(_ => Profile());
        await using var _r = rig;
        await rig.Db.Handicap.UpsertAsync([new HandicapRow { Id = Guid.NewGuid(), EffectiveOn = new DateTime(2026, 4, 1), Index = 14.3 }]);

        await vm.LoadAsync();

        Assert.Equal(("a@b.c", "Ada", "1234567", "Imperial"), (vm.Email, vm.FirstName, vm.GhinNumber, vm.Units));
        Assert.Equal(14.3, vm.LatestHandicapIndex);
        Assert.Single(vm.HandicapHistory);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Offline_the_profile_says_so_but_the_handicap_history_still_shows()
    {
        var (rig, vm, stub, net, _) = await BuildAsync(_ => Profile());
        await using var _r = rig;
        net.IsOnline = false;
        await rig.Db.Handicap.UpsertAsync([new HandicapRow { Id = Guid.NewGuid(), EffectiveOn = new DateTime(2026, 4, 1), Index = 14.3 }]);

        await vm.LoadAsync();

        Assert.Equal(UiText.ProfileLoadFailed, vm.ErrorMessage);
        Assert.Empty(stub.Requests);
        Assert.Equal(14.3, vm.LatestHandicapIndex);
    }

    [Fact]
    public async Task Saving_sends_the_whole_profile_because_PUT_me_replaces_it()
    {
        var (rig, vm, stub, _, _) = await BuildAsync(r => r.Method.Method == "PUT" ? Profile() : Profile());
        await using var _r = rig;
        await vm.LoadAsync();
        vm.FirstName = "Adeline";

        await vm.SaveProfileCommand.ExecuteAsync(null);

        var put = stub.Requests.Last(r => r.Method.Method == "PUT");
        using var body = JsonDocument.Parse(put.Body!);
        Assert.Equal("Adeline", body.RootElement.GetProperty("firstName").GetString());
        Assert.Equal("L", body.RootElement.GetProperty("lastName").GetString());
        Assert.Equal("Female", body.RootElement.GetProperty("gender").GetString());
        Assert.Equal(Home, body.RootElement.GetProperty("homeCourseId").GetGuid());
        Assert.Equal("Imperial", body.RootElement.GetProperty("units").GetString());
        Assert.True(vm.IsSaved);
    }

    [Fact]
    public async Task Saving_is_not_possible_until_the_profile_has_loaded()
    {
        var (rig, vm, stub, net, _) = await BuildAsync(_ => Profile());
        await using var _r = rig;
        net.IsOnline = false;

        await vm.LoadAsync();
        Assert.False(vm.IsLoaded);
        Assert.False(vm.SaveProfileCommand.CanExecute(null));
        await vm.SaveProfileCommand.ExecuteAsync(null);
        Assert.Empty(stub.Requests);                 // nothing blanked on the server

        net.IsOnline = true;
        await vm.LoadAsync();
        Assert.True(vm.IsLoaded);
        Assert.True(vm.SaveProfileCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_failed_load_leaves_saving_disabled()
    {
        var (rig, vm, _, _, _) = await BuildAsync(_ => StubHandler.Status(HttpStatusCode.InternalServerError));
        await using var _r = rig;

        await vm.LoadAsync();

        Assert.False(vm.IsLoaded);
        Assert.Equal(UiText.ProfileLoadFailed, vm.ErrorMessage);
        Assert.False(vm.SaveProfileCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_failed_save_reports_it()
    {
        var (rig, vm, _, _, _) = await BuildAsync(r => r.Method.Method == "PUT" ? StubHandler.Status(HttpStatusCode.InternalServerError) : Profile());
        await using var _r = rig;
        await vm.LoadAsync();

        await vm.SaveProfileCommand.ExecuteAsync(null);

        Assert.Equal(UiText.ProfileSaveFailed, vm.ErrorMessage);
        Assert.False(vm.IsSaved);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(60.0)]
    [InlineData(-11.0)]
    public async Task A_handicap_outside_the_valid_range_is_refused(double? index)
    {
        var (rig, vm, stub, _, _) = await BuildAsync(_ => Profile());
        await using var _r = rig;
        vm.NewHandicapIndex = index;

        await vm.AddHandicapCommand.ExecuteAsync(null);

        Assert.Contains(UiText.HandicapInvalid, vm.HandicapErrors);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Adding_a_handicap_posts_it_and_keeps_the_entry_on_the_device()
    {
        var id = Guid.NewGuid();
        var (rig, vm, stub, _, _) = await BuildAsync(r => r.Path == "/me/handicap"
            ? StubHandler.Json(HttpStatusCode.Created, new Client.HandicapEntryResponse { Id = id, EffectiveOn = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), Index = 12.8 })
            : Profile());
        await using var _r = rig;
        await vm.LoadAsync();
        vm.NewHandicapIndex = 12.8;

        await vm.AddHandicapCommand.ExecuteAsync(null);

        Assert.Contains("12.8", stub.Requests.Last().Body);
        Assert.Contains("\"2026-06-01\"", stub.Requests.Last().Body);
        Assert.Equal(12.8, vm.LatestHandicapIndex);
        Assert.Null(vm.NewHandicapIndex);
        Assert.Empty(vm.HandicapErrors);
        Assert.Equal(12.8, (await rig.Db.Handicap.LatestAsync())!.Index);
    }

    [Fact]
    public async Task Adding_a_handicap_offline_asks_for_a_connection()
    {
        var (rig, vm, stub, net, _) = await BuildAsync(_ => Profile());
        await using var _r = rig;
        net.IsOnline = false;
        vm.NewHandicapIndex = 10;

        await vm.AddHandicapCommand.ExecuteAsync(null);

        Assert.Contains(UiText.NeedConnection, vm.HandicapErrors);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Signing_out_removes_the_tokens_keeps_the_rounds_and_returns_to_sign_in()
    {
        var (rig, vm, _, _, store) = await BuildAsync(_ => Profile());
        await using var _r = rig;
        store.Values[TokenService.AccessTokenKey] = "a";
        store.Values[TokenService.RefreshTokenKey] = "r";
        await rig.Db.Rounds.SaveAsync(Samples.FullShotRound());

        await vm.SignOutCommand.ExecuteAsync(null);

        Assert.Empty(store.Values);
        Assert.Equal(Route.SignIn, rig.Nav.Last.Route);
        Assert.Single(await rig.Db.Rounds.ListAsync());
    }
}
