using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Features.Courses;
using CustomGolfStats.Api.Features.Rounds;
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

    /// <summary>Some used source tee versions have no single matching target version; nothing changed.</summary>
    Unmatched,

    /// <summary>Rounds kept arriving on the source while merging; nothing changed, try again.</summary>
    Busy,
}

public sealed record CourseMergeResult(
    CourseMergeOutcome Outcome,
    CourseMergeResultDto? Merged = null,
    IReadOnlyList<UnmatchedTeeVersionDto>? Unmatched = null);

/// <summary>
/// Merges a duplicate course into another, in one transaction:
/// <list type="bullet">
/// <item>Each source tee matches the target tee with the same name (trimmed, case-insensitive) and gender
/// (<see cref="TeeMatching"/>); two such target tees are ambiguous.</item>
/// <item>Each source version that rounds use (soft-deleted rounds included) matches the matched tee's version with the
/// same EffectiveFrom, and that version must have every hole number the rounds played. If any used version has no single
/// such match, nothing changes and the unmatched ones are returned.</item>
/// <item>Otherwise those rounds move to the matching target versions (ModifiedAt and Version bumped, so sync sends the
/// new course and tee) and shot-by-shot ones are re-derived on the target's holes (<see cref="RoundRederiver"/>); open
/// flags move to the target (their tee mapped, or cleared when it has no single match); home courses and courses
/// previously merged into the source point at the target; the source gets MergedIntoCourseId.</item>
/// </list>
/// Unused source tees and versions are left on the (now hidden) source course. Later pushes with a source tee id are
/// redirected by <see cref="MergedTeeRedirector"/>.
/// </summary>
public sealed class CourseMerger(AppDbContext db, RoundRederiver rederiver, TimeProvider time)
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

        var teeMatches = source.Tees.ToDictionary(t => t.Id, t => TeeMatching.Matches(t, target.Tees));
        var now = time.GetUtcNow();
        var movedRoundIds = new List<Guid>();

        // Later passes pick up rounds saved on a source version while the previous pass ran; normally there are none.
        for (var pass = 0; ; pass++)
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

            if (pass == MaxPasses)
            {
                await transaction.RollbackAsync(ct);
                return new CourseMergeResult(CourseMergeOutcome.Busy);
            }

            var moves = new Dictionary<Guid, Guid>();
            var unmatched = new List<UnmatchedTeeVersionDto>();
            foreach (var used in usage)
            {
                var version = source.Tees.SelectMany(t => t.Versions).SingleOrDefault(v => v.Id == used.VersionId);
                if (version is null)
                {
                    // A version added to the source after it was loaded: report it rather than guess.
                    var added = await db.TeeVersions.AsNoTracking().Include(v => v.Tee).SingleAsync(v => v.Id == used.VersionId, ct);
                    unmatched.Add(Unmatched(added.Tee, added, used.Count, "NoMatchingVersion"));
                    continue;
                }

                var tee = version.Tee;
                var matches = teeMatches[tee.Id];
                var match = matches.Count == 1 ? matches[0].Versions.SingleOrDefault(v => v.EffectiveFrom == version.EffectiveFrom) : null;
                if (match is not null)
                {
                    // Rounds keep their hole results, so the target version must have every hole number they played.
                    var targetNumbers = match.Holes.Select(h => h.Number).ToList();
                    var missing = await db.HoleResults
                        .AnyAsync(h => h.Round.TeeVersionId == version.Id && !targetNumbers.Contains(h.HoleNumber), ct);
                    if (missing)
                    {
                        unmatched.Add(Unmatched(tee, version, used.Count, "MissingHoles"));
                        continue;
                    }

                    moves[version.Id] = match.Id;
                    continue;
                }

                var reason = matches.Count switch { 0 => "NoMatchingTee", 1 => "NoMatchingVersion", _ => "AmbiguousTee" };
                unmatched.Add(Unmatched(tee, version, used.Count, reason));
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
                var rounds = db.Rounds.IgnoreQueryFilters().Where(r => r.TeeVersionId == from);
                movedRoundIds.AddRange(await rounds.Select(r => r.Id).ToListAsync(ct));
                await rounds.ExecuteUpdateAsync(
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
            flag.TeeId = flag.TeeId is { } teeId && teeMatches.TryGetValue(teeId, out var matches) && matches.Count == 1
                ? matches[0].Id
                : null;
        }

        await db.Users.Where(a => a.HomeCourseId == courseId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.HomeCourseId, intoCourseId), ct);
        await db.Courses.Where(c => c.MergedIntoCourseId == courseId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.MergedIntoCourseId, intoCourseId), ct);

        source.MergedIntoCourseId = target.Id;
        source.UpdatedAt = now;
        // The target gained rounds (and maybe a home course), so sync re-sends it to golfers who already had it.
        target.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        // Shot-by-shot summaries depend on the hole data (par for GIR, driving holes), which the target may differ in.
        await rederiver.RederiveRoundsAsync(movedRoundIds, ct);
        await transaction.CommitAsync(ct);

        return new CourseMergeResult(
            CourseMergeOutcome.Merged,
            new CourseMergeResultDto(courseId, intoCourseId, movedRoundIds.Distinct().Count(), openFlags.Count));
    }

    private static UnmatchedTeeVersionDto Unmatched(Tee tee, TeeVersion version, int rounds, string reason) =>
        new(tee.Id, tee.Name, tee.Gender, version.Id, version.EffectiveFrom, rounds, reason);

    private Task<Course?> LoadAsync(Guid id, CancellationToken ct) =>
        db.Courses.AsSplitQuery()
            .Include(c => c.Tees).ThenInclude(t => t.Versions).ThenInclude(v => v.Holes)
            .SingleOrDefaultAsync(c => c.Id == id, ct);
}
