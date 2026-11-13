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

    /// <summary>Raised when the refresh token was rejected and the stored tokens were cleared: the golfer must sign in again.</summary>
    public event Action? SessionExpired;

    public async Task<bool> IsSignedInAsync() => await store.GetAsync(RefreshTokenKey) is not null;

    public Task<string?> GetAccessTokenAsync() => store.GetAsync(AccessTokenKey);

    public async Task SignInAsync(string email, string password, CancellationToken ct = default)
    {
        var tokens = await authApi.AuthLoginAsync(false, false, new LoginRequest { Email = email, Password = password }, ct);
        await SaveAsync(tokens);
    }

    /// <summary>Creates the account, then signs in with it.</summary>
    public async Task RegisterAsync(string email, string password, CancellationToken ct = default)
    {
        await authApi.AuthRegisterAsync(new RegisterRequest { Email = email, Password = password }, ct);
        await SignInAsync(email, password, ct);
    }

    /// <summary>Apple / Google sign-in with an ID token supplied by the platform.</summary>
    public async Task ExternalSignInAsync(string provider, string idToken, string? firstName = null, string? lastName = null, CancellationToken ct = default)
    {
        var tokens = await authApi.AuthExternalSignInAsync(
            new ExternalSignInRequest { Provider = provider, IdToken = idToken, FirstName = firstName, LastName = lastName }, ct);
        await SaveAsync(tokens);
    }

    /// <summary>
    /// Refreshes the access token. <paramref name="staleAccessToken"/> is the token the failed request used: if another caller
    /// already replaced it, nothing is sent and true is returned (concurrent 401s share one refresh). Returns false when there is
    /// no refresh token or the server rejected it (tokens are cleared and <see cref="SessionExpired"/> raised). A network
    /// failure propagates and keeps the tokens.
    /// </summary>
    public async Task<bool> RefreshAsync(string? staleAccessToken, CancellationToken ct = default)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            var current = await store.GetAsync(AccessTokenKey);
            if (current is not null && staleAccessToken is not null && current != staleAccessToken)
                return true;

            var refresh = await store.GetAsync(RefreshTokenKey);
            if (refresh is null)
                return false;

            try
            {
                var tokens = await authApi.AuthRefreshAsync(new RefreshRequest { RefreshToken = refresh }, ct);
                await SaveAsync(tokens);
                return true;
            }
            catch (ApiException ex) when (ex.StatusCode is 400 or 401 or 403)
            {
                await ClearAsync();
                SessionExpired?.Invoke();
                return false;
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public Task SignOutAsync() => ClearAsync();

    private async Task SaveAsync(AccessTokenResponse tokens)
    {
        await store.SetAsync(AccessTokenKey, tokens.AccessToken);
        await store.SetAsync(RefreshTokenKey, tokens.RefreshToken);
    }

    private async Task ClearAsync()
    {
        await store.RemoveAsync(AccessTokenKey);
        await store.RemoveAsync(RefreshTokenKey);
    }
}
