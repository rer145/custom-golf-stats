using System.Net;
using System.Text.Json;
using CustomGolfStats.App.Core.ApiAccess;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.App.Core.Sync;
using CustomGolfStats.App.Core.Tests.Support;
using CustomGolfStats.App.Core.ViewModels;

namespace CustomGolfStats.App.Core.Tests.ViewModels;

/// <summary>Profile → Delete account: sync stops before the request, the device is cleared only once the server deleted the account,
/// and a refused deletion changes nothing.</summary>
public sealed class AccountDeletionViewModelTests
{
    private sealed class Rig(TempDb temp, StubHandler stub, FakeSecureStore store, SyncEngine engine, ProfileViewModel vm, FakeNavigator nav,
        FakeConnectivity net, TaskCompletionSource<HttpResponseMessage> firstPut, TaskCompletionSource firstPutStarted) : IAsyncDisposable
    {
        public TempDb Temp => temp;
        public LocalDb Db => temp.Db;
        public StubHandler Stub => stub;
        public FakeSecureStore Store => store;
        public SyncEngine Engine => engine;
        public ProfileViewModel Vm => vm;
        public FakeNavigator Nav => nav;
        public FakeConnectivity Net => net;
        public TaskCompletionSource<HttpResponseMessage> FirstPut => firstPut;
        public Task FirstPutStarted => firstPutStarted.Task;

        public Task<int> CountAsync(string table) => Db.Connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {table}");

        public ValueTask DisposeAsync() => temp.DisposeAsync();
    }

    /// <summary>Signed in and verified as ada, with two unsynced rounds, a handicap entry and the sample course. DELETE /me answers
    /// <paramref name="deleteStatus"/>; the first PUT (a sync push) waits until released and honours cancellation.</summary>
    private static async Task<Rig> CreateAsync(HttpStatusCode deleteStatus = HttpStatusCode.NoContent)
    {
        var temp = await TempDb.CreateAsync();
        await temp.Db.Courses.SaveFromServerAsync(Samples.Course());
        await temp.Db.Rounds.SaveAsync(Samples.FullShotRound());
        await temp.Db.Rounds.SaveAsync(Samples.FullShotRound());
        await temp.Db.Handicap.UpsertAsync([new HandicapRow { Id = Guid.NewGuid(), EffectiveOn = new DateTime(2026, 4, 1), Index = 9.9 }]);

        var firstPut = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var puts = 0;
        var stub = new StubHandler(async r =>
        {
            if (r.Method == HttpMethod.Delete && r.Path == "/me")
                return deleteStatus == HttpStatusCode.BadRequest
                    ? StubHandler.Json(HttpStatusCode.BadRequest, new { title = "Bad", status = 400, errors = new { password = new[] { "The password is incorrect." } } })
                    : StubHandler.Status(deleteStatus);
            if (r.Path == "/auth/refresh")
                return StubHandler.Status(HttpStatusCode.Unauthorized); // the account is gone: no new tokens
            if (r.Method == HttpMethod.Put && Interlocked.Increment(ref puts) == 1)
            {
                started.TrySetResult();
                return await firstPut.Task;
            }
            return r.Path == "/sync/changes"
                ? StubHandler.Json(HttpStatusCode.OK, Payloads.Changes(DateTimeOffset.UnixEpoch.AddDays(5)))
                : StubHandler.Json(HttpStatusCode.OK, Payloads.Round(Guid.NewGuid(), 1));
        });
        var store = new FakeSecureStore();
        store.Values[TokenService.AccessTokenKey] = "ada-access";
        store.Values[TokenService.RefreshTokenKey] = "ada-refresh";
        var session = ApiClientFactory.Create(StubHandler.BaseAddress, store, new CancellableHandler(stub));
        var engine = new SyncEngine(temp.Db, session.Client);
        var accounts = new AccountSwitchService(temp.Db, engine);
        await accounts.CheckAsync("ada@example.com");
        var nav = new FakeNavigator();
        var net = new FakeConnectivity();
        var vm = new ProfileViewModel(temp.Db, session.Client, session.Tokens, engine, accounts, net, temp.Clock, nav);
        return new Rig(temp, stub, store, engine, vm, nav, net, firstPut, started);
    }

