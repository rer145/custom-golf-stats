using System.Globalization;
using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Domain;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Infrastructure;

/// <summary>Typed reads of <see cref="AppSetting"/> rows.</summary>
public sealed class AppSettingsReader(AppDbContext db)
{
    /// <summary>The Free tier's shot-by-shot trial allowance; falls back to the domain default when unset or invalid.</summary>
    public async Task<int> GetTrialShotRoundsAsync(CancellationToken ct)
    {
        var value = await db.AppSettings.AsNoTracking()
            .Where(s => s.Key == AppSettingKeys.TrialShotRounds)
            .Select(s => s.Value)
            .SingleOrDefaultAsync(ct);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rounds) && rounds >= 0
            ? rounds
            : Entitlements.DefaultTrialShotRounds;
    }
}
