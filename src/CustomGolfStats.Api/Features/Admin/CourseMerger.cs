using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Features.Admin;

public enum CourseMergeOutcome
{
    Merged,
    NotFound,
    TargetNotFound,
    IntoItself,

    /// <summary>The target was itself merged into another course.</summary>
    TargetMerged,

    /// <summary>The source was already merged.</summary>
    AlreadyMerged,

    /// <summary>Some used source tee versions have no matching target version; nothing changed.</summary>
    Unmatched,
}

public sealed record CourseMergeResult(
    CourseMergeOutcome Outcome,
    CourseMergeResultDto? Merged = null,
    IReadOnlyList<UnmatchedTeeVersionDto>? Unmatched = null);

/// <summary>
/// Merges a duplicate course into another, in one transaction:
/// <list type="bullet">
/// <item>Each source tee matches the target tee with the same name (trimmed, case-insensitive) and gender.</item>
/// <item>Each source version that rounds use (soft-deleted rounds included) matches the matched tee's version with the
/// same EffectiveFrom. If any used version has no match, nothing changes and the unmatched ones are returned.</item>
/// <item>Otherwise those rounds move to the matching target versions (ModifiedAt and Version bumped, so sync sends the
/// new course and tee); open flags move to the target (their tee mapped, or cleared when it has no match); home courses
/// and courses previously merged into the source point at the target; the source gets MergedIntoCourseId.</item>
/// </list>
/// Unused source tees and versions are left on the (now hidden) source course.
/// </summary>
public sealed class CourseMerger(AppDbContext db, TimeProvider time)
{
    private const int MaxPasses = 3;

    public async Task<CourseMergeResult> MergeAsync(Guid courseId, Guid intoCourseId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Row locks so two merges (or a merge and a re-merge) of the same courses can't interleave.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM courses WHERE id = {courseId} OR id = {intoCourseId} ORDER BY id FOR UPDATE", ct);

        var source = await LoadAsync(courseId, ct);
        if (source is null)
        {
            return new CourseMergeResult(CourseMergeOutcome.NotFound);
        }

        if (courseId == intoCourseId)
        {
            return new CourseMergeResult(CourseMergeOutcome.IntoItself);
        }

        if (source.MergedIntoCourseId is not null)
        {
            return new CourseMergeResult(CourseMergeOutcome.AlreadyMerged);
        }

        var target = await LoadAsync(intoCourseId, ct);
        if (target is null)
        {
            return new CourseMergeResult(CourseMergeOutcome.TargetNotFound);
        }

        if (target.MergedIntoCourseId is not null)
        {
            return new CourseMergeResult(CourseMergeOutcome.TargetMerged);
        }

        var teeMap = source.Tees.ToDictionary(t => t.Id, t => target.Tees
            .Where(candidate => candidate.Gender == t.Gender && SameName(candidate.Name, t.Name))
            .OrderBy(candidate => candidate.Id)
            .FirstOrDefault());

        var now = time.GetUtcNow();
        var roundsMoved = 0;

        // A second pass picks up rounds saved on a source version while the first pass ran; normally it finds none.
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var usage = await db.Rounds.IgnoreQueryFilters()
                .Where(r => r.TeeVersion.Tee.CourseId == courseId)
                .GroupBy(r => r.TeeVersionId)
                .Select(g => new { VersionId = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            if (usage.Count == 0)
            {
                break;
            }

            var moves = new Dictionary<Guid, Guid>();
            var unmatched = new List<UnmatchedTeeVersionDto>();
            foreach (var used in usage)
            {
                var tee = source.Tees.Single(t => t.Versions.Any(v => v.Id == used.VersionId));
                var version = tee.Versions.Single(v => v.Id == used.VersionId);
                var match = teeMap[tee.Id]?.Versions.SingleOrDefault(v => v.EffectiveFrom == version.EffectiveFrom);
                if (match is null)
                {
                    unmatched.Add(new UnmatchedTeeVersionDto(tee.Id, tee.Name, tee.Gender, version.Id, version.EffectiveFrom, used.Count));
                }
                else
                {
                    moves[version.Id] = match.Id;
                }
            }

            if (unmatched.Count > 0)
            {
                await transaction.RollbackAsync(ct);
                return new CourseMergeResult(
                    CourseMergeOutcome.Unmatched,
                    Unmatched: unmatched.OrderBy(u => u.TeeName, StringComparer.OrdinalIgnoreCase).ThenBy(u => u.Gender)
                        .ThenBy(u => u.EffectiveFrom).ToList());
            }

            foreach (var (from, to) in moves)
            {
                roundsMoved += await db.Rounds.IgnoreQueryFilters()
                    .Where(r => r.TeeVersionId == from)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(r => r.TeeVersionId, to)
                            .SetProperty(r => r.ModifiedAt, now)
                            .SetProperty(r => r.Version, r => r.Version + 1),
                        ct);
            }
        }

        var openFlags = await db.CourseFlags.Where(f => f.CourseId == courseId && f.Status == FlagStatus.Open).ToListAsync(ct);
        foreach (var flag in openFlags)
        {
            flag.CourseId = target.Id;
            flag.TeeId = flag.TeeId is { } teeId && teeMap.TryGetValue(teeId, out var targetTee) ? targetTee?.Id : null;
        }

        await db.Users.Where(a => a.HomeCourseId == courseId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.HomeCourseId, intoCourseId), ct);
        await db.Courses.Where(c => c.MergedIntoCourseId == courseId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.MergedIntoCourseId, intoCourseId), ct);

        source.MergedIntoCourseId = target.Id;
        source.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new CourseMergeResult(
            CourseMergeOutcome.Merged,
            new CourseMergeResultDto(courseId, intoCourseId, roundsMoved, openFlags.Count));
    }

    private Task<Course?> LoadAsync(Guid id, CancellationToken ct) =>
        db.Courses.AsSplitQuery()
            .Include(c => c.Tees).ThenInclude(t => t.Versions)
            .SingleOrDefaultAsync(c => c.Id == id, ct);

    private static bool SameName(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