    /// <summary>Like a real socket handler: a cancelled request stops waiting.</summary>
    private sealed class CancellableHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            await base.SendAsync(request, cancellationToken).WaitAsync(cancellationToken);
    }

    [Fact]
    public async Task Deleting_stops_a_running_sync_first_then_clears_the_device_and_signs_out()
    {
        await using var rig = await CreateAsync();
        var sync = rig.Engine.SyncAsync(TestContext.Current.CancellationToken);
        await rig.FirstPutStarted;

        rig.Vm.DeletePassword = "Fairway-123";
        await rig.Vm.DeleteAccountCommand.ExecuteAsync(null);

        Assert.True(sync.IsCompleted);
        Assert.True((await sync).SignedOut);
        // The push in flight was the only sync request; the DELETE came after it, with the password, and nothing followed.
        Assert.Equal(["PUT", "DELETE"], rig.Stub.Requests.Select(r => r.Method.Method));
        var delete = rig.Stub.Requests[^1];
        Assert.Equal("/me", delete.Path);
        Assert.Equal("ada-access", delete.Bearer);
        Assert.Equal("Fairway-123", JsonDocument.Parse(delete.Body!).RootElement.GetProperty("password").GetString());

        foreach (var table in new[] { "RoundRow", "HoleRow", "ShotRow", "CourseRow", "TeeRow", "TeeVersionRow", "TeeHoleRow", "HandicapRow", "EntitlementsRow", "ShotDraftRow", "SyncStateRow" })
            Assert.Equal(0, await rig.CountAsync(table));
        Assert.Empty(rig.Store.Values);
        Assert.False(await AccountSwitchService.IsSyncAllowedAsync(rig.Db));
        Assert.Equal(Route.SignIn, rig.Nav.Last.Route);
        Assert.Null(rig.Vm.DeleteError);
        Assert.Equal("", rig.Vm.DeletePassword);

        // A later sync (a timer, the app resuming) sends nothing.
        var after = await rig.Engine.SyncAsync(TestContext.Current.CancellationToken);
        Assert.True(after.SignedOut);
        Assert.Equal(2, rig.Stub.Requests.Count);
    }

    [Fact]
    public async Task A_wrong_password_deletes_nothing_and_lets_sync_resume()
    {
        await using var rig = await CreateAsync(HttpStatusCode.BadRequest);

        rig.Vm.DeletePassword = "wrong";
        await rig.Vm.DeleteAccountCommand.ExecuteAsync(null);

        Assert.Equal(UiText.DeleteAccountWrongPassword, rig.Vm.DeleteError);
        Assert.Equal("", rig.Vm.DeletePassword);
        Assert.Equal(2, await rig.Db.Rounds.CountDirtyAsync());
        Assert.Equal(1, await rig.CountAsync("HandicapRow"));
        Assert.Equal("ada-refresh", rig.Store.Values[TokenService.RefreshTokenKey]);
        Assert.True(await AccountSwitchService.IsSyncAllowedAsync(rig.Db));
        Assert.Empty(rig.Nav.Visits);
        Assert.False(rig.Vm.IsBusy);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, UiText.DeleteAccountLastAdmin)]
    [InlineData(HttpStatusCode.TooManyRequests, UiText.DeleteAccountLockedOut)]
    [InlineData(HttpStatusCode.InternalServerError, UiText.DeleteAccountFailed)]
    public async Task A_refused_or_failed_deletion_keeps_everything(HttpStatusCode status, string message)
    {
        await using var rig = await CreateAsync(status);

        rig.Vm.DeletePassword = "Fairway-123";
        await rig.Vm.DeleteAccountCommand.ExecuteAsync(null);

        Assert.Equal(message, rig.Vm.DeleteError);
        Assert.Equal(2, await rig.Db.Rounds.CountDirtyAsync());
        Assert.NotEmpty(rig.Store.Values);
        Assert.True(await AccountSwitchService.IsSyncAllowedAsync(rig.Db));
        Assert.Empty(rig.Nav.Visits);
    }

    [Fact]
    public async Task An_ended_session_goes_to_sign_in_and_keeps_the_rounds_on_this_device()
    {
        await using var rig = await CreateAsync(HttpStatusCode.Unauthorized);

        rig.Vm.DeletePassword = "Fairway-123";
        await rig.Vm.DeleteAccountCommand.ExecuteAsync(null);

        Assert.Contains(rig.Stub.Requests, r => r.Path == "/auth/refresh");
        Assert.Equal(UiText.DeleteAccountSessionEnded, rig.Vm.DeleteError);
        Assert.Empty(rig.Store.Values);
        Assert.Equal(Route.SignIn, rig.Nav.Last.Route);
        Assert.Equal(2, await rig.Db.Rounds.CountDirtyAsync());
        Assert.Null(await rig.Db.SyncState.GetAsync(AccountSwitchService.PendingKey)); // marker lifted
        Assert.False(rig.Vm.IsBusy);
    }

    [Fact]
    public async Task An_account_already_deleted_elsewhere_is_cleared_from_the_device_too()
    {
        await using var rig = await CreateAsync(HttpStatusCode.NotFound);

        rig.Vm.DeletePassword = "Fairway-123";
        await rig.Vm.DeleteAccountCommand.ExecuteAsync(null);

        Assert.Equal(0, await rig.CountAsync("RoundRow"));
        Assert.Empty(rig.Store.Values);
        Assert.Equal(Route.SignIn, rig.Nav.Last.Route);
    }

    [Fact]
    public async Task No_request_is_sent_without_a_password_or_a_connection()
    {
        await using var rig = await CreateAsync();

        await rig.Vm.DeleteAccountCommand.ExecuteAsync(null);
        Assert.Equal(UiText.DeleteAccountPasswordMissing, rig.Vm.DeleteError);

        rig.Net.IsOnline = false;
        rig.Vm.DeletePassword = "Fairway-123";
        await rig.Vm.DeleteAccountCommand.ExecuteAsync(null);
        Assert.Equal(UiText.NeedConnection, rig.Vm.DeleteError);

        Assert.Empty(rig.Stub.Requests);
        Assert.Equal(2, await rig.Db.Rounds.CountDirtyAsync());
        Assert.True(await AccountSwitchService.IsSyncAllowedAsync(rig.Db));
    }

    [Fact]
    public async Task The_warning_says_it_is_permanent_and_counts_what_only_this_device_holds()
    {
        await using var rig = await CreateAsync();

        var warning = await rig.Vm.DescribeAccountDeletionAsync();

        Assert.Contains("cannot be undone", warning);
        Assert.Contains("2 rounds that never synced", warning);
        Assert.DoesNotContain("never synced", UiText.DeleteAccountWarning(0, 0, 0));
        Assert.Contains("1 round, 1 course and 3 holes being entered", UiText.DeleteAccountWarning(1, 1, 3));
    }
}
