using System.Security.Cryptography;
using CustomGolfStats.Api.Features.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CustomGolfStats.Api.Tests.Auth;

/// <summary>Uses a locally generated RSA key and an in-memory metadata source: no network.</summary>
public sealed class ExternalTokenValidatorTests : IDisposable
{
    private const string AppleIssuer = "https://appleid.apple.com";
    private const string AppleClient = "com.example.golf";
    private const string GoogleClient = "google-client.apps.googleusercontent.com";

    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;
    private readonly FakeTimeProvider _time = new(Now);

    public ExternalTokenValidatorTests() => _key = new RsaSecurityKey(_rsa) { KeyId = "kid-1" };

    public void Dispose() => _rsa.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ExternalTokenValidator CreateValidator(SecurityKey? publishedKey = null, string[]? appleIds = null, string[]? googleIds = null)
    {
        var config = new OpenIdConnectConfiguration();
        config.SigningKeys.Add(publishedKey ?? _key);
        var source = new FakeMetadata(config);
        return new ExternalTokenValidator(
            new ExternalAuthOptions(appleIds ?? [AppleClient], googleIds ?? [GoogleClient]),
            new Dictionary<string, IConfigurationManager<OpenIdConnectConfiguration>>
            {
                [ExternalTokenValidator.Apple] = source,
                [ExternalTokenValidator.Google] = source,
            },
            _time,
            NullLogger<ExternalTokenValidator>.Instance);
    }

