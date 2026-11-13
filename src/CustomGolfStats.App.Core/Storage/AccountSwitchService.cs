namespace CustomGolfStats.App.Core.Storage;

/// <summary>What would be lost if the local data were cleared for a different account: unsynced rounds, courses created on this
/// device and not yet saved to the account, and holes being entered shot by shot that were never saved.</summary>
public sealed record AccountSwitchPrompt(int UnsyncedRounds, int PendingCourses, int UnsavedHoles = 0)
{
    public bool HasUnsyncedWork => UnsyncedRounds + PendingCourses + UnsavedHoles > 0;
}

/// <summary>
/// Keeps one account's data from leaking into another. The signed-in account is stored with the sync state. When a different account
/// signs in: with nothing unsynced the old data is cleared at once; with unsynced rounds, courses or hole drafts a prompt is returned
/// and syncing is blocked until the golfer confirms (everything local is cleared) or cancels (the new sign-in is undone by the
/// caller). The pending state is stored, so it survives the app being closed with the question unanswered.
/// <para>A sign-in is bracketed: <see cref="BeginSignInAsync"/> stores a blocking marker BEFORE any token is saved, and only
/// <see cref="CheckAsync"/> (or a confirmed switch) removes it. Sync refuses to run while any marker is present or before an account
/// was ever verified (<see cref="IsSyncAllowedAsync"/>), so a crash or exception between saving the tokens and checking the account
/// can never send one account's unsynced rounds to another.</para>
/// </summary>
public sealed class AccountSwitchService(LocalDb db)
{
    internal const string AccountKey = "account";
    internal const string PendingKey = "pendingAccount";

    /// <summary>The pending value while a sign-in has saved (or may have saved) tokens whose account was not checked yet. Not an
    /// email, so it can never equal an account key.</summary>
    internal const string UnverifiedMarker = "<unverified sign-in>";

    /// <summary>Normalised form of an email, used as the account key.</summary>
    public static string KeyFor(string email) => email.Trim().ToLowerInvariant();

    /// <summary>Call before a sign-in can store tokens. Blocks sync until <see cref="CheckAsync"/> verifies the account. If an
    /// account-switch question is already open, it stays (it blocks sync too).</summary>
    public async Task BeginSignInAsync()
    {
        if (await db.SyncState.GetAsync(PendingKey).ConfigureAwait(false) is null)
            await db.SyncState.SetAsync(PendingKey, UnverifiedMarker).ConfigureAwait(false);
    }

    /// <summary>The sign-in did not complete and its tokens were removed: lift the marker <see cref="BeginSignInAsync"/> set.</summary>
    public async Task AbandonSignInAsync()
    {
        if (await db.SyncState.GetAsync(PendingKey).ConfigureAwait(false) == UnverifiedMarker)
            await db.SyncState.SetAsync(PendingKey, null).ConfigureAwait(false);
    }

    /// <summary>True when a sign-in stored tokens but its account was never checked (the app was closed or failed in between). The
    /// caller must sign out and then call <see cref="AbandonSignInAsync"/>.</summary>
    public async Task<bool> HasUnverifiedSignInAsync() =>
        await db.SyncState.GetAsync(PendingKey).ConfigureAwait(false) == UnverifiedMarker;

    /// <summary>Sync may run only when an account was verified for the stored session and no question or check is outstanding.</summary>
    public static async Task<bool> IsSyncAllowedAsync(LocalDb db) =>
        await db.SyncState.GetAsync(PendingKey).ConfigureAwait(false) is null
        && await db.SyncState.GetAsync(AccountKey).ConfigureAwait(false) is not null;

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
        if (prompt.HasUnsyncedWork)
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
        return key is null or UnverifiedMarker ? null : (key, await DescribeAsync().ConfigureAwait(false));
    }

    /// <summary>The golfer agreed: clear the previous account's data from this device and adopt the new account.</summary>
    public Task ConfirmSwitchAsync(string accountKey) => ReplaceAsync(accountKey);

    /// <summary>The golfer declined: forget the question (the caller signs the new session out first). The old data stays.</summary>
    public Task CancelSwitchAsync() => db.SyncState.SetAsync(PendingKey, null);

    private async Task ReplaceAsync(string accountKey)
    {
        await db.ClearAccountDataAsync().ConfigureAwait(false); // the sync state goes too, including any pending marker
        await db.SyncState.SetAsync(AccountKey, accountKey).ConfigureAwait(false);
    }

    private async Task<AccountSwitchPrompt> DescribeAsync() =>
        new(await db.Rounds.CountDirtyAsync().ConfigureAwait(false),
            (await db.Courses.GetPendingAsync().ConfigureAwait(false)).Count,
            await db.Drafts.CountHolesAsync().ConfigureAwait(false));
}
