using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Features.Auth;
using CustomGolfStats.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Features.Me;

/// <summary>The outcome of <see cref="AccountDeleter.DeleteAsync"/>.</summary>
public enum AccountDeletionOutcome
{
    Deleted,

    /// <summary>The account no longer exists (already deleted).</summary>
    NotFound,

    /// <summary>Neither a password nor an external ID token was sent, or both were.</summary>
    ConfirmationMissing,

    /// <summary>The password is wrong, or the account has no password.</summary>
    PasswordRejected,

    /// <summary>Too many wrong passwords: the account is locked out for a while.</summary>
    LockedOut,

    /// <summary>The external ID token is invalid, expired, or belongs to a sign-in that isn't linked to this account.</summary>
    ExternalTokenRejected,

    /// <summary>The account is the only active admin.</summary>
    LastAdmin,
}

/// <summary>
/// Deletes an account and everything it owns, after the golfer re-confirms who they are.
/// <para>Owned rows are deleted: rounds with their hole results and shots, handicap entries, course flags the account reported,
/// and Identity's claims, external logins and stored tokens. Shared rows stay, with the reference cleared: courses it created
/// (<see cref="Course.CreatedByAccountId"/>), tee versions it changed (<see cref="TeeVersion.ChangedByAccountId"/>) and flags it
/// resolved as an admin (<see cref="CourseFlag.ResolvedByAccountId"/>). The database's foreign keys say the same (cascade or set
/// null), so the explicit statements here and the cascades agree; the explicit ones keep the rule readable and survive a future
/// change to a foreign key. Everything runs in one transaction.</para>
/// <para>Removing the account row ends every session: a refresh token is checked against the account's security stamp, which no
/// longer exists, and an access token already issued fails the active-account check every authorized request passes
/// (<see cref="ActiveAccountRequirement"/>), so it is answered 401.</para>
/// <para>An admin may delete their account only while another active (not disabled) admin exists, so the admin tools are never
/// left without anyone able to use them.</para>
/// </summary>
public sealed class AccountDeleter(
    AppDbContext db, UserManager<Account> users, IExternalTokenValidator externalTokens, AccountStatusCache status)
{
    public async Task<AccountDeletionOutcome> DeleteAsync(
        Guid accountId, string? password, string? externalProvider, string? externalIdToken, CancellationToken ct)
    {
        var hasPassword = !string.IsNullOrEmpty(password);
        var hasExternal = !string.IsNullOrWhiteSpace(externalIdToken);
        if (hasPassword == hasExternal)
        {
            return AccountDeletionOutcome.ConfirmationMissing;
        }

        var account = await users.FindByIdAsync(accountId.ToString());
        if (account is null)
        {
            return AccountDeletionOutcome.NotFound;
        }

        var confirmed = hasPassword
            ? await CheckPasswordAsync(account, password!)
            : await CheckExternalAsync(account, externalProvider, externalIdToken!, ct);
        if (confirmed != AccountDeletionOutcome.Deleted)
        {
            return confirmed;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        if (account.Role == Role.Admin && !await AnotherActiveAdminAsync(account.Id, ct))
        {
            return AccountDeletionOutcome.LastAdmin;
        }

        // Owned: deleted. Shots and hole results first, then the rounds that hold them.
        var rounds = db.Rounds.Where(r => r.AccountId == account.Id).Select(r => r.Id);
        await db.Shots.Where(s => rounds.Contains(s.RoundId)).ExecuteDeleteAsync(ct);
        await db.HoleResults.Where(h => rounds.Contains(h.RoundId)).ExecuteDeleteAsync(ct);
        await db.Rounds.Where(r => r.AccountId == account.Id).ExecuteDeleteAsync(ct);
        await db.HandicapEntries.Where(h => h.AccountId == account.Id).ExecuteDeleteAsync(ct);
        await db.CourseFlags.Where(f => f.ReporterAccountId == account.Id).ExecuteDeleteAsync(ct);

        // Shared: kept, with the reference to this account cleared.
        await db.Courses.Where(c => c.CreatedByAccountId == account.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedByAccountId, (Guid?)null), ct);
        await db.TeeVersions.Where(v => v.ChangedByAccountId == account.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.ChangedByAccountId, (Guid?)null), ct);
        await db.CourseFlags.Where(f => f.ResolvedByAccountId == account.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.ResolvedByAccountId, (Guid?)null), ct);

        // Identity's own rows (claims, logins, tokens) cascade with the account.
        var result = await users.DeleteAsync(account);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "The account could not be deleted: " + string.Join("; ", result.Errors.Select(e => e.Code)));
        }

        await transaction.CommitAsync(ct);
        status.Evict(account.Id); // access tokens already issued are refused from the next request on
        return AccountDeletionOutcome.Deleted;
    }

    /// <summary>
    /// Locks the active admin rows (<c>FOR UPDATE</c>) for the rest of the transaction, so two admins deleting themselves at the
    /// same moment can't both see the other as the remaining admin: the second waits, then no longer finds the first.
    /// </summary>
    private async Task<bool> AnotherActiveAdminAsync(Guid accountId, CancellationToken ct)
    {
        var admins = await db.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM accounts WHERE role = {nameof(Role.Admin)} AND disabled_at IS NULL FOR UPDATE")
            .ToListAsync(ct);
        return admins.Any(id => id != accountId);
    }

    /// <summary>Wrong passwords count towards Identity's lockout, so a stolen access token can't be used to guess the password.</summary>
    private async Task<AccountDeletionOutcome> CheckPasswordAsync(Account account, string password)
    {
        if (!await users.HasPasswordAsync(account))
        {
            return AccountDeletionOutcome.PasswordRejected;
        }

        if (await users.IsLockedOutAsync(account))
        {
            return AccountDeletionOutcome.LockedOut;
        }

        if (await users.CheckPasswordAsync(account, password))
        {
            // Earlier wrong guesses don't linger if the deletion is then refused (only admin) and the golfer stays.
            await users.ResetAccessFailedCountAsync(account);
            return AccountDeletionOutcome.Deleted;
        }

        await users.AccessFailedAsync(account);
        return await users.IsLockedOutAsync(account) ? AccountDeletionOutcome.LockedOut : AccountDeletionOutcome.PasswordRejected;
    }

    /// <summary>
    /// The token must be valid now (signature, issuer, audience and expiry, which keeps it fresh: Apple's last minutes, Google's
    /// an hour) and must name an external sign-in already linked to this account.
    /// </summary>
    private async Task<AccountDeletionOutcome> CheckExternalAsync(Account account, string? provider, string idToken, CancellationToken ct)
    {
        provider = provider?.Trim().ToLowerInvariant();
        if (provider is not (ExternalTokenValidator.Apple or ExternalTokenValidator.Google))
        {
            return AccountDeletionOutcome.ExternalTokenRejected;
        }

        var identity = await externalTokens.ValidateAsync(provider, idToken, ct);
        if (identity is null)
        {
            return AccountDeletionOutcome.ExternalTokenRejected;
        }

        var owner = await users.FindByLoginAsync(identity.Provider, identity.Subject);
        return owner?.Id == account.Id ? AccountDeletionOutcome.Deleted : AccountDeletionOutcome.ExternalTokenRejected;
    }
}
