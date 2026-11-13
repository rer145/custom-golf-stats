using CustomGolfStats.Api.Client;
using CustomGolfStats.App.Core.Platform;

namespace CustomGolfStats.App.Core.ApiAccess;

/// <summary>
/// Sign-in, sign-up, external sign-in, refresh and sign-out. Tokens live only in <see cref="ISecureStore"/>; nothing here
/// logs, formats or exposes a token except <see cref="GetAccessTokenAsync"/> for the bearer header.
/// <paramref name="authApi"/> must be a client that does NOT use the bearer handler.
/// </summary>
public sealed class TokenService(ISecureStore store, ICustomGolfStatsApiClient authApi)
{
    public const string AccessTokenKey = "auth.accessToken";
    public const string RefreshTokenKey = "auth.refreshToken";

    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    /// <summary>Bumped by every sign-in and sign-out. A refresh only stores its tokens if the session it started in is still current,
    /// so a slow refresh for an old session can never overwrite a newer session's tokens.</summary>
    private long _session;

    /// <summary>Raised when the refresh token was rejected and the stored tokens were cleared: the golfer must sign in again.</summary>
    public event Action? SessionExpired;

    public async Task<bool> IsSignedInAsync() => await store.GetAsync(RefreshTokenKey) is not null;

    public Task<string?> GetAccessTokenAsync() => store.GetAsync(AccessTokenKey);

    public async Task SignInAsync(string email, string password, CancellationToken ct = default)
    {
        var tokens = await authApi.AuthLoginAsync(false, false, new LoginRequest { Email = email, Password = password }, ct).ConfigureAwait(false);
        NewSession();
        await SaveAsync(tokens).ConfigureAwait(false);
    }

    /// <summary>Creates the account, then signs in with it. If the account was created but the sign-in then failed, throws
    /// <see cref="AccountCreatedSignInFailedException"/> so the caller can tell the golfer the account exists.</summary>
    public async Task RegisterAsync(string email, string password, CancellationToken ct = default)
    {
        await authApi.AuthRegisterAsync(new RegisterRequest { Email = email, Password = password }, ct).ConfigureAwait(false);
        try
        {
            await SignInAsync(email, password, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new AccountCreatedSignInFailedException(ex);
        }
    }

    /// <summary>Apple / Google sign-in with an ID token supplied by the platform.</summary>
    public async Task ExternalSignInAsync(string provider, string idToken, string? firstName = null, string? lastName = null, CancellationToken ct = default)
    {
        var tokens = await authApi.AuthExternalSignInAsync(
            new ExternalSignInRequest { Provider = provider, IdToken = idToken, FirstName = firstName, LastName = lastName }, ct);
        NewSession();
        await SaveAsync(tokens).ConfigureAwait(false);
    }

    /// <summary>
    /// Refreshes the access token. <paramref name="staleAccessToken"/> is the token the failed request used: if another caller
    /// already replaced it, nothing is sent and true is returned (concurrent 401s share one refresh). Returns false when there is
    /// no refresh token or the server rejected it (tokens are cleared and <see cref="SessionExpired"/> raised). A network
    /// failure propagates and keeps the tokens.
    /// </summary>
    public async Task<bool> RefreshAsync(string? staleAccessToken, CancellationToken ct = default)
    {
        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = await store.GetAsync(AccessTokenKey).ConfigureAwait(false);
            if (current is not null && staleAccessToken is not null && current != staleAccessToken)
                return true;

            var session = Interlocked.Read(ref _session);
            var refresh = await store.GetAsync(RefreshTokenKey).ConfigureAwait(false);
            if (refresh is null)
                return false;

            try
            {
                var tokens = await authApi.AuthRefreshAsync(new RefreshRequest { RefreshToken = refresh }, ct).ConfigureAwait(false);
                if (Interlocked.Read(ref _session) != session)
                    return true; // signed out or in again meanwhile: the new session's tokens stand; the caller retries with them
                await SaveAsync(tokens).ConfigureAwait(false);
                return true;
            }
            catch (ApiException ex) when (ex.StatusCode is 400 or 401 or 403)
            {
                if (Interlocked.Read(ref _session) != session)
                    return true; // the rejected token belonged to an old session: leave the new one alone
                await ClearAsync().ConfigureAwait(false);
                SessionExpired?.Invoke();
                return false;
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public Task SignOutAsync()
    {
        NewSession();
        return ClearAsync();
    }

    private void NewSession() => Interlocked.Increment(ref _session);

    private async Task SaveAsync(AccessTokenResponse tokens)
    {
        await store.SetAsync(AccessTokenKey, tokens.AccessToken).ConfigureAwait(false);
        await store.SetAsync(RefreshTokenKey, tokens.RefreshToken).ConfigureAwait(false);
    }

    private async Task ClearAsync()
    {
        await store.RemoveAsync(AccessTokenKey).ConfigureAwait(false);
        await store.RemoveAsync(RefreshTokenKey).ConfigureAwait(false);
    }
}

/// <summary>Registration succeeded but the automatic sign-in after it did not.</summary>
public sealed class AccountCreatedSignInFailedException(Exception inner) : Exception("The account was created but signing in failed.", inner);