    private string Token(
        string issuer = AppleIssuer,
        string audience = AppleClient,
        DateTimeOffset? expires = null,
        SecurityKey? signWith = null,
        string? subject = "sub-1",
        string? email = "golfer@example.com",
        string? emailVerified = "true",
        bool? emailVerifiedBool = null,
        DateTimeOffset? notBefore = null)
    {
        var claims = new Dictionary<string, object>();
        if (subject is not null) claims["sub"] = subject;
        if (email is not null) claims["email"] = email;
        if (emailVerifiedBool is not null) claims["email_verified"] = emailVerifiedBool.Value;
        else if (emailVerified is not null) claims["email_verified"] = emailVerified;
        var expiry = expires ?? Now.AddHours(1);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = Now.UtcDateTime,
            NotBefore = (notBefore ?? Now.AddMinutes(-1)).UtcDateTime,
            Expires = expiry.UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(signWith ?? _key, SecurityAlgorithms.RsaSha256),
        });
    }

    [Fact]
    public async Task Valid_apple_token_returns_the_identity()
    {
        var identity = await CreateValidator().ValidateAsync("apple", Token(), Ct);

        Assert.Equal(new ExternalIdentity("apple", "sub-1", "golfer@example.com", true), identity);
    }

    [Theory]
    [InlineData("accounts.google.com")]
    [InlineData("https://accounts.google.com")]
    public async Task Valid_google_token_accepts_both_issuer_forms(string issuer)
    {
        var token = Token(issuer, GoogleClient, emailVerifiedBool: true);

        var identity = await CreateValidator().ValidateAsync("google", token, Ct);

        Assert.Equal(new ExternalIdentity("google", "sub-1", "golfer@example.com", true), identity);
    }

    [Fact]
    public async Task Unverified_or_missing_email_is_reported_as_unverified()
    {
        var validator = CreateValidator();

        var unverified = await validator.ValidateAsync("apple", Token(emailVerified: "false"), Ct);
        var noEmail = await validator.ValidateAsync("apple", Token(email: null, emailVerified: null), Ct);

        Assert.False(unverified!.EmailVerified);
        Assert.Equal("golfer@example.com", unverified.Email);
        Assert.Null(noEmail!.Email);
        Assert.False(noEmail.EmailVerified);
    }

    [Fact]
    public async Task Token_signed_by_an_unpublished_key_is_rejected()
    {
        using var other = RSA.Create(2048);
        var token = Token(signWith: new RsaSecurityKey(other) { KeyId = "kid-1" });

        Assert.Null(await CreateValidator().ValidateAsync("apple", token, Ct));
    }

    [Fact]
    public async Task Wrong_issuer_is_rejected()
    {
        Assert.Null(await CreateValidator().ValidateAsync("apple", Token(issuer: "https://evil.example.com"), Ct));
        // A Google issuer is not valid for Apple.
        Assert.Null(await CreateValidator().ValidateAsync("apple", Token(issuer: "https://accounts.google.com"), Ct));
    }

    [Fact]
    public async Task Wrong_audience_is_rejected()
    {
        Assert.Null(await CreateValidator().ValidateAsync("apple", Token(audience: "some.other.app"), Ct));
    }

    [Fact]
    public async Task No_configured_audience_rejects_everything()
    {
        Assert.Null(await CreateValidator(appleIds: []).ValidateAsync("apple", Token(), Ct));
    }

    [Fact]
    public async Task Expired_token_is_rejected_using_the_injected_clock()
    {
        var validator = CreateValidator();
        var token = Token(expires: Now.AddMinutes(10));

        Assert.NotNull(await validator.ValidateAsync("apple", token, Ct));

        _time.Advance(TimeSpan.FromMinutes(13));
        Assert.Null(await validator.ValidateAsync("apple", token, Ct));
    }

    [Fact]
    public async Task Token_without_a_subject_is_rejected()
    {
        Assert.Null(await CreateValidator().ValidateAsync("apple", Token(subject: null), Ct));
    }

    [Fact]
    public async Task Garbage_unknown_provider_and_unreachable_metadata_are_invalid_not_errors()
    {
        var validator = CreateValidator();
        Assert.Null(await validator.ValidateAsync("apple", "not-a-jwt", Ct));
        Assert.Null(await validator.ValidateAsync("facebook", Token(), Ct));

        var broken = new ExternalTokenValidator(
            new ExternalAuthOptions([AppleClient], []),
            new Dictionary<string, IConfigurationManager<OpenIdConnectConfiguration>> { ["apple"] = new FakeMetadata(null) },
            _time,
            NullLogger<ExternalTokenValidator>.Instance);
        Assert.Null(await broken.ValidateAsync("apple", Token(), Ct));
    }

    [Fact]
    public void Metadata_sources_point_at_the_real_provider_documents()
    {
        var sources = ExternalTokenValidator.CreateMetadataSources();

        Assert.Equal("https://appleid.apple.com/.well-known/openid-configuration", ((ConfigurationManager<OpenIdConnectConfiguration>)sources["apple"]).MetadataAddress);
        Assert.Equal("https://accounts.google.com/.well-known/openid-configuration", ((ConfigurationManager<OpenIdConnectConfiguration>)sources["google"]).MetadataAddress);
    }

    [Fact]
    public async Task Token_that_is_not_yet_valid_is_rejected()
    {
        Assert.Null(await CreateValidator().ValidateAsync("apple", Token(notBefore: Now.AddHours(1)), Ct));
    }

    [Fact]
    public async Task Unsigned_alg_none_token_is_rejected()
    {
        var header = Base64UrlEncoder.Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var payload = Base64UrlEncoder.Encode(
            $"{{\"iss\":\"{AppleIssuer}\",\"aud\":\"{AppleClient}\",\"sub\":\"sub-1\",\"exp\":{Now.AddHours(1).ToUnixTimeSeconds()}}}");

        Assert.Null(await CreateValidator().ValidateAsync("apple", $"{header}.{payload}.", Ct));
    }

    [Fact]
    public async Task Rotated_signing_key_is_picked_up_by_refreshing_the_metadata_once()
    {
        var stale = new OpenIdConnectConfiguration();
        stale.SigningKeys.Add(new RsaSecurityKey(RSA.Create(2048)) { KeyId = "old" });
        var fresh = new OpenIdConnectConfiguration();
        fresh.SigningKeys.Add(_key);
        var source = new FakeMetadata(stale) { AfterRefresh = fresh };
        var validator = new ExternalTokenValidator(
            new ExternalAuthOptions([AppleClient], []),
            new Dictionary<string, IConfigurationManager<OpenIdConnectConfiguration>> { ["apple"] = source },
            _time,
            NullLogger<ExternalTokenValidator>.Instance);

        var identity = await validator.ValidateAsync("apple", Token(), Ct);

        Assert.NotNull(identity);
        Assert.Equal(1, source.Refreshes);
    }

    private sealed class FakeMetadata(OpenIdConnectConfiguration? configuration) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        private OpenIdConnectConfiguration? _configuration = configuration;

        public OpenIdConnectConfiguration? AfterRefresh { get; init; }

        public int Refreshes { get; private set; }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) =>
            _configuration is null
                ? throw new InvalidOperationException("metadata unreachable")
                : Task.FromResult(_configuration);

        public void RequestRefresh()
        {
            Refreshes++;
            _configuration = AfterRefresh ?? _configuration;
        }
    }
}
