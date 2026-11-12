using CustomGolfStats.Api.Data.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace CustomGolfStats.Api.Features.Auth;

public static class AuthFeature
{
    public static IServiceCollection AddAuthFeature(this IServiceCollection services)
    {
        // Registered after AddIdentityApiEndpoints, so this replaces the stock SignInManager (disabled accounts are refused).
        services.AddScoped<SignInManager<Account>, AccountSignInManager>();

        services.AddSingleton<IExternalTokenValidator>(sp => new ExternalTokenValidator(
            ExternalAuthOptions.From(sp.GetRequiredService<IConfiguration>()),
            ExternalTokenValidator.CreateMetadataSources(),
            sp.GetRequiredService<TimeProvider>()));
        return services;
    }

    /// <summary>
    /// <c>/auth/register</c>, <c>/auth/login</c> and <c>/auth/refresh</c> come from Identity's bearer API
    /// (<c>MapIdentityApi</c>), as does <c>/auth/manage/info</c> (the "who am I" probe the test harness uses). Identity also maps
    /// confirmEmail, resendConfirmationEmail, forgotPassword, resetPassword and manage/2fa. Those are unused by the apps:
    /// there is no email sender, so they are inert, and account changes go through the Me feature instead.
    /// <c>/auth/external</c> is ours.
    /// </summary>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/auth").WithTags("Auth");
        auth.MapIdentityApi<Account>();

        auth.MapPost("/external", ExternalSignInAsync)
            .WithName("AuthExternalSignIn")
            .AllowAnonymous();
        return app;
    }

    private static async Task<Results<EmptyHttpResult, ValidationProblem, UnauthorizedHttpResult>> ExternalSignInAsync(
        ExternalSignInRequest request,
        IExternalTokenValidator validator,
        UserManager<Account> users,
        SignInManager<Account> signIn,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var provider = request.Provider?.Trim().ToLowerInvariant();
        if (provider is not (ExternalTokenValidator.Apple or ExternalTokenValidator.Google))
        {
            errors["provider"] = ["Provider must be 'apple' or 'google'."];
        }

        if (string.IsNullOrWhiteSpace(request.IdToken))
        {
            errors["idToken"] = ["An ID token is required."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var identity = await validator.ValidateAsync(provider!, request.IdToken!, ct);
        if (identity is null)
        {
            return TypedResults.Unauthorized();
        }

        var account = await users.FindByLoginAsync(identity.Provider, identity.Subject);
        if (account is null)
        {
            // Link by email only when the provider vouches for it; otherwise anyone could claim someone else's account.
            if (identity.Email is not null)
            {
                var byEmail = await users.FindByEmailAsync(identity.Email);
                if (byEmail is not null)
                {
                    if (!identity.EmailVerified)
                    {
                        return TypedResults.Unauthorized();
                    }

                    account = byEmail;
                }
            }

            if (account is null)
            {
                account = NewAccount(identity, request);
                if (!(await users.CreateAsync(account)).Succeeded)
                {
                    return TypedResults.Unauthorized();
                }
            }

            if (!(await users.AddLoginAsync(account, new UserLoginInfo(identity.Provider, identity.Subject, identity.Provider))).Succeeded)
            {
                return TypedResults.Unauthorized();
            }
        }

        if (account.DisabledAt is not null)
        {
            return TypedResults.Unauthorized();
        }

        // Same token response as /auth/login: the bearer handler writes it when signing in.
        signIn.AuthenticationScheme = IdentityConstants.BearerScheme;
        await signIn.SignInAsync(account, isPersistent: false);
        return TypedResults.Empty;
    }

    /// <summary>Accounts without an email still need a unique user name made of characters Identity allows.</summary>
    private static string SafeUserName(ExternalIdentity identity) =>
        identity.Provider + "_" + new string(identity.Subject.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' ? c : '_').ToArray());

    private static Account NewAccount(ExternalIdentity identity, ExternalSignInRequest request) => new()
    {
        UserName = identity.Email ?? SafeUserName(identity),
        Email = identity.Email,
        EmailConfirmed = identity.EmailVerified,
        FirstName = request.FirstName?.Trim() ?? "",
        LastName = request.LastName?.Trim() ?? "",
    };
}
