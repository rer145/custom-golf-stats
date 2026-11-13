namespace CustomGolfStats.App.Core.Storage;

/// <summary>What would be lost if the local data were cleared for a different account.</summary>
public sealed record AccountSwitchPrompt(int UnsyncedRounds, int PendingCourses);

/// <summary>
/// Keeps one account's data from leaking into another. The signed-in account is stored with the sync state. When a different account
/// signs in: with nothing unsynced the old data is cleared at once; with unsynced rounds or courses a prompt is returned and syncing
/// is blocked until the golfer confirms (everything local is cleared) or cancels (the new sign-in is undone by the caller). The
/// pending state is stored, so it survives the app being closed with the question unanswered.
/// </summary>
public sealed class AccountSwitchService(LocalDb db)
{
    private const string AccountKey = "account";
    internal const string PendingKey = "pendingAccount";

    /// <summary>Normalised form of an email, used as the account key.</summary>
    public static string KeyFor(string email) => email.Trim().ToLowerInvariant();

    /// <summary>Call right after a successful sign-in. Null means carry on; a prompt means ask the golfer first
    /// (call <see cref="ConfirmSwitchAsync"/> or <see cref="CancelSwitchAsync"/>).</summary>
    public async Task<AccountSwitchPrompt?> CheckAsync(string accountKey)
    {
        var stored = await db.SyncState.GetAsync(AccountKey).ConfigureAwait(false);
        if (stored is null || stored == accountKey)
        {
            // First sign-in on this device (any rounds already here were made before signing in) or the same account again.
            await db.SyncState.SetAsync(AccountKey, accountKey).ConfigureAwait(false);
            await db.SyncState.SetAsync(PendingKey, null).ConfigureAwait(false);
            return null;
        }

        var prompt = await DescribeAsync().ConfigureAwait(false);
        if (prompt.UnsyncedRounds + prompt.PendingCourses > 0)
        {
            await db.SyncState.SetAsync(PendingKey, accountKey).ConfigureAwait(false);
            return prompt;
        }

        await ReplaceAsync(accountKey).ConfigureAwait(false);
        return null;
    }

    /// <summary>The unanswered question left by a previous run, if any: the new account's key and what would be lost.</summary>
    public async Task<(string AccountKey, AccountSwitchPrompt Prompt)?> GetPendingAsync()
    {
        var key = await db.SyncState.GetAsync(PendingKey).ConfigureAwait(false);
        return key is null ? null : (key, await DescribeAsync().ConfigureAwait(false));
    }

    /// <summary>The golfer agreed: clear the previous account's data from this device and adopt the new account.</summary>
    public Task ConfirmSwitchAsync(string accountKey) => ReplaceAsync(accountKey);

    /// <summary>The golfer declined: forget the question (the caller signs the new session out). The old data stays.</summary>
    public Task CancelSwitchAsync() => db.SyncState.SetAsync(PendingKey, null);

    private async Task ReplaceAsync(string accountKey)
    {
        await db.ClearAccountDataAsync().ConfigureAwait(false);
        await db.SyncState.SetAsync(AccountKey, accountKey).ConfigureAwait(false);
    }

    private async Task<AccountSwitchPrompt> DescribeAsync() =>
        new(await db.Rounds.CountDirtyAsync().ConfigureAwait(false), (await db.Courses.GetPendingAsync().ConfigureAwait(false)).Count);
}
