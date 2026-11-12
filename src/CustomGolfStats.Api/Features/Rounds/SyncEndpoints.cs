using System.Globalization;
using System.Security.Claims;
using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Features.Me;
using CustomGolfStats.Api.Infrastructure;
using CustomGolfStats.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CustomGolfStats.Api.Features.Rounds;

/// <summary>Configuration section <c>Sync</c>.</summary>
public sealed class SyncOptions
{
    public const string SectionName = "Sync";

    /// <summary>
    /// How far before the server time <c>nextSince</c> is set, to cover writes still committing and clock skew between
    /// API instances. Configured as <c>Sync:CursorMargin</c> (for example <c>00:02:00</c>).
    /// </summary>
    public TimeSpan CursorMargin { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary><c>/sync/changes</c>: what a device pulls.</summary>
internal static class SyncEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/changes", GetChangesAsync).WithName("SyncChanges");
    }

    /// <summary>
    /// Rounds changed since <paramref name="since"/> (all of them when omitted), deleted ones as tombstones; the full
    /// courses those rounds use; every handicap entry; and the golfer's entitlements. Rounds are filtered on the
    /// server-set <c>ModifiedAt &gt;= since</c>. <c>ModifiedAt</c> is stamped before its transaction commits (and servers'
    /// clocks can differ), so a write stamped just before this read may not be visible yet. The returned
    /// <c>nextSince</c> is therefore the server time taken before the reads minus <see cref="SyncOptions.CursorMargin"/>:
    /// the client sends it back as <c>since</c>, and a write that commits within the margin is picked up next time.
    /// Rounds may come more than once; clients key them by id and version.
    /// </summary>
    private static async Task<Results<Ok<SyncChangesResponse>, ValidationProblem>> GetChangesAsync(
        string? since, ClaimsPrincipal user, AppDbContext db, EntitlementsReader entitlementsReader, TimeProvider time,
        IOptions<SyncOptions> options, CancellationToken ct)
    {
        DateTimeOffset? cursor = null;
        if (!string.IsNullOrEmpty(since))
        {
            if (!DateTimeOffset.TryParse(since, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["since"] = ["InvalidDateTime"] });
            }

            cursor = parsed.ToUniversalTime();
        }

        var serverTime = time.GetUtcNow();
        var accountId = user.GetAccountId();

        var roundsQuery = db.Rounds.AsNoTracking().Where(r => r.AccountId == accountId);
        if (cursor is { } from)
        {
            roundsQuery = roundsQuery.Where(r => r.ModifiedAt >= from);
        }

        var rounds = await roundsQuery.WithDetails().OrderBy(r => r.ModifiedAt).ThenBy(r => r.Id).ToListAsync(ct);

        var courseIds = rounds.Select(r => r.TeeVersion.Tee.CourseId).Distinct().ToList();
        var courses = await db.Courses.AsNoTracking()
            .Where(c => courseIds.Contains(c.Id))
            .Include(c => c.Tees).ThenInclude(t => t.Versions).ThenInclude(v => v.Holes)
            .AsSplitQuery()
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .ToListAsync(ct);

        var handicapEntries = await db.HandicapEntries.AsNoTracking()
            .Where(h => h.AccountId == accountId)
            .OrderBy(h => h.EffectiveOn).ThenBy(h => h.Id)
            .Select(h => new SyncHandicapEntryDto(h.Id, h.EffectiveOn, h.Index))
            .ToListAsync(ct);

        var entitlements = await entitlementsReader.GetAsync(accountId, ct)
            ?? throw new InvalidOperationException("Signed-in account no longer exists.");

        return TypedResults.Ok(new SyncChangesResponse(
            serverTime,
            serverTime - options.Value.CursorMargin,
            [.. rounds.Select(r => r.ToDto())],
            [.. courses.Select(c => c.ToSyncDto())],
            handicapEntries,
            entitlements));
    }
}
