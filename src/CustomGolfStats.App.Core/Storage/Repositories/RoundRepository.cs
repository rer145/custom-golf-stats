using CustomGolfStats.App.Core.Platform;
using CustomGolfStats.App.Core.Storage.Rows;
using SQLite;

namespace CustomGolfStats.App.Core.Storage.Repositories;

/// <summary>
/// Rounds with their holes and shots. Every local write marks the round dirty and sets <c>ClientUpdatedAt</c> to now;
/// <see cref="SaveFromServerAsync"/> stores a server copy clean. A deleted round stays as a tombstone row.
/// </summary>
public sealed class RoundRepository(SQLiteAsyncConnection db, IClock clock)
{
    /// <summary>Saves a local change: replaces the round's holes and shots, sets Dirty and ClientUpdatedAt = now (on <paramref name="record"/>'s row too).</summary>
    public Task SaveAsync(RoundRecord record)
    {
        record.Round.ClientUpdatedAt = clock.UtcNow;
        record.Round.Dirty = true;
        return db.RunInTransactionAsync(c => Write(c, record));
    }

    /// <summary>Stores a copy received from the server: clean, with its own ClientUpdatedAt/Version/ModifiedAt untouched.
    /// A copy with DeletedAt set becomes a clean tombstone with no holes or shots.</summary>
    public Task SaveFromServerAsync(RoundRecord record)
    {
        record.Round.Dirty = false;
        return db.RunInTransactionAsync(c => Write(c, record));
    }

    /// <summary>Replaces one hole (and its shots) and marks the round dirty. The round must exist.</summary>
    public Task SaveHoleAsync(Guid roundId, HoleRecord hole) => db.RunInTransactionAsync(c =>
    {
        var round = RequireLive(c, roundId);
        DeleteHole(c, roundId, hole.Hole.HoleNumber);
        InsertHole(c, roundId, hole);
        Touch(c, round);
    });

    public Task DeleteHoleAsync(Guid roundId, int holeNumber) => db.RunInTransactionAsync(c =>
    {
        var round = RequireLive(c, roundId);
        DeleteHole(c, roundId, holeNumber);
        Touch(c, round);
    });

    /// <summary>Tombstones the round: DeletedAt and ClientUpdatedAt = now, dirty (a DELETE is pending), holes and shots removed.
    /// Unknown ids are ignored.</summary>
    public Task MarkDeletedAsync(Guid roundId) => db.RunInTransactionAsync(c =>
    {
        var round = c.Find<RoundRow>(roundId);
        if (round is null)
            return;
        var now = clock.UtcNow;
        round.DeletedAt = now;
        round.ClientUpdatedAt = now;
        round.Dirty = true;
        c.Update(round);
        DeleteChildren(c, roundId);
    });

    /// <summary>Clears the dirty flag after a successful push. When <paramref name="ifClientUpdatedAtIs"/> is given the flag is
    /// cleared only if the round has not been edited since that value was pushed; returns whether it was cleared.</summary>
    public async Task<bool> MarkCleanAsync(Guid roundId, DateTimeOffset? ifClientUpdatedAtIs = null)
    {
        var cleared = false;
        await db.RunInTransactionAsync(c =>
        {
            var round = c.Find<RoundRow>(roundId);
            if (round is null || (ifClientUpdatedAtIs is { } expected && round.ClientUpdatedAt != expected))
                return;
            round.Dirty = false;
            c.Update(round);
            cleared = true;
        });
        return cleared;
    }

    public async Task<RoundRecord?> GetAsync(Guid roundId, bool includeDeleted = false)
    {
        var round = await db.FindAsync<RoundRow>(roundId);
        if (round is null || (round.DeletedAt is not null && !includeDeleted))
            return null;
        return (await LoadAsync([round]))[0];
    }

    /// <summary>Round headers newest first, tombstones excluded.</summary>
    public Task<List<RoundRow>> ListAsync() =>
        db.Table<RoundRow>().Where(r => r.DeletedAt == null).OrderByDescending(r => r.PlayedOn).ThenByDescending(r => r.ClientUpdatedAt).ToListAsync();

    /// <summary>Rounds (and tombstones) with changes not yet pushed, oldest edit first, with holes and shots.</summary>
    public async Task<List<RoundRecord>> GetDirtyAsync()
    {
        var rows = await db.Table<RoundRow>().Where(r => r.Dirty).OrderBy(r => r.ClientUpdatedAt).ToListAsync();
        return await LoadAsync(rows);
    }

    public Task<int> CountDirtyAsync() => db.Table<RoundRow>().Where(r => r.Dirty).CountAsync();

    private async Task<List<RoundRecord>> LoadAsync(List<RoundRow> rounds)
    {
        var result = new List<RoundRecord>(rounds.Count);
        foreach (var round in rounds)
        {
            var holes = await db.Table<HoleRow>().Where(h => h.RoundId == round.Id).OrderBy(h => h.HoleNumber).ToListAsync();
            var shots = await db.Table<ShotRow>().Where(s => s.RoundId == round.Id).OrderBy(s => s.HoleNumber).ThenBy(s => s.Sequence).ToListAsync();
            var byHole = shots.ToLookup(s => s.HoleNumber);
            result.Add(new RoundRecord(round, holes.Select(h => new HoleRecord(h, byHole[h.HoleNumber].ToList())).ToList()));
        }
        return result;
    }

    private static void Write(SQLiteConnection c, RoundRecord record)
    {
        var id = record.Round.Id;
        c.InsertOrReplace(record.Round);
        DeleteChildren(c, id);
        if (record.Round.DeletedAt is not null)
            return;
        foreach (var hole in record.Holes.OrderBy(h => h.Hole.HoleNumber))
            InsertHole(c, id, hole);
    }

    private static void InsertHole(SQLiteConnection c, Guid roundId, HoleRecord hole)
    {
        hole.Hole.Id = 0;
        hole.Hole.RoundId = roundId;
        c.Insert(hole.Hole);
        foreach (var shot in hole.Shots.OrderBy(s => s.Sequence))
        {
            shot.Id = 0;
            shot.RoundId = roundId;
            shot.HoleNumber = hole.Hole.HoleNumber;
            c.Insert(shot);
        }
    }

    private static void DeleteHole(SQLiteConnection c, Guid roundId, int holeNumber)
    {
        c.Execute("DELETE FROM ShotRow WHERE RoundId = ? AND HoleNumber = ?", roundId, holeNumber);
        c.Execute("DELETE FROM HoleRow WHERE RoundId = ? AND HoleNumber = ?", roundId, holeNumber);
    }

    private static void DeleteChildren(SQLiteConnection c, Guid roundId)
    {
        c.Execute("DELETE FROM ShotRow WHERE RoundId = ?", roundId);
        c.Execute("DELETE FROM HoleRow WHERE RoundId = ?", roundId);
    }

    private static RoundRow RequireLive(SQLiteConnection c, Guid roundId)
    {
        var round = c.Find<RoundRow>(roundId);
        if (round is null || round.DeletedAt is not null)
            throw new InvalidOperationException($"Round {roundId} does not exist locally.");
        return round;
    }

    private void Touch(SQLiteConnection c, RoundRow round)
    {
        round.ClientUpdatedAt = clock.UtcNow;
        round.Dirty = true;
        c.Update(round);
    }
}
