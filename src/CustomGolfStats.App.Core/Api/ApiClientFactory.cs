using CustomGolfStats.Api.Client;
using CustomGolfStats.App.Core.Platform;

namespace CustomGolfStats.App.Core.ApiAccess;

/// <summary>The authenticated API client and the token service that backs it.</summary>
public sealed record ApiSession(ICustomGolfStatsApiClient Client, TokenService Tokens);

public static class ApiClientFactory
{
    /// <summary>
    /// Builds the client pair: an unauthenticated one for /auth/* (used by <see cref="TokenService"/>) and the app client whose
    /// requests carry the bearer and refresh once on a 401. <paramref name="inner"/> lets tests supply a stub handler.
    /// </summary>
    public static ApiSession Create(Uri baseAddress, ISecureStore store, HttpMessageHandler? inner = null)
    {
        inner ??= new SocketsHttpHandler();
        var authClient = new CustomGolfStatsApiClient(new HttpClient(inner, disposeHandler: false) { BaseAddress = baseAddress });
        var tokens = new TokenService(store, authClient);
        var http = new HttpClient(new AuthHandler(tokens) { InnerHandler = inner }) { BaseAddress = baseAddress };
        return new ApiSession(new CustomGolfStatsApiClient(http), tokens);
    }
}
