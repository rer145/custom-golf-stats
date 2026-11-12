using CustomGolfStats.Api.Data.Entities;
using Microsoft.AspNetCore.Authentication.BearerToken;
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
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<ExternalTokenValidator>>()));
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
            .Produces<AccessTokenResponse>()
            .AllowAnonymous();
        return app;
    }

    private static async Task<Results<EmptyHttpResult, ValidationProblem, UnauthorizedHttpResult, ProblemHttpResult>> ExternalSignInAsync(
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
        if (account is null && identity.Email is not null)
        {
            account = await users.FindByEmailAsync(identity.Email);
            if (account is not null)
            {
                // Link only when both sides vouch for the address. Register has no email confirmation, so an unconfirmed
                // local account may belong to someone who does not own the address (pre-hijacking).
                if (!account.EmailConfirmed || !identity.EmailVerified)
                {
                    return TypedResults.Problem(
                        "An account with this email already exists; sign in with your password.",
                        statusCode: StatusCodes.Status409Conflict);
                }

                if (account.DisabledAt is not null)
                {
                    return TypedResults.Unauthorized();
                }

                if (!(await users.AddLoginAsync(account, LoginInfo(identity))).Succeeded)
                {
                    return StoreFailure();
                }
            }
        }

        if (account is null)
        {
            account = NewAccount(identity, request);
            if (!(await users.CreateAsync(account)).Succeeded)
            {
                return StoreFailure();
            }

            if (!(await users.AddLoginAsync(account, LoginInfo(identity))).Succeeded)
            {
                await users.DeleteAsync(account);
                return StoreFailure();
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

    private static ProblemHttpResult StoreFailure() =>
        TypedResults.Problem("The account could not be saved.", statusCode: StatusCodes.Status500InternalServerError);

    private static UserLoginInfo LoginInfo(ExternalIdentity identity) =>
        new(identity.Provider, identity.Subject, identity.Provider);

    /// <summary>Accounts without an email still need a unique user name made of characters Identity allows.</summary>
    private static string SafeUserName(ExternalIdentity identity) =>
        identity.Provider + "_" + new string(identity.Subject.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' ? c : '_').ToArray());

    /// <summary>An unverified external email is never stored, so it cannot squat an address it does not own.</summary>
    private static Account NewAccount(ExternalIdentity identity, ExternalSignInRequest request)
    {
        var email = identity.EmailVerified ? identity.Email : null;
        return new Account
        {
            UserName = email ?? SafeUserName(identity),
            Email = email,
            EmailConfirmed = email is not null,
            FirstName = request.FirstName?.Trim() ?? "",
            LastName = request.LastName?.Trim() ?? "",
        };
    }
}
