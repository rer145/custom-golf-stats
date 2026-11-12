using CustomGolfStats.Api.Infrastructure;
using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Features.Courses;
using CustomGolfStats.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CustomGolfStats.Api.Features.Rounds;

public enum UpsertStatus { Saved, Invalid, NotFound, Conflict }

/// <param name="Errors">Set when <see cref="Status"/> is <see cref="UpsertStatus.Invalid"/>.</param>
public sealed record UpsertOutcome(UpsertStatus Status, IDictionary<string, string[]>? Errors = null);

/// <summary>
/// <c>PUT /rounds/{id}</c>: last write wins per round on <see cref="Round.ClientUpdatedAt"/>, trial counting once when a
/// Free golfer's round is first stored as shot-by-shot, then a weather lookup when the round has none.
/// </summary>
public sealed class RoundUpsertService(AppDbContext db, TeeVersionService teeVersions, RoundWeatherFiller weather, TimeProvider time)
{
    // Racing pushes of the same round lose on the primary key (create) or the Version token (update) and are re-run.
    private const int MaxAttempts = 5;
    private const string RoundsPrimaryKey = "pk_rounds";
    private const string HoleResultsEntrySequenceIndex = "ix_hole_results_round_id_entry_sequence";

    public async Task<UpsertOutcome> UpsertAsync(Guid accountId, Guid roundId, UpsertRoundRequest request, CancellationToken ct)
    {
        var headerErrors = new ValidationErrors();
        RoundRequestValidation.ValidateHeader(request, headerErrors);
        if (headerErrors.Any)
        {
            return new UpsertOutcome(UpsertStatus.Invalid, headerErrors.ToDictionary());
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var outcome = await TryUpsertAsync(accountId, roundId, request, ct);
                if (outcome.Status == UpsertStatus.Saved)
                {
                    await weather.FillIfMissingAsync(roundId, ct);
                }

                return outcome;
            }
            catch (DbUpdateException ex) when (attempt < MaxAttempts && IsLostRace(ex))
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    // Duplicate entry sequences are rejected before saving, so a clash on that index means another push won.
    private static bool IsLostRace(DbUpdateException ex) =>
        ex is DbUpdateConcurrencyException
        || ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: RoundsPrimaryKey or HoleResultsEntrySequenceIndex,
        };

    private async Task<UpsertOutcome> TryUpsertAsync(Guid accountId, Guid roundId, UpsertRoundRequest request, CancellationToken ct)
    {
        var clientUpdatedAt = request.ClientUpdatedAt.ToUniversalTime();
        var existing = await db.Rounds
            .Include(r => r.TeeVersion).ThenInclude(v => v.Holes)
            .Include(r => r.HoleResults).ThenInclude(h => h.Shots)
            .AsSplitQuery()
            .SingleOrDefaultAsync(r => r.Id == roundId, ct);

        if (existing is not null)
        {
            if (existing.AccountId != accountId)
                return new UpsertOutcome(UpsertStatus.NotFound);
            // Deletes are final, and an older edit never overwrites a newer one.
            if (existing.DeletedAt is not null || existing.ClientUpdatedAt > clientUpdatedAt)
                return new UpsertOutcome(UpsertStatus.Conflict);
        }

        var keepVersion = existing is not null && existing.TeeVersion.TeeId == request.TeeId && existing.PlayedOn == request.PlayedOn;
        var version = keepVersion ? existing!.TeeVersion : await teeVersions.ResolveAsync(request.TeeId, request.PlayedOn, ct);
        var errors = new ValidationErrors();
        if (version is null)
        {
            errors.Add("teeId", "UnknownTee");
            return new UpsertOutcome(UpsertStatus.Invalid, errors.ToDictionary());
        }

        var holes = RoundRequestValidation.BuildHoles(roundId, request, version, errors);
        if (errors.Any)
        {
            return new UpsertOutcome(UpsertStatus.Invalid, errors.ToDictionary());
        }

        var round = existing ?? new Round { Id = roundId, AccountId = accountId };
        if (existing is null)
        {
            db.Rounds.Add(round);
        }
        else
        {
            db.Shots.RemoveRange(existing.HoleResults.SelectMany(h => h.Shots));
            db.HoleResults.RemoveRange(existing.HoleResults);
            existing.HoleResults.Clear();
            existing.Version++;
        }

        Apply(round, request, version, clientUpdatedAt, keepServerWeather: existing is not null);
        round.HoleResults.AddRange(holes);
        // Explicitly Added: with their ids already set, EF would otherwise take them for existing rows of a tracked round.
        db.HoleResults.AddRange(holes);
        db.Shots.AddRange(holes.SelectMany(h => h.Shots));

        // A trial round is used when a shot-by-shot round is started: on create, or when an update switches a quick round
        // to shot-by-shot. TrialCounted makes it once per round.
        var countTrial = round.EntryMode == EntryMode.ShotByShot
            && !round.TrialCounted
            && await db.Users.Where(a => a.Id == accountId).Select(a => a.Tier).SingleAsync(ct) == Tier.Free;
        if (!countTrial)
        {
            await db.SaveChangesAsync(ct);
            return new UpsertOutcome(UpsertStatus.Saved);
        }

        // The round write and the increment commit together. A racing push of the same round loses on the primary key
        // (create) or the Version token (update), rolls back its increment and is re-run, now seeing TrialCounted.
        round.TrialCounted = true;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        await db.Users
            .Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.TrialShotRoundsUsed, a => a.TrialShotRoundsUsed + 1), ct);
        await transaction.CommitAsync(ct);
        return new UpsertOutcome(UpsertStatus.Saved);
    }

    private void Apply(Round round, UpsertRoundRequest request, TeeVersion version, DateTimeOffset clientUpdatedAt, bool keepServerWeather)
    {
        round.TeeVersionId = version.Id;
        round.TeeVersion = version;
        round.PlayedOn = request.PlayedOn;
        round.StartedAt = request.StartedAt?.ToUniversalTime();
        round.FinishedAt = request.FinishedAt?.ToUniversalTime();
        round.EntryMode = request.EntryMode!.Value;
        round.RoundType = request.RoundType ?? RoundType.Casual;
        round.Notes = request.Notes;
        round.DurationMinutes = request.DurationMinutes;
        round.DistanceMiles = request.DistanceMiles;
        round.StepCount = request.StepCount;
        round.ClientUpdatedAt = clientUpdatedAt;
        round.ModifiedAt = time.GetUtcNow();

        var sentWeather = request.WeatherConditions is not null || request.TemperatureF is not null
            || request.WindSpeedMph is not null || request.WindDirectionDegrees is not null;
        // A push without weather keeps what is stored (usually filled in by the server), so it isn't looked up again.
        if (sentWeather || !keepServerWeather)
        {
            round.WeatherConditions = request.WeatherConditions;
            round.TemperatureF = request.TemperatureF;
            round.WindSpeedMph = request.WindSpeedMph;
            round.WindDirectionDegrees = request.WindDirectionDegrees;
        }
    }
}
