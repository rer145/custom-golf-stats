using CustomGolfStats.Api.Data;
using CustomGolfStats.Domain.Holes;
using CustomGolfStats.Domain.Stats;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Features.Stats;

/// <summary>Loads a golfer's rounds as domain <see cref="RoundData"/> for the stats engine.</summary>
public sealed partial class RoundDataLoader(AppDbContext db, ILogger<RoundDataLoader> logger)
{
    /// <summary>
    /// All of the golfer's rounds that are not soft-deleted. Course and tee names come from the current entities;
    /// par for each hole comes from the round's own tee version. A hole result whose hole number the version doesn't have
    /// (which edits and merges guard against) is skipped and logged rather than failing every stats request.
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
            var holes = new List<HoleSummary>();
            foreach (var h in r.HoleResults.OrderBy(h => h.EntrySequence))
            {
                if (!parByHole.TryGetValue(h.HoleNumber, out var par))
                {
                    LogHoleMissing(r.Id, h.HoleNumber, r.TeeVersionId);
                    continue;
                }

                holes.Add(new HoleSummary(h.HoleNumber, h.EntrySequence, par, h.Score, h.Putts,
                    h.FairwayHit, h.TeeShotLeft, h.TeeShotRight, h.TeeShotLong, h.TeeShotShort,
                    h.Gir, h.PenaltyStrokes, h.GreensideSandShots, h.FairwaySandShots));
            }

            return new RoundData(r.Id, r.PlayedOn, tee.CourseId, tee.Course.Name, tee.Id, tee.Name, holes);
        }).ToList();
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Round {RoundId} has a result for hole {HoleNumber}, which tee version {TeeVersionId} lacks; skipped in stats.")]
    private partial void LogHoleMissing(Guid roundId, int holeNumber, Guid teeVersionId);
}
