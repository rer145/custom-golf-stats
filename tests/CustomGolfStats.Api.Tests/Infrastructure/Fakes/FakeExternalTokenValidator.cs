using System.Collections.Concurrent;
using CustomGolfStats.Api.Features.Auth;

namespace CustomGolfStats.Api.Tests.Infrastructure.Fakes;

/// <summary>Accepts only ID tokens registered with <see cref="Accept"/>; everything else is invalid (null).</summary>
public sealed class FakeExternalTokenValidator : IExternalTokenValidator
{
    private readonly ConcurrentDictionary<(string Provider, string IdToken), ExternalIdentity> _tokens = new();

    public void Accept(string idToken, ExternalIdentity identity) => _tokens[(identity.Provider, idToken)] = identity;

    public Task<ExternalIdentity?> ValidateAsync(string provider, string idToken, CancellationToken ct) =>
        Task.FromResult(_tokens.GetValueOrDefault((provider, idToken)));
}
