using System.Security.Claims;
using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Features.Rounds;

/// <summary><c>/rounds</c>: list, read, upsert and soft delete the signed-in golfer's rounds.</summary>
internal static class RoundsEndpoints
{
    private const int MaxDeleteAttempts = 3;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", ListAsync).WithName("RoundsList");
        group.MapGet("/{id:guid}", GetAsync).WithName("RoundsGet");
        group.MapPut("/{id:guid}", UpsertAsync).WithName("RoundsUpsert");
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("RoundsDelete");
    }

    /// <summary>The golfer's rounds (not deleted), newest first; <paramref name="year"/> limits them to that year.</summary>
    private static async Task<Results<Ok<List<RoundSummaryDto>>, ValidationProblem>> ListAsync(
        int? year, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        if (year is < 1900 or > 9998)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["year"] = [RoundRequestValidation.OutOfRange] });
        }

        var accountId = user.GetAccountId();
        var query = db.Rounds.AsNoTracking().Where(r => r.AccountId == accountId && r.DeletedAt == null);
        if (year is { } y)
        {
            var from = new DateOnly(y, 1, 1);
            var to = from.AddYears(1);
            query = query.Where(r => r.PlayedOn >= from && r.PlayedOn < to);
        }

        var rows = await query
            .OrderByDescending(r => r.PlayedOn).ThenByDescending(r => r.StartedAt).ThenBy(r => r.Id)
            .Select(r => new
            {
                r.Id,
                r.PlayedOn,
                r.TeeVersion.Tee.CourseId,
                CourseName = r.TeeVersion.Tee.Course.Name,
                r.TeeVersion.TeeId,
                TeeName = r.TeeVersion.Tee.Name,
                r.EntryMode,
                Holes = r.HoleResults.Select(h => new { h.HoleNumber, h.Score }).ToList(),
                Pars = r.TeeVersion.Holes.Select(h => new { h.Number, h.Par }).ToList(),
            })
            .AsSplitQuery()
            .ToListAsync(ct);

        return TypedResults.Ok(rows.Select(r =>
        {
            var pars = r.Pars.ToDictionary(p => p.Number, p => p.Par);
            var score = r.Holes.Sum(h => h.Score);
            var par = r.Holes.Sum(h => pars.GetValueOrDefault(h.HoleNumber));
            return new RoundSummaryDto(r.Id, r.PlayedOn, r.CourseId, r.CourseName, r.TeeId, r.TeeName, r.EntryMode, r.Holes.Count, score, score - par);
        }).ToList());
    }

    private static async Task<Results<Ok<RoundDto>, NotFound>> GetAsync(Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var round = await LoadAsync(db, user.GetAccountId(), id, ct);
        return round is null || round.DeletedAt is not null ? TypedResults.NotFound() : TypedResults.Ok(round);
    }

    /// <summary>
    /// Creates or replaces the whole round. 409 (with the server copy) when the stored round was edited more recently
    /// on a device, or was deleted.
    /// </summary>
    private static async Task<Results<Ok<RoundDto>, Conflict<RoundDto>, NotFound, ValidationProblem>> UpsertAsync(
        Guid id, UpsertRoundRequest request, ClaimsPrincipal user, RoundUpsertService service, AppDbContext db, CancellationToken ct)
    {
        var accountId = user.GetAccountId();
        var outcome = await service.UpsertAsync(accountId, id, request, ct);
        switch (outcome.Status)
        {
            case UpsertStatus.Invalid:
                return TypedResults.ValidationProblem(outcome.Errors!);
            case UpsertStatus.NotFound:
                return TypedResults.NotFound();
        }

        db.ChangeTracker.Clear();
        var round = await LoadAsync(db, accountId, id, ct);
        if (round is null)
            return TypedResults.NotFound();
        return outcome.Status == UpsertStatus.Conflict ? TypedResults.Conflict(round) : TypedResults.Ok(round);
    }

    /// <summary>Soft delete, so the delete syncs to the golfer's other devices. Deleting twice is fine.</summary>
    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        Guid id, ClaimsPrincipal user, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var accountId = user.GetAccountId();
        for (var attempt = 1; ; attempt++)
        {
            var round = await db.Rounds.SingleOrDefaultAsync(r => r.Id == id && r.AccountId == accountId, ct);
            if (round is null)
                return TypedResults.NotFound();
            if (round.DeletedAt is not null)
                return TypedResults.NoContent();

            var now = time.GetUtcNow();
            round.DeletedAt = now;
            round.ModifiedAt = now;
            round.Version++;
            try
            {
                await db.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxDeleteAttempts)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>The golfer's round with details (deleted ones included), or null when missing or someone else's.</summary>
    private static async Task<RoundDto?> LoadAsync(AppDbContext db, Guid accountId, Guid id, CancellationToken ct)
    {
        var round = await db.Rounds.AsNoTracking().WithDetails().SingleOrDefaultAsync(r => r.Id == id && r.AccountId == accountId, ct);
        return round?.ToDto();
    }
}
