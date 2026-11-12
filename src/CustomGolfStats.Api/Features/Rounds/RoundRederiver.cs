using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Domain;
using CustomGolfStats.Domain.Holes;
using CustomGolfStats.Domain.Shots;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Features.Rounds;

/// <summary>
/// Re-derives the stored hole summaries of shot-by-shot rounds from their stored shots with
/// <see cref="HoleSummaryDeriver"/>, using the hole data (par, yardage, stroke index) their tee version has now. Used
/// after an admin corrects a version in place or a merge moves rounds to another version. Quick rounds are never touched:
/// their summaries are what the golfer entered.
/// </summary>
public sealed class RoundRederiver(AppDbContext db, TimeProvider time)
{
    /// <summary>Every shot-by-shot round (soft-deleted included) on <paramref name="versionIds"/>. Saves; returns the count.</summary>
    public Task<int> RederiveForVersionsAsync(IReadOnlyCollection<Guid> versionIds, CancellationToken ct) =>
        RederiveAsync(db.Rounds.IgnoreQueryFilters().Where(r => versionIds.Contains(r.TeeVersionId)), ct);

    /// <summary>The shot-by-shot rounds among <paramref name="roundIds"/> (soft-deleted included). Saves; returns the count.</summary>
    public Task<int> RederiveRoundsAsync(IReadOnlyCollection<Guid> roundIds, CancellationToken ct) =>
        RederiveAsync(db.Rounds.IgnoreQueryFilters().Where(r => roundIds.Contains(r.Id)), ct);

    private async Task<int> RederiveAsync(IQueryable<Round> rounds, CancellationToken ct)
    {
        var shotRounds = await rounds
            .Where(r => r.EntryMode == EntryMode.ShotByShot)
            .Include(r => r.TeeVersion).ThenInclude(v => v.Holes)
            .Include(r => r.HoleResults).ThenInclude(h => h.Shots)
            .AsSplitQuery()
            .ToListAsync(ct);
        if (shotRounds.Count == 0)
        {
            return 0;
        }

        var now = time.GetUtcNow();
        foreach (var round in shotRounds)
        {
            var holesOnTee = round.TeeVersion.Holes.ToDictionary(h => h.Number);
            foreach (var result in round.HoleResults)
            {
                // A hole the corrected version no longer has keeps its last summary.
                if (!holesOnTee.TryGetValue(result.HoleNumber, out var teeHole))
                {
                    continue;
                }

                var info = new HoleInfo(teeHole.Number, teeHole.Par, teeHole.Yardage, teeHole.StrokeIndex);
                var shots = result.Shots.OrderBy(s => s.Sequence)
                    .Select(s => new Shot(s.Sequence, s.StartLie, s.StartDistance, s.Club, s.ShotType, s.EndLie, s.EndDistance,
                        s.Direction, s.Shape, s.PenaltyStrokes))
                    .ToList();
                Apply(result, HoleSummaryDeriver.Derive(info, result.EntrySequence, shots));
            }

            round.ModifiedAt = now;
            round.Version++;
        }

        await db.SaveChangesAsync(ct);
        return shotRounds.Count;
    }

    private static void Apply(HoleResult result, HoleSummary summary)
    {
        result.Score = summary.Score;
        result.Putts = summary.Putts;
        result.FairwayHit = summary.FairwayHit;
        result.TeeShotLeft = summary.TeeShotLeft;
        result.TeeShotRight = summary.TeeShotRight;
        result.TeeShotLong = summary.TeeShotLong;
        result.TeeShotShort = summary.TeeShotShort;
        result.Gir = summary.Gir;
        result.PenaltyStrokes = summary.PenaltyStrokes;
        result.GreensideSandShots = summary.GreensideSandShots;
        result.FairwaySandShots = summary.FairwaySandShots;
        result.IsDerived = true;
    }
}
