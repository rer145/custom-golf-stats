using System.Net;
using CustomGolfStats.App.Core.ApiAccess;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.App.Core.Sync;
using CustomGolfStats.App.Core.Tests.Support;
using CustomGolfStats.App.Core.ViewModels;
using Client = CustomGolfStats.Api.Client;
using Dom = CustomGolfStats.Domain;

namespace CustomGolfStats.App.Core.Tests.ViewModels;

public class AccountSwitchTests
{
    private sealed record Device(SignInViewModel Vm, FakeNavigator Nav, FakeSecureStore Store, StubHandler Stub, TempDb Temp);

    /// <summary>A sign-in screen on the given database whose server says the signed-in account has this email.</summary>
    private static Device SignInScreen(TempDb temp, string serverEmail)
    {
        var stub = new StubHandler(r => r.Path == "/me"
            ? StubHandler.Json(HttpStatusCode.OK, new Client.ProfileResponse { Id = Guid.NewGuid(), Email = serverEmail, FirstName = "A", LastName = "B", Gender = "Male", Units = "Imperial" })
            : StubHandler.Json(HttpStatusCode.OK, Payloads.Tokens("acc-" + serverEmail, "ref-" + serverEmail)));
        var store = new FakeSecureStore();
        var session = ApiClientFactory.Create(StubHandler.BaseAddress, store, stub);
        var nav = new FakeNavigator();
        return new Device(new SignInViewModel(session.Tokens, TestAccounts.For(temp.Db), session.Client, nav), nav, store, stub, temp);
    }

    private static async Task SignInAsync(Device d, string email)
    {
        d.Vm.Email = email;
        d.Vm.Password = "pw";
        await d.Vm.SubmitCommand.ExecuteAsync(null);
    }

