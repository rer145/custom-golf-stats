using CustomGolfStats.Api.Infrastructure;
using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CustomGolfStats.Api.Features.Courses;

public enum TeeVersionEditOutcome
{
    Applied,
    NotFound,
    Invalid,

    /// <summary>A round (soft-deleted ones included) uses the version and the caller didn't allow editing it.</summary>
    InUse,

    /// <summary>The tee already has another version with the requested EffectiveFrom.</summary>
    DuplicateEffectiveFrom,
}

/// <param name="Version">The saved version (tracked, with holes and tee.course loaded) when applied.</param>
/// <param name="Errors">Validation errors keyed by field when <see cref="TeeVersionEditOutcome.Invalid"/>.</param>
public sealed record TeeVersionEditResult(
    TeeVersionEditOutcome Outcome,
    TeeVersion? Version = null,
    IDictionary<string, string[]>? Errors = null);

/// <summary>
/// Creates and edits tee versions. In-place edits change the stats of every round on that version, so they are
/// refused while any round uses it unless <c>allowWhenUsed</c> (an admin correction, Task 7). Callers decide who may
/// edit (e.g. non-admin edits to a Verified course become flags before reaching here).
/// </summary>
public sealed class TeeVersionEditor(AppDbContext db, TimeProvider time)
{
    /// <summary>True when any round, including soft-deleted ones, points at the version.</summary>
    public Task<bool> IsUsedAsync(Guid versionId, CancellationToken ct) =>
        db.Rounds.IgnoreQueryFilters().AnyAsync(r => r.TeeVersionId == versionId, ct);

    /// <summary>
    /// Edits a version in place (holes matched by number; par totals recomputed) and saves. Records who changed it and
    /// when, sets Source to Manual, and sets the course to UserEdited unless it is Verified.
    /// A null <see cref="TeeVersionInput.EffectiveFrom"/> keeps the current date.
    /// </summary>
    public async Task<TeeVersionEditResult> ApplyAsync(
        Guid versionId, TeeVersionInput changes, bool allowWhenUsed, Guid? changedByAccountId, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        CourseRules.ValidateVersion(changes, errors, requireEffectiveFrom: false);
        if (errors.Any)
        {
            return new TeeVersionEditResult(TeeVersionEditOutcome.Invalid, Errors: errors.ToDictionary());
        }

        var version = await db.TeeVersions
            .Include(v => v.Holes)
            .Include(v => v.Tee).ThenInclude(t => t.Course)
            .SingleOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null)
        {
            return new TeeVersionEditResult(TeeVersionEditOutcome.NotFound);
        }

        if (!allowWhenUsed && await IsUsedAsync(versionId, ct))
        {
            return new TeeVersionEditResult(TeeVersionEditOutcome.InUse, version);
        }

        if (changes.EffectiveFrom is { } effectiveFrom && effectiveFrom != version.EffectiveFrom
            && await db.TeeVersions.AnyAsync(v => v.TeeId == version.TeeId && v.EffectiveFrom == effectiveFrom, ct))
        {
            return new TeeVersionEditResult(TeeVersionEditOutcome.DuplicateEffectiveFrom, version);
        }

        CourseRules.ApplyVersion(version, changes);
        Stamp(version, changedByAccountId);
        return await SaveAsync(version, ct);
    }

    /// <summary>
    /// Adds a new version to <paramref name="tee"/> (tracked, with its course loaded) and saves. Old versions, and the
    /// rounds on them, are untouched. <see cref="TeeVersionInput.EffectiveFrom"/> must be set.
    /// </summary>
    public async Task<TeeVersionEditResult> CreateAsync(Tee tee, TeeVersionInput input, Guid? changedByAccountId, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        CourseRules.ValidateVersion(input, errors, requireEffectiveFrom: true);
        if (errors.Any)
        {
            return new TeeVersionEditResult(TeeVersionEditOutcome.Invalid, Errors: errors.ToDictionary());
        }

        if (await db.TeeVersions.AnyAsync(v => v.TeeId == tee.Id && v.EffectiveFrom == input.EffectiveFrom, ct))
        {
            return new TeeVersionEditResult(TeeVersionEditOutcome.DuplicateEffectiveFrom);
        }

        var version = new TeeVersion { Tee = tee, TeeId = tee.Id };
        CourseRules.ApplyVersion(version, input);
        Stamp(version, changedByAccountId);
        db.TeeVersions.Add(version);
        return await SaveAsync(version, ct);
    }

    /// <summary>True for a PostgreSQL unique-constraint violation (e.g. a concurrent writer won the race).</summary>
    internal static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private async Task<TeeVersionEditResult> SaveAsync(TeeVersion version, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return new TeeVersionEditResult(TeeVersionEditOutcome.Applied, version);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Another request gave this tee a version with the same EffectiveFrom first.
            db.ChangeTracker.Clear();
            return new TeeVersionEditResult(TeeVersionEditOutcome.DuplicateEffectiveFrom);
        }
    }

    private void Stamp(TeeVersion version, Guid? changedByAccountId)
    {
        var now = time.GetUtcNow();
        version.Source = CourseSource.Manual;
        version.ChangedByAccountId = changedByAccountId;
        version.ChangedAt = now;
        var course = version.Tee.Course;
        course.UpdatedAt = now;
        if (course.Status != CourseStatus.Verified)
        {
            course.Status = CourseStatus.UserEdited;
        }
    }
}
