using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Features.Auth;
using CustomGolfStats.Api.Tests.Infrastructure;
using CustomGolfStats.Domain;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Tests.Me;

/// <summary><c>DELETE /me</c>: re-authentication, what is deleted, what survives, and that no session outlives the account.</summary>
public sealed class AccountDeletionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset Now = ApiFactory.StartTime;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Tokens(string AccessToken, string RefreshToken);

    private static Task<HttpResponseMessage> DeleteAccountAsync(HttpClient client, object body) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/me") { Content = JsonContent.Create(body) }, Ct);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;

    private async Task<Tokens> LoginAsync(string email)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/auth/login", new { email, password = TestAuth.Password }, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Tokens>(Ct))!;
    }

    private HttpClient Bearer(string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private Task<bool> ExistsAsync(Guid accountId) => factory.WithDbAsync(db => db.Users.AnyAsync(a => a.Id == accountId, Ct));

    [Fact]
    public async Task Deletes_everything_the_account_owns_keeps_shared_courses_and_leaves_other_accounts_alone()
    {
        var client = await TestAuth.RegisterAsync(factory, "leaving@example.com");
        await TestAuth.RegisterAsync(factory, "staying@example.com");
        var leaving = await TestAuth.GetAccountIdAsync(factory, "leaving@example.com");
        var staying = await TestAuth.GetAccountIdAsync(factory, "staying@example.com");

        // The leaving golfer created a course and edited its tee, played two rounds (one shot by shot), kept a handicap, reported a
        // flag, resolved someone else's flag and linked a Google sign-in. The staying golfer played the same course.
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "Shared Links"));
        Guid leavingShotRound = Guid.NewGuid(), stayingRound = Guid.Empty, stayingFlag = Guid.NewGuid(), leavingFlag = Guid.NewGuid();
        await factory.WithDbAsync(async db =>
        {
            var course = await db.Courses.SingleAsync(c => c.Id == seeded.Course.Id, Ct);
            course.CreatedByAccountId = leaving;
            var version = await db.TeeVersions.SingleAsync(v => v.Id == seeded.Version.Id, Ct);
            version.ChangedByAccountId = leaving;
            (await db.Users.SingleAsync(a => a.Id == leaving, Ct)).HomeCourseId = course.Id;
            (await db.Users.SingleAsync(a => a.Id == staying, Ct)).HomeCourseId = course.Id;

            var round = new Round
            {
                Id = leavingShotRound, AccountId = leaving, TeeVersionId = version.Id, PlayedOn = new DateOnly(2026, 5, 30),
                EntryMode = EntryMode.ShotByShot, ClientUpdatedAt = Now, ModifiedAt = Now,
            };
            var hole = new HoleResult { Round = round, HoleNumber = 1, EntrySequence = 1, Score = 4, Putts = 2, IsDerived = true };
            hole.Shots.Add(new ShotEntity
            {
                Round = round, Sequence = 1, StartLie = Lie.Tee, StartDistance = 380, Club = Club.Driver,
                ShotType = ShotType.Tee, EndLie = Lie.Fairway, EndDistance = 150, Direction = Direction.OnTarget, Shape = Shape.Straight,
            });
            db.AddRange(round, hole);
            db.HandicapEntries.Add(new HandicapEntry { Id = Guid.NewGuid(), AccountId = leaving, EffectiveOn = new DateOnly(2026, 1, 1), Index = 12.4m });
            db.HandicapEntries.Add(new HandicapEntry { Id = Guid.NewGuid(), AccountId = staying, EffectiveOn = new DateOnly(2026, 1, 1), Index = 8.1m });
            db.CourseFlags.Add(new CourseFlag { Id = leavingFlag, CourseId = course.Id, ReporterAccountId = leaving, Note = "Mine", CreatedAt = Now });
            db.CourseFlags.Add(new CourseFlag
            {
                Id = stayingFlag, CourseId = course.Id, ReporterAccountId = staying, Note = "Theirs", CreatedAt = Now,
                Status = FlagStatus.Resolved, ResolvedByAccountId = leaving, ResolvedAt = Now,
            });
            db.UserLogins.Add(new Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>
            {
                LoginProvider = "google", ProviderKey = "g-leaving", ProviderDisplayName = "google", UserId = leaving,
            });
            await db.SaveChangesAsync(Ct);
            await TestData.SeedQuickRoundAsync(db, leaving, seeded.Version, new DateOnly(2026, 6, 1), [4, 5, 3]);
            stayingRound = (await TestData.SeedQuickRoundAsync(db, staying, seeded.Version, new DateOnly(2026, 6, 1), [5, 5, 4])).Id;
        });

        var response = await DeleteAccountAsync(client, new { password = TestAuth.Password });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await factory.WithDbAsync(async db =>
        {
            Assert.False(await db.Users.AnyAsync(a => a.Id == leaving, Ct));
            Assert.False(await db.Rounds.AnyAsync(r => r.AccountId == leaving, Ct));
            Assert.False(await db.HoleResults.AnyAsync(h => h.RoundId == leavingShotRound, Ct));
            Assert.False(await db.Shots.AnyAsync(s => s.RoundId == leavingShotRound, Ct));
            Assert.False(await db.HandicapEntries.AnyAsync(h => h.AccountId == leaving, Ct));
            Assert.False(await db.CourseFlags.AnyAsync(f => f.Id == leavingFlag, Ct));
            Assert.False(await db.UserLogins.AnyAsync(l => l.UserId == leaving, Ct));
            Assert.False(await db.UserClaims.AnyAsync(c => c.UserId == leaving, Ct));
            Assert.False(await db.UserTokens.AnyAsync(t => t.UserId == leaving, Ct));
            // No hole or shot is left without its round.
            Assert.False(await db.HoleResults.AnyAsync(h => !db.Rounds.Any(r => r.Id == h.RoundId), Ct));
            Assert.False(await db.Shots.AnyAsync(s => !db.Rounds.Any(r => r.Id == s.RoundId), Ct));

            // Shared: the course and its tee survive, unattributed.
            var course = await db.Courses.SingleAsync(c => c.Id == seeded.Course.Id, Ct);
            Assert.Null(course.CreatedByAccountId);
            Assert.Null((await db.TeeVersions.SingleAsync(v => v.Id == seeded.Version.Id, Ct)).ChangedByAccountId);
            Assert.Equal(18, await db.TeeHoles.CountAsync(h => h.TeeVersionId == seeded.Version.Id, Ct));

            // The other golfer keeps everything: account, home course, round with holes, handicap, and their flag (unattributed resolver).
            var other = await db.Users.SingleAsync(a => a.Id == staying, Ct);
            Assert.Equal(course.Id, other.HomeCourseId);
            Assert.Equal(3, await db.HoleResults.CountAsync(h => h.RoundId == stayingRound, Ct));
            Assert.Equal(1, await db.HandicapEntries.CountAsync(h => h.AccountId == staying, Ct));
            var flag = await db.CourseFlags.SingleAsync(f => f.Id == stayingFlag, Ct);
            Assert.Null(flag.ResolvedByAccountId);
            Assert.Equal(FlagStatus.Resolved, flag.Status);
        });
    }

    [Fact]
    public async Task No_session_survives_the_deletion()
    {
        await TestAuth.RegisterAsync(factory, "tokens@example.com");
        var accountId = await TestAuth.GetAccountIdAsync(factory, "tokens@example.com");
        var first = await LoginAsync("tokens@example.com");
        var second = await LoginAsync("tokens@example.com"); // another device
        var stale = Bearer(second.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await stale.GetAsync("/me", Ct)).StatusCode); // the account is now cached as active

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAccountAsync(Bearer(first.AccessToken), new { password = TestAuth.Password })).StatusCode);

        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/auth/refresh", new { refreshToken = first.RefreshToken }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/auth/refresh", new { refreshToken = second.RefreshToken }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/auth/login", new { email = "tokens@example.com", password = TestAuth.Password }, Ct)).StatusCode);

        // An access token issued before the deletion is refused at once, with 401 so clients refresh, fail and sign out.
        Assert.Equal(HttpStatusCode.Unauthorized, (await stale.GetAsync("/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stale.GetAsync("/rounds", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await stale.PostAsJsonAsync("/me/handicap", new { effectiveOn = "2026-06-01", index = 10.0 }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await DeleteAccountAsync(stale, new { password = TestAuth.Password })).StatusCode);
        // Anonymous endpoints ignore the stale header.
        Assert.Equal(HttpStatusCode.OK, (await stale.GetAsync("/health", Ct)).StatusCode);
        Assert.False(await factory.WithDbAsync(db => db.HandicapEntries.AnyAsync(h => h.AccountId == accountId, Ct)));
        Assert.False(await ExistsAsync(accountId));
    }

    [Fact]
    public async Task A_wrong_or_missing_password_is_rejected_and_nothing_is_deleted()
    {
        var client = await TestAuth.RegisterAsync(factory, "wrong@example.com");
        var accountId = await TestAuth.GetAccountIdAsync(factory, "wrong@example.com");
        await factory.WithDbAsync(async db =>
        {
            db.HandicapEntries.Add(new HandicapEntry { Id = Guid.NewGuid(), AccountId = accountId, EffectiveOn = new DateOnly(2026, 1, 1), Index = 5.0m });
            await db.SaveChangesAsync(Ct);
        });

        var wrong = await DeleteAccountAsync(client, new { password = "Not-the-passw0rd" });
        var missing = await DeleteAccountAsync(client, new { password = (string?)null });
        var both = await DeleteAccountAsync(client, new { password = TestAuth.Password, externalProvider = "google", externalIdToken = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("The password is incorrect.", (await ReadAsync(wrong)).GetProperty("errors").GetProperty("password")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.True((await ReadAsync(missing)).GetProperty("errors").TryGetProperty("password", out _));
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
        Assert.True(await ExistsAsync(accountId));
        Assert.True(await factory.WithDbAsync(db => db.HandicapEntries.AnyAsync(h => h.AccountId == accountId, Ct)));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_account_out()
    {
        var client = await TestAuth.RegisterAsync(factory, "guess@example.com");
        var accountId = await TestAuth.GetAccountIdAsync(factory, "guess@example.com");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
            statuses.Add((await DeleteAccountAsync(client, new { password = $"Guess-{i}" })).StatusCode);
        var right = await DeleteAccountAsync(client, new { password = TestAuth.Password });

        Assert.Equal([.. Enumerable.Repeat(HttpStatusCode.BadRequest, 4), HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(HttpStatusCode.TooManyRequests, right.StatusCode);
        Assert.True(await ExistsAsync(accountId));
    }

    [Fact]
    public async Task An_external_only_account_confirms_with_a_fresh_token_for_its_own_sign_in()
    {
        factory.TokenValidator.Accept("apple-leaving", new ExternalIdentity("apple", "a-sub-leaving", "ext-leaving@example.com", true));
        factory.TokenValidator.Accept("apple-someone-else", new ExternalIdentity("apple", "a-sub-other", "ext-other@example.com", true));
        var signIn = await factory.CreateClient().PostAsJsonAsync("/auth/external", new { provider = "apple", idToken = "apple-leaving" }, Ct);
        var tokens = (await signIn.Content.ReadFromJsonAsync<Tokens>(Ct))!;
        var client = Bearer(tokens.AccessToken);
        var accountId = await TestAuth.GetAccountIdAsync(factory, "ext-leaving@example.com");

        var noPassword = await DeleteAccountAsync(client, new { password = "anything" });
        var invalid = await DeleteAccountAsync(client, new { externalProvider = "apple", externalIdToken = "expired-or-forged" });
        var otherAccount = await DeleteAccountAsync(client, new { externalProvider = "apple", externalIdToken = "apple-someone-else" });
        var wrongProvider = await DeleteAccountAsync(client, new { externalProvider = "google", externalIdToken = "apple-leaving" });

        Assert.Equal(HttpStatusCode.BadRequest, noPassword.StatusCode);
        Assert.True((await ReadAsync(invalid)).GetProperty("errors").TryGetProperty("externalIdToken", out _));
        Assert.Equal(HttpStatusCode.BadRequest, otherAccount.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongProvider.StatusCode);
        Assert.True(await ExistsAsync(accountId));

        var deleted = await DeleteAccountAsync(client, new { externalProvider = "apple", externalIdToken = "apple-leaving" });

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.False(await ExistsAsync(accountId));
        Assert.False(await factory.WithDbAsync(db => db.UserLogins.AnyAsync(l => l.ProviderKey == "a-sub-leaving", Ct)));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.CreateClient().PostAsJsonAsync("/auth/refresh", new { refreshToken = tokens.RefreshToken }, Ct)).StatusCode);
    }
}
