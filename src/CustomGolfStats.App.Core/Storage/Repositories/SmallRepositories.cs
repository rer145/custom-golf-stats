using System.Globalization;
using CustomGolfStats.App.Core.Platform;
using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.Domain;
using SQLite;

namespace CustomGolfStats.App.Core.Storage.Repositories;

public sealed class HandicapRepository(SQLiteAsyncConnection db)
{
    /// <summary>Insert-or-replace by id, so receiving the same entry twice is harmless.</summary>
    public Task UpsertAsync(IEnumerable<HandicapRow> entries)
    {
        var list = entries.ToList();
        return db.RunInTransactionAsync(c => { foreach (var e in list) c.InsertOrReplace(e); });
    }

    public Task<List<HandicapRow>> ListAsync() => db.Table<HandicapRow>().OrderByDescending(h => h.EffectiveOn).ToListAsync();

    public async Task<HandicapRow?> LatestAsync() => (await db.Table<HandicapRow>().OrderByDescending(h => h.EffectiveOn).Take(1).ToListAsync()).FirstOrDefault();
}

public sealed class EntitlementsRepository(SQLiteAsyncConnection db, IClock clock)
{
    public Task<EntitlementsRow?> GetAsync() => db.FindAsync<EntitlementsRow>(EntitlementsRow.SingletonId)!;

    /// <summary>Replaces the single cached row and stamps UpdatedAt.</summary>
    public async Task SaveAsync(EntitlementsRow row)
    {
        row.Id = EntitlementsRow.SingletonId;
        row.UpdatedAt = clock.UtcNow;
        await db.InsertOrReplaceAsync(row);
    }

    /// <summary>Gives back a trial round consumed by <see cref="TryConsumeTrialRoundAsync"/> when the round could not be created.</summary>
    public Task RefundTrialRoundAsync() => db.RunInTransactionAsync(c =>
    {
        var row = c.Find<EntitlementsRow>(EntitlementsRow.SingletonId);
        if (row is not { TrialShotRoundsRemaining: { } remaining })
            return;
        row.TrialShotRoundsRemaining = remaining + 1;
        row.ShotByShotEntry = true;
        row.UpdatedAt = clock.UtcNow;
        c.Update(row);
    });

    /// <summary>
    /// Offline start of a shot-by-shot round: true when allowed. Paid (or unlimited) consumes nothing; a free golfer's cached
    /// remaining count is decremented (never below zero) and the feature closes at zero. Nothing cached means no.
    /// The server stays the source of truth and overwrites the cache on the next sync.
    /// </summary>
    public async Task<bool> TryConsumeTrialRoundAsync()
    {
        var allowed = false;
        await db.RunInTransactionAsync(c =>
        {
            var row = c.Find<EntitlementsRow>(EntitlementsRow.SingletonId);
            if (row is null || !row.ShotByShotEntry)
                return;
            allowed = true;
            if (row.TrialShotRoundsRemaining is not { } remaining)
                return;
            if (remaining <= 0)
            {
                allowed = false;
                return;
            }
            row.TrialShotRoundsRemaining = remaining - 1;
            if (row.TrialShotRoundsRemaining == 0)
                row.ShotByShotEntry = false;
            row.UpdatedAt = clock.UtcNow;
            c.Update(row);
        });
        return allowed;
    }
}

public sealed class SyncStateRepository(SQLiteAsyncConnection db)
{
    private const string NextSinceKey = "nextSince";

    public async Task<string?> GetAsync(string key) => (await db.FindAsync<SyncStateRow>(key))?.Value;

    public Task SetAsync(string key, string? value) => db.InsertOrReplaceAsync(new SyncStateRow { Key = key, Value = value });

    /// <summary>The server's <c>nextSince</c> from the last successful pull, or null before the first sync.</summary>
    public async Task<DateTimeOffset?> GetNextSinceAsync() =>
        await GetAsync(NextSinceKey) is { } text ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture) : null;

    public Task SetNextSinceAsync(DateTimeOffset value) => SetAsync(NextSinceKey, value.ToString("O", CultureInfo.InvariantCulture));
}
