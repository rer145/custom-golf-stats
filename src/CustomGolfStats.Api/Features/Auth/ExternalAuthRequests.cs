namespace CustomGolfStats.Api.Features.Auth;

/// <summary>Body of <c>POST /auth/external</c>. <see cref="Provider"/> is <c>apple</c> or <c>google</c>.</summary>
public sealed record ExternalSignInRequest(string? Provider, string? IdToken, string? FirstName, string? LastName);
