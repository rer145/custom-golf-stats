using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Features.Courses;

public sealed class TeeVersionService(AppDbContext db)
{
    /// <summary>
    /// The version of <paramref name="teeId"/> in effect on <paramref name="playedOn"/>: the latest with
    /// <c>EffectiveFrom &lt;= playedOn</c>, else the earliest. Holes are included. Null when the tee has no versions
    /// (or doesn't exist). The entity is tracked.
    /// </summary>
    public async Task<TeeVersion?> ResolveAsync(Guid teeId, DateOnly playedOn, CancellationToken ct)
    {
        var versions = db.TeeVersions.Include(v => v.Holes).Where(v => v.TeeId == teeId);
        return await versions.Where(v => v.EffectiveFrom <= playedOn).OrderByDescending(v => v.EffectiveFrom).FirstOrDefaultAsync(ct)
            ?? await versions.OrderBy(v => v.EffectiveFrom).FirstOrDefaultAsync(ct);
    }
}
