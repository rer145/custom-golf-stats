using System.Security.Claims;
using CustomGolfStats.Api.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CustomGolfStats.Api.Infrastructure;

/// <summary>
/// Adds <see cref="Account.Role"/> as a role claim, so <c>RequireRole</c> / the <see cref="Policies.Admin"/> policy work.
/// Claims are fixed when a token is issued (login or refresh), so a role change applies from the next one.
/// </summary>
public sealed class AccountClaimsPrincipalFactory(UserManager<Account> userManager, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<Account>(userManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(Account user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(Options.ClaimsIdentity.RoleClaimType, user.Role.ToString()));
        return identity;
    }
}
