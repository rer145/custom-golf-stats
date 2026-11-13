using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Migration.Mapping;
using CustomGolfStats.Migration.Source;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Migration.Import;

/// <summary>A problem the person running the tool can fix (wrong email, data already imported elsewhere). Exit code 2.</summary>
public sealed class MigrationUserException(string message) : Exception(message);

/// <param name="CoursesReused">Mapped golftrack courses that were kept as they are because something else uses them.</param>
/// <param name="TeesWritten">Tees written, on new courses and (missing tees only) on reused ones.</param>
/// <param name="RoundsRemoved">Previously imported rounds cleared before writing (the same for handicap entries and courses).</param>
/// <param name="RoundsRemovedFromOtherGolfers">Of <paramref name="RoundsRemoved"/>, those imported from a different golftrack
/// golfer than this run's (an earlier run with another --golfer-id).</param>
public sealed record ImportCounts(
    int CoursesWritten,
    int CoursesReused,
    int TeesWritten,
    int TeeHolesWritten,
    int Rounds,
    int HoleResults,
    int HandicapEntries,
    int RoundsRemoved,
    int HandicapEntriesRemoved,
    int CoursesRemoved,
    int RoundsRemovedFromOtherGolfers = 0);

public sealed record ImportResult(Guid AccountId, int GolferId, bool Committed, ImportCounts Counts, MappedImport Mapped);

/// <summary>
/// Imports one golftrack golfer into an existing account, in one transaction:
/// <list type="number">
/// <item>Clears the previous import: the account's rounds and handicap entries whose ids are the deterministic ids of
/// any golftrack Round / HandicapHistory row, then every golftrack course (by the deterministic id of any golftrack
/// Course row) that nothing uses any more: no round of any account, no flag, no home course, no merge.</item>
/// <item>Writes courses (a course still in use is reused as it is; only tees it lacks are added), rounds with hole
/// results, and handicap entries.</item>
/// <item>Commits, or rolls back on a dry run.</item>
/// </list>
/// Rows are written directly (not through the round upsert service): no trial counting, no weather lookups.
/// </summary>
public sealed class Importer(GolftrackReader source, AppDbContext target, TimeProvider time)
{
    public async Task<ImportResult> RunAsync(int golferId, string accountEmail, bool dryRun, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = new UpperInvariantLookupNormalizer().NormalizeEmail(accountEmail.Trim());
        var accountId = await target.Users
            .Where(a => a.NormalizedEmail == normalizedEmail)
            .Select(a => (Guid?)a.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new MigrationUserException(
                $"No account has the email {accountEmail.Trim()}. Create the account through the app or API first.");

        var legacy = await source.ReadAsync(golferId, cancellationToken);
        var allIds = await source.ReadAllIdsAsync(cancellationToken);
        var mapped = GolftrackMapper.Map(legacy, accountId, time.GetUtcNow());

        await using var transaction = await target.Database.BeginTransactionAsync(cancellationToken);

        var golferByRoundId = allIds.RoundGolfers.ToDictionary(r => DeterministicIds.For("Round", r.Key), r => r.Value);
        var importableRoundIds = golferByRoundId.Keys.ToList();
        var importableHandicapIds = allIds.HandicapIds.Select(id => DeterministicIds.For("HandicapHistory", id)).ToList();
        var importableCourseIds = allIds.CourseIds.Select(id => DeterministicIds.For("Course", id)).ToList();

        var previousRoundIds = await target.Rounds
            .Where(r => r.AccountId == accountId && importableRoundIds.Contains(r.Id))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);
        var roundsRemoved = await target.Rounds.Where(r => previousRoundIds.Contains(r.Id)).ExecuteDeleteAsync(cancellationToken);
        var removedFromOtherGolfers = previousRoundIds.Count(id => golferByRoundId[id] != golferId);
        var handicapsRemoved = await target.HandicapEntries
            .Where(h => h.AccountId == accountId && importableHandicapIds.Contains(h.Id))
            .ExecuteDeleteAsync(cancellationToken);
        var coursesRemoved = await target.Courses
            .Where(c => c.Source == CourseSource.Golftrack && importableCourseIds.Contains(c.Id))
            .Where(c => !target.Rounds.Any(r => r.TeeVersion.Tee.CourseId == c.Id))
            .Where(c => !target.CourseFlags.Any(f => f.CourseId == c.Id))
            .Where(c => !target.Users.Any(a => a.HomeCourseId == c.Id))
            .Where(c => !target.Courses.Any(other => other.MergedIntoCourseId == c.Id))
            .ExecuteDeleteAsync(cancellationToken);

        await RefuseRowsOwnedByAnotherAccountAsync(mapped, cancellationToken);

        var (coursesWritten, coursesReused, teesWritten, teeHolesWritten) = await AddCoursesAsync(mapped.Courses, cancellationToken);
        target.Rounds.AddRange(mapped.Rounds);
        target.HandicapEntries.AddRange(mapped.HandicapEntries);
        await target.SaveChangesAsync(cancellationToken);

        if (dryRun)
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        else
        {
            await transaction.CommitAsync(cancellationToken);
        }

        target.ChangeTracker.Clear();
        var counts = new ImportCounts(
            coursesWritten,
            coursesReused,
            teesWritten,
            teeHolesWritten,
            mapped.Rounds.Count,
            mapped.Rounds.Sum(r => r.HoleResults.Count),
            mapped.HandicapEntries.Count,
            roundsRemoved,
            handicapsRemoved,
            coursesRemoved,
            removedFromOtherGolfers);
        return new ImportResult(accountId, golferId, !dryRun, counts, mapped);
    }

