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
    /// <summary>Saves a local change: replaces the round's holes and shots, sets Dirty and ClientUpdatedAt = now.
    /// Server-owned fields (Version, ModifiedAt, TrialCounted) are taken from the stored row, not from <paramref name="record"/>.
    /// The caller's objects are not modified; the write is one transaction, so a failure leaves the stored round untouched.</summary>
    public Task SaveAsync(RoundRecord record)
    {
        var now = clock.UtcNow;
        return db.RunInTransactionAsync(c =>
        {
            var round = record.Round.Clone();
            if (c.Find<RoundRow>(round.Id) is { } stored)
            {
                if (stored.DeletedAt is not null)
                    throw new InvalidOperationException($"Round {round.Id} is deleted and cannot be edited.");
                round.Version = stored.Version;
                round.ModifiedAt = stored.ModifiedAt;
                round.TrialCounted = stored.TrialCounted;
            }
            round.ClientUpdatedAt = now;
            round.Dirty = true;
            Write(c, record with { Round = round });
        });
    }

    /// <summary>
    /// Stores a copy received from the server, subject to <paramref name="mode"/> so unsent local edits are never lost. Returns true
    /// when the copy was stored. A stored copy is clean with its own ClientUpdatedAt/Version/ModifiedAt; a DeletedAt copy becomes a
    /// clean tombstone with no holes or shots.
    /// </summary>
    public async Task<bool> SaveFromServerAsync(RoundRecord record, ServerWriteMode mode)
    {
        var stored = false;
        await db.RunInTransactionAsync(c =>
        {
            var local = c.Find<RoundRow>(record.Round.Id);
            switch (mode)
            {
                case ServerWriteMode.PullMode when local is { Dirty: true }:
                    return; // unsent local edit (or pending delete) wins until it is pushed
                case ServerWriteMode.PushAcceptedMode when local is { DeletedAt: not null }:
                    return; // deleted locally: the DELETE is still to be sent, whatever the pushed clock value
                case ServerWriteMode.PushAcceptedMode when local is null:
                    return; // the round was purged while its push was in flight: never bring it back
                case ServerWriteMode.PushAcceptedMode accepted when local.ClientUpdatedAt != accepted.PushedClientUpdatedAt:
                    // Edited (or deleted) during the push: keep the local round dirty, but record what the server now owns.
                    local.Version = record.Round.Version;
                    local.ModifiedAt = record.Round.ModifiedAt;
                    local.TrialCounted = record.Round.TrialCounted;
                    c.Update(local);
                    return;
            }

            var copy = record.Round.Clone();
            copy.Dirty = false;
            Write(c, record with { Round = copy });
            stored = true;
        });
        return stored;
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

    /// <summary>Tombstones the round (even one never synced, since its push may be in flight): DeletedAt and ClientUpdatedAt = now, dirty (a DELETE is pending), holes and shots removed.
    /// Unknown ids are ignored. After the DELETE is pushed
    /// (a 404 counts as success) call <see cref="PurgeTombstoneAsync"/>.</summary>
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

    /// <summary>Removes a tombstone row once its DELETE has been pushed. Returns false (and removes nothing) if the round is
    /// missing or is not a tombstone.</summary>
    public async Task<bool> PurgeTombstoneAsync(Guid roundId)
    {
        var purged = false;
        await db.RunInTransactionAsync(c =>
        {
            if (c.Find<RoundRow>(roundId) is not { DeletedAt: not null })
                return;
            DeleteChildren(c, roundId);
            c.Delete<RoundRow>(roundId);
            purged = true;
        });
        return purged;
    }

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
        var holeRow = hole.Hole.Clone();
        holeRow.Id = 0;
        holeRow.RoundId = roundId;
        c.Insert(holeRow);
        foreach (var source in hole.Shots.OrderBy(s => s.Sequence))
        {
            var shot = source.Clone();
            shot.Id = 0;
            shot.RoundId = roundId;
            shot.HoleNumber = holeRow.HoleNumber;
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

/// <summary>How a server copy may overwrite the local round.</summary>
public abstract record ServerWriteMode
{
    private ServerWriteMode() { }

    /// <summary>A pulled change: skipped (returns false) when the stored round is dirty.</summary>
    public static ServerWriteMode Pull { get; } = new PullMode();

    /// <summary>The server accepted a push of the round as it was at <paramref name="pushedClientUpdatedAt"/>: stored only if the
    /// local round exists and has not been edited since; otherwise the local round stays dirty (or stays gone).</summary>
    public static ServerWriteMode PushAccepted(DateTimeOffset pushedClientUpdatedAt) => new PushAcceptedMode(pushedClientUpdatedAt);

    /// <summary>The 409 case: the server copy replaces the local round unconditionally.</summary>
    public static ServerWriteMode ServerWins { get; } = new ServerWinsMode();

    internal sealed record PullMode : ServerWriteMode;
    internal sealed record PushAcceptedMode(DateTimeOffset PushedClientUpdatedAt) : ServerWriteMode;
    internal sealed record ServerWinsMode : ServerWriteMode;
}
