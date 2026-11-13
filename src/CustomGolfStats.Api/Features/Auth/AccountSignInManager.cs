using System.Security.Claims;
using CustomGolfStats.Api.Data.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CustomGolfStats.Api.Features.Auth;

/// <summary>
/// Refuses disabled accounts (<see cref="Account.DisabledAt"/>) everywhere Identity's bearer endpoints check:
/// password login (via <see cref="CanSignInAsync"/>, which Identity reports as 401) and token refresh
/// (via security-stamp validation, which Identity reports as 401 too).
/// </summary>
public sealed class AccountSignInManager(
    UserManager<Account> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<Account> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<Account>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<Account> confirmation)
    : SignInManager<Account>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(Account user) =>
        user.DisabledAt is null && await base.CanSignInAsync(user);

    public override async Task<Account?> ValidateSecurityStampAsync(ClaimsPrincipal? principal)
    {
        var user = await base.ValidateSecurityStampAsync(principal);
        return user is { DisabledAt: null } ? user : null;
    }
}
