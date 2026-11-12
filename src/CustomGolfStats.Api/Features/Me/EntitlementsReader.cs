using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Infrastructure;
using CustomGolfStats.Domain;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Features.Me;

/// <summary>Builds an <see cref="EntitlementsResponse"/> from the database, never from token claims, so tier changes show at once.</summary>
public sealed class EntitlementsReader(AppDbContext db, AppSettingsReader settings)
{
    public async Task<EntitlementsResponse?> GetAsync(Guid accountId, CancellationToken ct)
    {
        var account = await db.Users.AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => new { a.Tier, a.Role, a.TrialShotRoundsUsed })
            .SingleOrDefaultAsync(ct);
        if (account is null)
        {
            return null;
        }

        var allowance = await settings.GetTrialShotRoundsAsync(ct);
        return new EntitlementsResponse(
            account.Tier.ToString(),
            account.Role.ToString(),
            new EntitlementFeatures(Entitlements.CanUse(Feature.ShotByShotEntry, account.Tier, account.TrialShotRoundsUsed, allowance)),
            Entitlements.TrialRoundsRemaining(account.Tier, account.TrialShotRoundsUsed, allowance));
    }
}
