using System.Net;
using CustomGolfStats.App.Core.ApiAccess;
using CustomGolfStats.App.Core.Platform;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Storage.Repositories;
using CustomGolfStats.App.Core.Sync;
using CustomGolfStats.App.Core.Tests.Support;
using CustomGolfStats.App.Core.Tests.Sync;
using CustomGolfStats.App.Core.ViewModels;
using CustomGolfStats.Domain;
using Client = CustomGolfStats.Api.Client;

namespace CustomGolfStats.App.Core.Tests.ViewModels;

/// <summary>Carry-ins from the app-core final review, fixed in phase 5 task 4.</summary>
public class CarryInTests
{
    private static HttpResponseMessage Profile(string email) =>
        StubHandler.Json(HttpStatusCode.OK, new Client.ProfileResponse { Id = Guid.NewGuid(), Email = email, FirstName = "A", LastName = "B", Gender = "Male", Units = "Imperial" });

    private static SignInViewModel SignInVm(TempDb t, ISecureStore store, StubHandler stub, FakeNavigator nav)
    {
        var session = ApiClientFactory.Create(StubHandler.BaseAddress, store, stub);
        return new SignInViewModel(session.Tokens, TestAccounts.For(t.Db), session.Client, nav);
    }

    private static async Task SignInAsAsync(TempDb t, string email)
    {
        var stub = new StubHandler(r => r.Path == "/me" ? Profile(email) : StubHandler.Json(HttpStatusCode.OK, Payloads.Tokens("a", "r")));
        var vm = SignInVm(t, new FakeSecureStore(), stub, new FakeNavigator());
        vm.Email = email;
        vm.Password = "pw";
        await vm.SubmitCommand.ExecuteAsync(null);
    }

    // ---- 1: tokens saved before the account check must never let sync run ----

    [Fact]
    public async Task While_a_new_sign_in_is_saved_but_not_yet_checked_sync_refuses_and_a_restart_undoes_the_sign_in()
    {
        await using var t = await TempDb.CreateAsync();
        await SignInAsAsync(t, "ada@example.com");
        await t.Db.Rounds.SaveAsync(Samples.FullShotRound()); // ada's unsynced round

        // Bob signs in; the process dies while /me is in flight (tokens already stored, account not yet checked).
        var meStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource<HttpResponseMessage>();
        var stub = new StubHandler(async r =>
        {
            if (r.Path != "/me")
                return StubHandler.Json(HttpStatusCode.OK, Payloads.Tokens("bob-a", "bob-r"));
            meStarted.TrySetResult();
            return await never.Task;
        });
        var store = new FakeSecureStore();
        var vm = SignInVm(t, store, stub, new FakeNavigator());
        vm.Email = "bob@example.com";
        vm.Password = "pw";
        _ = vm.SubmitCommand.ExecuteAsync(null);
        await meStarted.Task;
        Assert.NotEmpty(store.Values); // bob's tokens are stored

        var syncStub = new StubHandler(_ => StubHandler.Status(HttpStatusCode.OK));
        var result = await new SyncEngine(t.Db, syncStub.Client()).SyncAsync(TestContext.Current.CancellationToken);
        Assert.True(result.SignedOut);
        Assert.Empty(syncStub.Requests);

        // Restart with bob's tokens still in the store.
        await t.ReopenAsync();
        var nav = new FakeNavigator();
        var reopened = SignInVm(t, store, new StubHandler(r => Profile("bob@example.com")), nav);
        await reopened.InitializeAsync();

        Assert.Empty(store.Values);                                   // the unchecked session is undone
        Assert.DoesNotContain(nav.Visits, v => v.Route == Route.Home);
        Assert.Null(reopened.PendingSwitch);
        Assert.Single(await t.Db.Rounds.ListAsync());                 // ada's round is kept
    }