    /// <summary>After clearing, a mapped round or handicap id that still exists belongs to another account.</summary>
    private async Task RefuseRowsOwnedByAnotherAccountAsync(MappedImport mapped, CancellationToken cancellationToken)
    {
        var roundIds = mapped.Rounds.Select(r => r.Id).ToList();
        var handicapIds = mapped.HandicapEntries.Select(h => h.Id).ToList();
        var takenRounds = await target.Rounds.CountAsync(r => roundIds.Contains(r.Id), cancellationToken);
        var takenHandicaps = await target.HandicapEntries.CountAsync(h => handicapIds.Contains(h.Id), cancellationToken);
        if (takenRounds > 0 || takenHandicaps > 0)
        {
            throw new MigrationUserException(
                $"{takenRounds} rounds and {takenHandicaps} handicap entries of this golfer are already imported into another " +
                "account. Nothing was changed.");
        }
    }

    private async Task<(int Written, int Reused, int Tees, int TeeHoles)> AddCoursesAsync(
        IReadOnlyList<Course> courses, CancellationToken cancellationToken)
    {
        var courseIds = courses.Select(c => c.Id).ToList();
        var tees = courses.SelectMany(c => c.Tees).ToList();
        var teeIds = tees.Select(t => t.Id).ToList();
        var versionIds = tees.SelectMany(t => t.Versions).Select(v => v.Id).ToList();
        var existingCourses = (await target.Courses.Where(c => courseIds.Contains(c.Id)).Select(c => c.Id).ToListAsync(cancellationToken)).ToHashSet();
        var existingTees = (await target.Tees.Where(t => teeIds.Contains(t.Id)).Select(t => t.Id).ToListAsync(cancellationToken)).ToHashSet();
        var existingVersions = (await target.TeeVersions.Where(v => versionIds.Contains(v.Id)).Select(v => v.Id).ToListAsync(cancellationToken)).ToHashSet();

        int written = 0, reused = 0, teesWritten = 0, teeHolesWritten = 0;
        foreach (var course in courses)
        {
            if (!existingCourses.Contains(course.Id))
            {
                target.Courses.Add(course);
                written++;
                teesWritten += course.Tees.Count;
                teeHolesWritten += course.Tees.SelectMany(t => t.Versions).Sum(v => v.Holes.Count);
                continue;
            }

            // In use by something else: keep it as it is (it may have been edited or verified since), adding only what an
            // imported round needs and the course lacks.
            reused++;
            foreach (var tee in course.Tees)
            {
                if (!existingTees.Contains(tee.Id))
                {
                    target.Tees.Add(tee);
                    teesWritten++;
                    teeHolesWritten += tee.Versions.Sum(v => v.Holes.Count);
                    continue;
                }

                foreach (var version in tee.Versions.Where(v => !existingVersions.Contains(v.Id)))
                {
                    target.TeeVersions.Add(version);
                    teeHolesWritten += version.Holes.Count;
                }
            }
        }

        return (written, reused, teesWritten, teeHolesWritten);
    }
}
