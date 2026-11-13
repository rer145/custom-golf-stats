using CustomGolfStats.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Migration.Import;

public static class AccountLookup
{
    /// <summary>
    /// The id of the account with <paramref name="email"/>, normalised the way Identity does (trimmed, upper invariant);
    /// a <see cref="MigrationUserException"/> when there is none. Accounts are never created or changed here.
    /// </summary>
    public static async Task<Guid> FindAsync(AppDbContext db, string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = new UpperInvariantLookupNormalizer().NormalizeEmail(email.Trim());
        return await db.Users
            .Where(a => a.NormalizedEmail == normalizedEmail)
            .Select(a => (Guid?)a.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new MigrationUserException(
                $"No account has the email {email.Trim()}. Create the account through the app or API first.");
    }
}
