using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CustomGolfStats.Api.Features.Auth;

/// <summary>Allowed audiences per provider, from <c>Auth:Apple:ClientIds</c> and <c>Auth:Google:ClientIds</c>.</summary>
public sealed record ExternalAuthOptions(IReadOnlyList<string> AppleClientIds, IReadOnlyList<string> GoogleClientIds)
{
    public static ExternalAuthOptions From(IConfiguration configuration) => new(
        configuration.GetSection("Auth:Apple:ClientIds").Get<string[]>() ?? [],
        configuration.GetSection("Auth:Google:ClientIds").Get<string[]>() ?? []);
}

/// <summary>
/// Validates Apple and Google ID tokens: signature (keys from the provider's OIDC metadata), issuer, audience and
/// expiry. Any failure, including an unknown provider or no configured audience, yields null.
/// </summary>
public sealed class ExternalTokenValidator(
    ExternalAuthOptions options,
    IReadOnlyDictionary<string, IConfigurationManager<OpenIdConnectConfiguration>> metadata,
    TimeProvider time) : IExternalTokenValidator
{
    public const string Apple = "apple";
    public const string Google = "google";

    public const string AppleMetadataAddress = "https://appleid.apple.com/.well-known/openid-configuration";
    public const string GoogleMetadataAddress = "https://accounts.google.com/.well-known/openid-configuration";

    private static readonly string[] AppleIssuers = ["https://appleid.apple.com"];
    private static readonly string[] GoogleIssuers = ["accounts.google.com", "https://accounts.google.com"];

    private readonly JsonWebTokenHandler _handler = new();

    /// <summary>The real metadata sources (cached and refreshed by <see cref="ConfigurationManager{T}"/>).</summary>
    public static IReadOnlyDictionary<string, IConfigurationManager<OpenIdConnectConfiguration>> CreateMetadataSources() =>
        new Dictionary<string, IConfigurationManager<OpenIdConnectConfiguration>>
        {
            [Apple] = Create(AppleMetadataAddress),
            [Google] = Create(GoogleMetadataAddress),
        };

    private static ConfigurationManager<OpenIdConnectConfiguration> Create(string address) =>
        new(address, new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever());

    public async Task<ExternalIdentity?> ValidateAsync(string provider, string idToken, CancellationToken ct)
    {
        string[]? issuers = null;
        IReadOnlyList<string> audiences = [];
        switch (provider)
        {
            case Apple:
                issuers = AppleIssuers;
                audiences = options.AppleClientIds;
                break;
            case Google:
                issuers = GoogleIssuers;
                audiences = options.GoogleClientIds;
                break;
        }

        if (issuers is null || audiences.Count == 0 || string.IsNullOrWhiteSpace(idToken)
            || !metadata.TryGetValue(provider, out var source))
        {
            return null;
        }

        try
        {
            var configuration = await source.GetConfigurationAsync(ct);
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = issuers,
                ValidateAudience = true,
                ValidAudiences = audiences,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = configuration.SigningKeys,
                ClockSkew = TimeSpan.FromMinutes(2),
                LifetimeValidator = (notBefore, expires, _, p) =>
                {
                    var now = time.GetUtcNow().UtcDateTime;
                    return expires is not null
                        && expires.Value + p.ClockSkew > now
                        && (notBefore is null || notBefore.Value - p.ClockSkew <= now);
                },
            };

            var result = await _handler.ValidateTokenAsync(idToken, parameters);
            if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt || string.IsNullOrEmpty(jwt.Subject))
            {
                return null;
            }

            jwt.TryGetClaim("email", out var emailClaim);
            jwt.TryGetClaim("email_verified", out var verifiedClaim);
            // Apple sends email_verified as the string "true"; Google as a JSON boolean.
            var verified = verifiedClaim is not null && bool.TryParse(verifiedClaim.Value, out var v) && v;
            var email = string.IsNullOrWhiteSpace(emailClaim?.Value) ? null : emailClaim.Value;
            return new ExternalIdentity(provider, jwt.Subject, email, verified && email is not null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Metadata unreachable or token malformed: treat as an invalid token.
            return null;
        }
    }
}
