namespace CustomGolfStats.Api.Features.Auth;

/// <summary>A verified identity from an Apple or Google ID token.</summary>
public sealed record ExternalIdentity(string Provider, string Subject, string? Email, bool EmailVerified);

/// <summary>Validates Apple / Google ID tokens (signature, issuer, audience, expiry). Faked in tests.</summary>
public interface IExternalTokenValidator
{
    /// <param name="provider"><c>apple</c> or <c>google</c>.</param>
    /// <returns>The identity, or null when the token is invalid.</returns>
    Task<ExternalIdentity?> ValidateAsync(string provider, string idToken, CancellationToken ct);
}