    private static async Task SeedAccountDataAsync(TempDb t)
    {
        await t.Db.Courses.SaveFromServerAsync(Samples.Course());
        await t.Db.Handicap.UpsertAsync([new HandicapRow { Id = Guid.NewGuid(), EffectiveOn = new DateTime(2026, 4, 1), Index = 10 }]);
        await t.Db.Entitlements.SaveAsync(new EntitlementsRow { Tier = Dom.Tier.Free, ShotByShotEntry = true, TrialShotRoundsRemaining = 2 });
        await t.Db.SyncState.SetNextSinceAsync(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
    }

    private static async Task AssertEmptyAsync(TempDb t)
    {
        Assert.Empty(await t.Db.Rounds.ListAsync());
        Assert.Equal(0, await t.Db.Connection.Table<CourseRow>().CountAsync());
        Assert.Equal(0, await t.Db.Connection.Table<TeeHoleRow>().CountAsync());
        Assert.Empty(await t.Db.Handicap.ListAsync());
        Assert.Null(await t.Db.Entitlements.GetAsync());
        Assert.Null(await t.Db.SyncState.GetNextSinceAsync());
    }

    [Fact]
    public async Task The_same_account_signing_in_again_keeps_everything_even_with_unsynced_rounds()
    {
        await using var t = await TempDb.CreateAsync();
        await SignInAsync(SignInScreen(t, "ada@example.com"), "ada@example.com");
        await SeedAccountDataAsync(t);
        await t.Db.Rounds.SaveAsync(Samples.FullShotRound()); // unsynced

        var again = SignInScreen(t, "ADA@Example.com "); // same account, different spelling
        await SignInAsync(again, "ada@example.com");

        Assert.Equal(Route.Home, again.Nav.Last.Route);
        Assert.Null(again.Vm.PendingSwitch);
        Assert.Single(await t.Db.Rounds.ListAsync());
        Assert.NotNull(await t.Db.Entitlements.GetAsync());
    }

    [Fact]
    public async Task The_first_ever_sign_in_adopts_rounds_made_before_signing_in()
    {
        await using var t = await TempDb.CreateAsync();
        await t.Db.Rounds.SaveAsync(Samples.FullShotRound()); // logged offline before an account existed

        var screen = SignInScreen(t, "ada@example.com");
        await SignInAsync(screen, "ada@example.com");

        Assert.Equal(Route.Home, screen.Nav.Last.Route);
        Assert.Single(await t.Db.Rounds.ListAsync());
    }

    [Fact]
    public async Task A_different_account_with_nothing_unsynced_gets_a_clean_device()
    {
        await using var t = await TempDb.CreateAsync();
        await SignInAsync(SignInScreen(t, "ada@example.com"), "ada@example.com");
        await SeedAccountDataAsync(t);
        await t.Db.Rounds.SaveFromServerAsync(Samples.Synced(Samples.FullShotRound()), Storage.Repositories.ServerWriteMode.Pull);

        var other = SignInScreen(t, "bob@example.com");
        await SignInAsync(other, "bob@example.com");

        Assert.Equal(Route.Home, other.Nav.Last.Route);
        Assert.Null(other.Vm.PendingSwitch);
        await AssertEmptyAsync(t);
    }

    [Fact]
    public async Task A_different_account_with_unsynced_rounds_must_confirm_and_nothing_is_cleared_or_synced_meanwhile()
    {
        await using var t = await TempDb.CreateAsync();
        await SignInAsync(SignInScreen(t, "ada@example.com"), "ada@example.com");
        await SeedAccountDataAsync(t);
        await t.Db.Rounds.SaveAsync(Samples.FullShotRound());
        await t.Db.Courses.SaveLocalAsync(Samples.Course("My Track"));

        var other = SignInScreen(t, "bob@example.com");
        await SignInAsync(other, "bob@example.com");

        var prompt = other.Vm.PendingSwitch!;
        Assert.Equal(1, prompt.UnsyncedRounds);
        Assert.Contains("1 round", other.Vm.PendingSwitchMessage);
        Assert.DoesNotContain(other.Nav.Visits, v => v.Route == Route.Home);
        Assert.Single(await t.Db.Rounds.ListAsync());       // not cleared yet
        Assert.NotNull(await t.Db.Entitlements.GetAsync());

        // Syncing is blocked while the question is open: nothing of the old account is sent to the new one.
        var syncStub = new StubHandler(_ => StubHandler.Status(HttpStatusCode.OK));
        var result = await new SyncEngine(t.Db, syncStub.Client()).SyncAsync(TestContext.Current.CancellationToken);
        Assert.True(result.SignedOut);
        Assert.Empty(syncStub.Requests);
    }

    [Fact]
    public async Task Confirming_clears_every_trace_of_the_previous_account_and_goes_home()
    {
        await using var t = await TempDb.CreateAsync();
        await SignInAsync(SignInScreen(t, "ada@example.com"), "ada@example.com");
        await SeedAccountDataAsync(t);
        await t.Db.Rounds.SaveAsync(Samples.FullShotRound());
        var other = SignInScreen(t, "bob@example.com");
        await SignInAsync(other, "bob@example.com");

        await other.Vm.ConfirmAccountSwitchCommand.ExecuteAsync(null);

        Assert.Equal(Route.Home, other.Nav.Last.Route);
        Assert.Null(other.Vm.PendingSwitch);
        await AssertEmptyAsync(t);
        // The new account owns the device now: a later sync is allowed again.
        var stub = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, Payloads.Changes(DateTimeOffset.UnixEpoch.AddDays(10))));
        Assert.False((await new SyncEngine(t.Db, stub.Client()).SyncAsync(TestContext.Current.CancellationToken)).SignedOut);
    }

    [Fact]
    public async Task Cancelling_signs_the_new_session_out_and_keeps_the_old_data()
    {
        await using var t = await TempDb.CreateAsync();
        await SignInAsync(SignInScreen(t, "ada@example.com"), "ada@example.com");
        await t.Db.Rounds.SaveAsync(Samples.FullShotRound());
        var other = SignInScreen(t, "bob@example.com");
        await SignInAsync(other, "bob@example.com");

        await other.Vm.CancelAccountSwitchCommand.ExecuteAsync(null);

        Assert.Null(other.Vm.PendingSwitch);
        Assert.Empty(other.Store.Values);                    // the new tokens are gone
        Assert.Single(await t.Db.Rounds.ListAsync());
        Assert.Null((await TestAccounts.For(t.Db).GetPendingAsync()));
        // And the original account can sign back in and carry on.
        var back = SignInScreen(t, "ada@example.com");
        await SignInAsync(back, "ada@example.com");
        Assert.Equal(Route.Home, back.Nav.Last.Route);
    }

    [Fact]
    public async Task An_unanswered_question_survives_the_app_being_closed()
    {
        await using var t = await TempDb.CreateAsync();
        await SignInAsync(SignInScreen(t, "ada@example.com"), "ada@example.com");
        await t.Db.Rounds.SaveAsync(Samples.FullShotRound());
        await SignInAsync(SignInScreen(t, "bob@example.com"), "bob@example.com");
        await t.ReopenAsync(); // the app is closed and reopened with the new session's tokens stored

        var reopened = SignInScreen(t, "bob@example.com");
        reopened.Store.Values[TokenService.RefreshTokenKey] = "stored";
        await reopened.Vm.InitializeAsync();

        Assert.NotNull(reopened.Vm.PendingSwitch);           // asks again instead of going home
        Assert.DoesNotContain(reopened.Nav.Visits, v => v.Route == Route.Home);
        await reopened.Vm.ConfirmAccountSwitchCommand.ExecuteAsync(null);
        await AssertEmptyAsync(t);
    }

    [Fact]
    public async Task An_external_sign_in_whose_account_cannot_be_determined_fails_closed()
    {
        await using var t = await TempDb.CreateAsync();
        await t.Db.Rounds.SaveAsync(Samples.FullShotRound());
        var stub = new StubHandler(r => r.Path == "/me" ? StubHandler.Status(HttpStatusCode.InternalServerError) : StubHandler.Json(HttpStatusCode.OK, Payloads.Tokens("a", "r")));
        var store = new FakeSecureStore();
        var session = ApiClientFactory.Create(StubHandler.BaseAddress, store, stub);
        var nav = new FakeNavigator();
        var vm = new SignInViewModel(session.Tokens, TestAccounts.For(t.Db), session.Client, nav);

        await vm.ExternalSignInAsync("Google", "id-token");

        Assert.Equal(UiText.AccountUnknown, vm.ErrorMessage);
        Assert.Empty(store.Values);
        Assert.Empty(nav.Visits);
    }

    [Fact]
    public async Task A_created_account_whose_automatic_sign_in_fails_says_so()
    {
        await using var t = await TempDb.CreateAsync();
        var stub = new StubHandler(r => r.Path == "/auth/register" ? StubHandler.Status(HttpStatusCode.OK) : StubHandler.Status(HttpStatusCode.InternalServerError));
        var store = new FakeSecureStore();
        var session = ApiClientFactory.Create(StubHandler.BaseAddress, store, stub);
        var vm = new SignInViewModel(session.Tokens, TestAccounts.For(t.Db), session.Client, new FakeNavigator());
        vm.ToggleModeCommand.Execute(null);
        vm.Email = "new@example.com";
        vm.Password = "pw";

        await vm.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(UiText.AccountCreatedSignInFailed, vm.ErrorMessage);
        Assert.False(vm.IsRegisterMode); // the account exists: the form goes back to signing in
    }
}
