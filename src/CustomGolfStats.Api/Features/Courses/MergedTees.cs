using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Features.Courses;

/// <summary>How tees of a merged (duplicate) course map onto the course it was merged into.</summary>
public static class TeeMatching
{
    /// <summary>Same gender and same name, trimmed and case-insensitive.</summary>
    public static bool IsSameTee(Tee a, Tee b) => a.Gender == b.Gender && SameName(a.Name, b.Name);

    /// <summary>Every tee in <paramref name="candidates"/> that matches <paramref name="tee"/> (more than one is ambiguous).</summary>
    public static List<Tee> Matches(Tee tee, IEnumerable<Tee> candidates) => candidates.Where(c => IsSameTee(tee, c)).ToList();

    private static bool SameName(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <param name="TeeId">The tee to use: the requested one, or its match on the course its course was merged into.</param>
/// <param name="Unmatched">True when the tee's course was merged and the target has no single matching tee.</param>
public sealed record TeeRedirect(Guid TeeId, bool Unmatched = false);

/// <summary>Follows <see cref="Course.MergedIntoCourseId"/> so tees of a merged course resolve to the surviving course.</summary>
public sealed class MergedTeeRedirector(AppDbContext db)
{
    private const int MaxHops = 10;

    /// <summary>
    /// Returns <paramref name="teeId"/> unchanged when it is unknown or its course isn't merged; otherwise the single
    /// matching tee (<see cref="TeeMatching"/>) on the course at the end of the merge chain, or Unmatched.
    /// </summary>
    public async Task<TeeRedirect> RedirectAsync(Guid teeId, CancellationToken ct)
    {
        var tee = await db.Tees.AsNoTracking().Include(t => t.Course).SingleOrDefaultAsync(t => t.Id == teeId, ct);
        if (tee is null)
        {
            return new TeeRedirect(teeId);
        }

        for (var hop = 0; tee.Course.MergedIntoCourseId is { } nextCourseId; hop++)
        {
            if (hop == MaxHops)
            {
                return new TeeRedirect(teeId, Unmatched: true);
            }

            var gender = tee.Gender;
            var candidates = await db.Tees.AsNoTracking().Include(t => t.Course)
                .Where(t => t.CourseId == nextCourseId && t.Gender == gender)
                .ToListAsync(ct);
            var matches = TeeMatching.Matches(tee, candidates);
            if (matches.Count != 1)
            {
                return new TeeRedirect(teeId, Unmatched: true);
            }

            tee = matches[0];
        }

        return new TeeRedirect(tee.Id);
    }
}
