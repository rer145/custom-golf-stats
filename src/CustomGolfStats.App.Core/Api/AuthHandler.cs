using System.Net;
using System.Net.Http.Headers;

namespace CustomGolfStats.App.Core.ApiAccess;

/// <summary>Attaches the bearer token; on a 401 refreshes once and retries the request once. The credential endpoints (login, register, refresh, external) pass through untouched.</summary>
public sealed class AuthHandler(TokenService tokens) : DelegatingHandler
{
    /// <summary>The credential endpoints, which never carry a bearer and never trigger a refresh. Other /auth/* calls (manage/2fa) need one.</summary>
    private static readonly HashSet<string> AnonymousPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/auth/login", "/auth/register", "/auth/refresh", "/auth/external",
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is { AbsolutePath: var path } && AnonymousPaths.Contains(path.TrimEnd('/')))
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Buffer the body so the request can be replayed after a refresh (a request message can only be sent once).
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var token = await tokens.GetAccessTokenAsync().ConfigureAwait(false);

        var response = await base.SendAsync(Clone(request, body, token), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        if (!await tokens.RefreshAsync(token, cancellationToken))
            return response; // session over: let the caller see the 401

        response.Dispose();
        return await base.SendAsync(Clone(request, body, await tokens.GetAccessTokenAsync()), cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestMessage Clone(HttpRequestMessage original, byte[]? body, string? token)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri) { Version = original.Version };
        foreach (var header in original.Headers.Where(h => !string.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase)))
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            foreach (var header in original.Content!.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        if (token is not null)
            clone.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return clone;
    }
}
