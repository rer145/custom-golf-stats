using CustomGolfStats.Api.Data;
using CustomGolfStats.Domain.Holes;
using CustomGolfStats.Domain.Stats;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Features.Stats;

/// <summary>Loads a golfer's rounds as domain <see cref="RoundData"/> for the stats engine.</summary>
public sealed class RoundDataLoader(AppDbContext db)
{
    /// <summary>
    /// All of the golfer's rounds that are not soft-deleted. Course and tee names come from the current entities;
    /// par for each hole comes from the round's own tee version.
    /// </summary>
    public async Task<IReadOnlyList<RoundData>> LoadAsync(Guid accountId, CancellationToken ct = default)
    {
        var rounds = await db.Rounds
            .AsNoTracking()
            .AsSplitQuery()
            .Where(r => r.AccountId == accountId && r.DeletedAt == null)
            .Include(r => r.HoleResults)
            .Include(r => r.TeeVersion).ThenInclude(v => v.Holes)
            .Include(r => r.TeeVersion).ThenInclude(v => v.Tee).ThenInclude(t => t.Course)
            .OrderBy(r => r.PlayedOn).ThenBy(r => r.Id)
            .ToListAsync(ct);

        return rounds.Select(r =>
        {
            var tee = r.TeeVersion.Tee;
            var parByHole = r.TeeVersion.Holes.ToDictionary(h => h.Number, h => h.Par);
            var holes = r.HoleResults
                .OrderBy(h => h.EntrySequence)
                .Select(h => new HoleSummary(h.HoleNumber, h.EntrySequence, parByHole[h.HoleNumber], h.Score, h.Putts,
                    h.FairwayHit, h.TeeShotLeft, h.TeeShotRight, h.TeeShotLong, h.TeeShotShort,
                    h.Gir, h.PenaltyStrokes, h.GreensideSandShots, h.FairwaySandShots))
                .ToList();
            return new RoundData(r.Id, r.PlayedOn, tee.CourseId, tee.Course.Name, tee.Id, tee.Name, holes);
        }).ToList();
    }
}
