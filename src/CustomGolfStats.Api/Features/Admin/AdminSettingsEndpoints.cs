using System.Globalization;
using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Features.Admin;

/// <summary><c>/admin/settings</c>: app settings stored as <see cref="AppSetting"/> rows.</summary>
public static class AdminSettingsEndpoints
{
    public static RouteGroupBuilder MapAdminSettingsRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("/settings", GetAsync).WithName("AdminGetSettings");
        group.MapPut("/settings", UpdateAsync).WithName("AdminUpdateSettings");
        return group;
    }

    /// <summary><c>GET /admin/settings</c>: the values in effect (defaults where a setting is unset).</summary>
    internal static async Task<Ok<AdminSettingsDto>> GetAsync(AppSettingsReader settings, CancellationToken ct) =>
        TypedResults.Ok(new AdminSettingsDto(await settings.GetTrialShotRoundsAsync(ct)));

    /// <summary>
    /// <c>PUT /admin/settings</c>: sets every setting (<c>trialShotRounds</c>: integer &gt;= 0). <see cref="AppSettingsReader"/>
    /// reads the database on every call (no cache), so the new allowance applies to the next request.
    /// </summary>
    internal static async Task<Results<Ok<AdminSettingsDto>, ValidationProblem>> UpdateAsync(
        UpdateSettingsRequest request, AppDbContext db, AppSettingsReader settings, CancellationToken ct)
    {
        if (request.TrialShotRounds is not >= 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["trialShotRounds"] = ["trialShotRounds is required and must be an integer of 0 or more."],
            });
        }

        var value = request.TrialShotRounds.Value.ToString(CultureInfo.InvariantCulture);
        var row = await db.AppSettings.SingleOrDefaultAsync(s => s.Key == AppSettingKeys.TrialShotRounds, ct);
        if (row is null)
        {
            db.AppSettings.Add(new AppSetting { Key = AppSettingKeys.TrialShotRounds, Value = value });
        }
        else
        {
            row.Value = value;
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new AdminSettingsDto(await settings.GetTrialShotRoundsAsync(ct)));
    }
}
