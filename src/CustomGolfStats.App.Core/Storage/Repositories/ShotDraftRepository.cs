using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.Domain.Shots;
using SQLite;

namespace CustomGolfStats.App.Core.Storage.Repositories;

/// <summary>A restored draft: which hole it was for and its shots in order.</summary>
public sealed record ShotDraft(int HoleNumber, IReadOnlyList<Shot> Shots);

/// <summary>The shots entered so far for a hole that has not been saved yet.</summary>
public sealed class ShotDraftRepository(SQLiteAsyncConnection db)
{
    /// <summary>Replaces the draft for (round, entry sequence). An empty list clears it.</summary>
    public Task SaveAsync(Guid roundId, int entrySequence, int holeNumber, IReadOnlyList<Shot> shots) => db.RunInTransactionAsync(c =>
    {
        c.Execute("DELETE FROM ShotDraftRow WHERE RoundId = ? AND EntrySequence = ?", roundId, entrySequence);
        foreach (var shot in shots)
            c.Insert(ShotDraftRow.From(roundId, entrySequence, holeNumber, shot));
    });

    public async Task<ShotDraft?> GetAsync(Guid roundId, int entrySequence)
    {
        var rows = await db.Table<ShotDraftRow>().Where(r => r.RoundId == roundId && r.EntrySequence == entrySequence)
            .OrderBy(r => r.Sequence).ToListAsync().ConfigureAwait(false);
        return rows.Count == 0 ? null : new ShotDraft(rows[0].HoleNumber, rows.Select(r => r.ToShot()).ToList());
    }

    public Task ClearAsync(Guid roundId, int entrySequence) =>
        db.ExecuteAsync("DELETE FROM ShotDraftRow WHERE RoundId = ? AND EntrySequence = ?", roundId, entrySequence);

    public Task ClearForRoundAsync(Guid roundId) => db.ExecuteAsync("DELETE FROM ShotDraftRow WHERE RoundId = ?", roundId);
}