    [Fact]
    public async Task A_failure_after_the_tokens_were_saved_signs_out_and_keeps_sync_blocked()
    {
        await using var t = await TempDb.CreateAsync();
        await SignInAsAsync(t, "ada@example.com");
        await t.Db.Rounds.SaveAsync(Samples.FullShotRound());
        var store = new FakeSecureStore();
        var stub = new StubHandler(async r =>
        {
            if (r.Path != "/me")
                return StubHandler.Json(HttpStatusCode.OK, Payloads.Tokens("bob-a", "bob-r"));
            // The account check that follows cannot use the sync state.
            await t.Db.Connection.ExecuteAsync("ALTER TABLE SyncStateRow RENAME TO SyncStateRowAway");
            return Profile("bob@example.com");
        });
        var nav = new FakeNavigator();
        var vm = SignInVm(t, store, stub, nav);
        vm.Email = "bob@example.com";
        vm.Password = "pw";

        await vm.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(UiText.SignInError, vm.ErrorMessage);
        Assert.Empty(store.Values);
        Assert.Empty(nav.Visits);
        await t.Db.Connection.ExecuteAsync("ALTER TABLE SyncStateRowAway RENAME TO SyncStateRow");
        await t.ReopenAsync();
        var syncStub = new StubHandler(_ => StubHandler.Status(HttpStatusCode.OK));
        Assert.True((await new SyncEngine(t.Db, syncStub.Client()).SyncAsync(TestContext.Current.CancellationToken)).SignedOut);
        Assert.Empty(syncStub.Requests);
    }

    [Fact]
    public async Task A_device_where_no_account_was_ever_verified_does_not_sync()
    {
        await using var t = await TempDb.CreateAsync();
        await t.Db.Rounds.SaveAsync(Samples.FullShotRound());
        var stub = new StubHandler(_ => StubHandler.Status(HttpStatusCode.OK));

        var result = await new SyncEngine(t.Db, stub.Client()).SyncAsync(TestContext.Current.CancellationToken);

        Assert.True(result.SignedOut);
        Assert.Empty(stub.Requests);
    }

    // ---- 2: course search never throws at the UI ----

    /// <summary>A UI-thread stand-in: continuations queue up and run one at a time on the calling thread.</summary>
    private sealed class QueueContext : SynchronizationContext
    {
        public System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Callback, object? State)> Queue { get; } = [];

        public override void Post(SendOrPostCallback d, object? state) => Queue.Add((d, state));

        public static void Run(Func<QueueContext, Task> body)
        {
            var ctx = new QueueContext();
            var previous = Current;
            SetSynchronizationContext(ctx);
            try
            {
                var task = body(ctx);
                task.ContinueWith(_ => ctx.Queue.CompleteAdding(), TaskScheduler.Default);
                foreach (var (callback, state) in ctx.Queue.GetConsumingEnumerable())
                    callback(state);
                task.GetAwaiter().GetResult();
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }
    }

