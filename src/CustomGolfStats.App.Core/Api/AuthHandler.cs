using System.Net;
using System.Net.Http.Headers;

namespace CustomGolfStats.App.Core.ApiAccess;

/// <summary>Attaches the bearer token; on a 401 refreshes once and retries the request once. /auth/* calls pass through untouched.</summary>
public sealed class AuthHandler(TokenService tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is { AbsolutePath: var path } && path.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase))
            return await base.SendAsync(request, cancellationToken);

        // Buffer the body so the request can be replayed after a refresh (a request message can only be sent once).
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var token = await tokens.GetAccessTokenAsync();

        var response = await base.SendAsync(Clone(request, body, token), cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        if (!await tokens.RefreshAsync(token, cancellationToken))
            return response; // session over: let the caller see the 401

        response.Dispose();
        return await base.SendAsync(Clone(request, body, await tokens.GetAccessTokenAsync()), cancellationToken);
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
