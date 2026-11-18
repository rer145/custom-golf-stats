using System.Security.Claims;
using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CustomGolfStats.Api.Features.Auth;

/// <summary>
/// Every authenticated request must come from an account that still exists and isn't disabled. Bearer access tokens are
/// stateless, so without this a token issued before the account was deleted or disabled would keep working until it
/// expires (up to 1 hour). Part of the default policy (every <c>RequireAuthorization()</c>) and of <see cref="Policies.Admin"/>.
/// A failure is answered 401, not 403 (see <see cref="ActiveAccountResultHandler"/>), so clients refresh, fail and sign out.
/// </summary>
public sealed class ActiveAccountRequirement : IAuthorizationRequirement;

/// <summary>Whether an account exists and isn't disabled, cached briefly per account so the check costs at most one lookup per
/// account every <see cref="Lifetime"/>. Whatever deletes, disables or re-enables an account calls <see cref="Evict"/>.</summary>
public sealed class AccountStatusCache(IMemoryCache cache)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private static string Key(Guid accountId) => $"account-active:{accountId}";

    public async Task<bool> IsActiveAsync(Guid accountId, AppDbContext db, CancellationToken ct)
    {
        if (cache.TryGetValue(Key(accountId), out bool active))
        {
            return active;
        }

        active = await db.Users.AsNoTracking().AnyAsync(a => a.Id == accountId && a.DisabledAt == null, ct);
        cache.Set(Key(accountId), active, Lifetime);
        return active;
    }

    public void Evict(Guid accountId) => cache.Remove(Key(accountId));
}

public sealed class ActiveAccountHandler(AccountStatusCache status, AppDbContext db)
    : AuthorizationHandler<ActiveAccountRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ActiveAccountRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true
            || !Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
        {
            return; // anonymous: the policy's authenticated-user requirement answers (with a challenge)
        }

        if (await status.IsActiveAsync(accountId, db, (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None))
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>Turns a failed <see cref="ActiveAccountRequirement"/> into a challenge (401) instead of the default forbid (403):
/// the token is no longer good for anything. Every other outcome is handled as usual.</summary>
public sealed class ActiveAccountResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure?.FailedRequirements.OfType<ActiveAccountRequirement>().Any() == true)
        {
            return ChallengeAsync(context, policy);
        }

        return _default.HandleAsync(next, context, policy, authorizeResult);
    }

    // As the default handler challenges: each of the policy's schemes, or the default scheme.
    private static async Task ChallengeAsync(HttpContext context, AuthorizationPolicy policy)
    {
        if (policy.AuthenticationSchemes.Count == 0)
        {
            await context.ChallengeAsync();
            return;
        }

        foreach (var scheme in policy.AuthenticationSchemes)
        {
            await context.ChallengeAsync(scheme);
        }
    }
}