    private sealed class OffContextHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.Run(() => base.SendAsync(request, cancellationToken), CancellationToken.None);
    }

    [Fact]
    public async Task A_search_whose_request_failed_just_before_a_newer_search_started_does_not_throw_or_show_a_message()
    {
        await using var rig = await VmRig.CreateAsync();
        var fail = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stub = new StubHandler(async r =>
        {
            if (r.Path == "/courses" && r.PathAndQuery.Contains("old"))
            {
                oldStarted.TrySetResult();
                await fail.Task;
                throw new HttpRequestException("connection reset");
            }
            if (r.Path == "/courses")
                return StubHandler.Json(HttpStatusCode.OK, new List<Client.CourseSummaryDto>());
            return StubHandler.Json(HttpStatusCode.OK, new List<Client.ProviderSearchResultDto>());
        });
        // Like SocketsHttpHandler, the network work completes off the UI thread.
        var client = new Client.CustomGolfStatsApiClient(new HttpClient(new OffContextHandler(stub)) { BaseAddress = StubHandler.BaseAddress });
        var vm = new CoursePickerViewModel(rig.Db, client, new FakeConnectivity(), rig.Temp.Clock, rig.Nav);
        Exception? escaped = null;

        // On a UI thread the failed request's continuation is queued; the golfer's newer search can run before it.
        QueueContext.Run(async ctx =>
        {
            vm.Query = "old query";
            var first = vm.SearchCommand.ExecuteAsync(null);
            await oldStarted.Task;
            fail.SetResult();
            Assert.True(SpinWait.SpinUntil(() => ctx.Queue.Count > 0, TimeSpan.FromSeconds(10))); // the failure is queued, not yet handled
            vm.Query = "new query";
            var second = vm.SearchCommand.ExecuteAsync(null);
            try { await first; } catch (Exception ex) { escaped = ex; }
            await second;
        });

        Assert.Null(escaped);
        Assert.Null(vm.Message);
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.IsSearchingOnline);
    }

    [Fact]
    public async Task A_local_search_or_favorite_toggle_that_fails_shows_an_error_instead_of_throwing()
    {
        var rig = await VmRig.CreateAsync();
        var vm = new CoursePickerViewModel(rig.Db, new StubHandler(_ => StubHandler.Status(HttpStatusCode.NotFound)).Client(), new FakeConnectivity(false), rig.Temp.Clock, rig.Nav);
        await vm.LoadAsync();
        var course = Assert.Single(vm.LocalResults);
        await rig.DisposeAsync(); // the database is gone

        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Equal(UiText.SomethingWentWrong, vm.ErrorMessage);

        vm.ErrorMessage = null;
        await vm.ToggleFavoriteCommand.ExecuteAsync(course);
        Assert.Equal(UiText.SomethingWentWrong, vm.ErrorMessage);
    }

    // ---- 3: a pulled record that cannot be stored is isolated like one that cannot be mapped ----

    [Fact]
    public async Task A_pulled_record_that_cannot_be_stored_is_skipped_reported_and_the_cursor_still_advances()
    {
        await using var rig = await SyncRig.CreateAsync();
        var goodId = Guid.NewGuid();
        var badId = Guid.NewGuid();
        var bad = Payloads.Round(badId, 1);
        bad.Holes = [new Client.HoleResultDto { HoleNumber = 1, EntrySequence = 1, Par = 4, Score = 5, Putts = 2 },
                     new Client.HoleResultDto { HoleNumber = 2, EntrySequence = 1, Par = 4, Score = 4, Putts = 2 }]; // duplicate sequence: the insert fails
        var badCourse = Payloads.Course();
        badCourse.Id = Guid.NewGuid();
        badCourse.Tees = [new Client.SyncTeeDto { Id = Guid.NewGuid(), Name = "Red", Gender = Client.Gender.Female, Versions = [new Client.SyncTeeVersionDto
        {
            Id = Guid.NewGuid(), EffectiveFrom = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), Par = 8, Rating = 70, Slope = 120,
            Holes = [new Client.TeeHoleDto { Number = 1, Par = 4, Yardage = 400, StrokeIndex = 1 },
                     new Client.TeeHoleDto { Number = 1, Par = 4, Yardage = 400, StrokeIndex = 2 }], // duplicate hole number: the insert fails
        }] }];
        var changes = Payloads.Changes(SyncRig.Cursor1, bad, Payloads.Round(goodId, 2));
        changes.Courses = [badCourse];
        rig.Changes = _ => StubHandler.Json(HttpStatusCode.OK, changes);

        var result = await rig.SyncAsync();

        Assert.True(result.PullCompleted);
        Assert.Equal(2, result.Failures.Count);
        Assert.Contains(result.Failures, f => f.Contains(badId.ToString()));
        Assert.Contains(result.Failures, f => f.Contains(badCourse.Id.ToString()));
        Assert.Equal(1, result.PulledRounds);
        Assert.NotNull(await rig.Temp.Db.Rounds.GetAsync(goodId));
        Assert.Null(await rig.Temp.Db.Rounds.GetAsync(badId));
        Assert.Equal(SyncRig.Cursor1, await rig.Temp.Db.SyncState.GetNextSinceAsync());
    }

    // ---- 4: shot drafts are unsynced work ----

    [Fact]
    public async Task A_shot_draft_is_unsynced_work_so_a_different_account_is_asked_first()
    {
        await using var t = await TempDb.CreateAsync();
        await SignInAsAsync(t, "ada@example.com");
        var round = Samples.Synced(Samples.FullShotRound());
        await t.Db.Courses.SaveFromServerAsync(Samples.Course());
        await t.Db.Rounds.SaveFromServerAsync(round, ServerWriteMode.Pull);
        await t.Db.Drafts.SaveAsync(round.Round.Id, 9, 3, Samples.Par4Shots());

        var stub = new StubHandler(r => r.Path == "/me" ? Profile("bob@example.com") : StubHandler.Json(HttpStatusCode.OK, Payloads.Tokens("a", "r")));
        var vm = SignInVm(t, new FakeSecureStore(), stub, new FakeNavigator());
        vm.Email = "bob@example.com";
        vm.Password = "pw";
        await vm.SubmitCommand.ExecuteAsync(null);

        var prompt = Assert.IsType<AccountSwitchPrompt>(vm.PendingSwitch);
        Assert.Equal(1, prompt.UnsavedHoles);
        Assert.Contains("1 hole", vm.PendingSwitchMessage);
        Assert.NotNull(await t.Db.Drafts.GetAsync(round.Round.Id, 9)); // not silently cleared
    }

    // ---- 5: a huge rating is out of range, not an exception ----

    [Theory]
    [InlineData(1e30)]
    [InlineData(-1e30)]
    public async Task A_rating_beyond_the_decimal_range_is_reported_as_out_of_range(double rating)
    {
        await using var rig = await VmRig.CreateAsync();
        var vm = new ManualCourseViewModel(rig.Db, rig.Temp.Clock, rig.Nav);
        vm.Initialize(9);
        vm.Name = "Big";
        vm.Rating = rating;
        vm.Slope = 113;

        var errors = vm.Validate();

        Assert.Contains(UiText.Describe(Domain.Courses.CourseError.RatingOutOfRange), errors);
    }

    // ---- 6: deleting a hole clears its draft ----

    [Fact]
    public async Task Deleting_a_hole_from_the_round_clears_its_draft()
    {
        await using var rig = await VmRig.CreateAsync();
        var roundId = await rig.CreateRoundAsync(EntryMode.Quick);
        var quick = new QuickHoleViewModel(rig.Db, rig.Nav);
        await quick.LoadAsync(new HoleArgs(roundId, HoleNumber: 1));
        await quick.SaveCommand.ExecuteAsync(null);
        await rig.Db.Drafts.SaveAsync(roundId, 1, 1, Samples.Par4Shots()); // an edit of that hole in progress
        var vm = new RoundViewModel(rig.Db, rig.Temp.Clock, rig.Nav, new FakePedometer());
        await vm.LoadAsync(new RoundArgs(roundId));

        await vm.DeleteHoleCommand.ExecuteAsync(vm.Holes[0]);

        Assert.Empty(vm.Holes);
        Assert.Null(await rig.Db.Drafts.GetAsync(roundId, 1));
    }

    // ---- 7: cancel-switch and start-round failure reporting ----

    private sealed class FailingRemoveStore : ISecureStore
    {
        public FakeSecureStore Inner { get; } = new();
        public bool FailRemove { get; set; }
        public Task<string?> GetAsync(string key) => Inner.GetAsync(key);
        public Task SetAsync(string key, string value) => Inner.SetAsync(key, value);
        public Task RemoveAsync(string key) => FailRemove ? throw new IOException("keychain unavailable") : Inner.RemoveAsync(key);
    }

    [Fact]
    public async Task Cancelling_an_account_switch_whose_sign_out_fails_keeps_the_question_open()
    {
        await using var t = await TempDb.CreateAsync();
        await SignInAsAsync(t, "ada@example.com");
        await t.Db.Rounds.SaveAsync(Samples.FullShotRound());
        var store = new FailingRemoveStore();
        var stub = new StubHandler(r => r.Path == "/me" ? Profile("bob@example.com") : StubHandler.Json(HttpStatusCode.OK, Payloads.Tokens("a", "r")));
        var vm = SignInVm(t, store, stub, new FakeNavigator());
        vm.Email = "bob@example.com";
        vm.Password = "pw";
        await vm.SubmitCommand.ExecuteAsync(null);
        Assert.NotNull(vm.PendingSwitch);

        store.FailRemove = true;
        await vm.CancelAccountSwitchCommand.ExecuteAsync(null);

        Assert.NotNull(vm.PendingSwitch);
        Assert.Equal(UiText.SomethingWentWrong, vm.ErrorMessage);
        Assert.NotNull(await TestAccounts.For(t.Db).GetPendingAsync());
    }

    private sealed class FailingNavigator : INavigator
    {
        public Task NavigateAsync(Route route, object? parameter = null) => throw new InvalidOperationException("no frame");
        public Task GoBackAsync() => Task.CompletedTask;
    }

    [Fact]
    public async Task A_round_that_started_but_could_not_be_opened_says_so_and_keeps_the_round()
    {
        await using var rig = await VmRig.CreateAsync();
        var vm = new StartRoundViewModel(rig.Db, rig.Temp.Clock, new FailingNavigator(), new FakeConnectivity(false), new FakeLocation(),
            new StubHandler(_ => StubHandler.Status(HttpStatusCode.NotFound)).Client());
        await vm.LoadAsync(new CourseArgs(Samples.CourseId));

        await vm.StartCommand.ExecuteAsync(null);

        Assert.Equal(UiText.CouldNotOpenRound, vm.ErrorMessage);
        Assert.Single(await rig.Db.Rounds.ListAsync());
    }
}
