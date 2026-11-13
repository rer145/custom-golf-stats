using CustomGolfStats.Api.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Features.Auth;

/// <summary>
/// Refuses a second account with the same email. Identity's own <c>RequireUniqueEmail</c> also rejects accounts with
/// no email, but external sign-in without a verified address creates exactly those, so uniqueness is enforced here and
/// only for accounts that have an email. Without it <c>FindByEmailAsync</c> throws when two accounts share an address.
/// </summary>
public sealed class UniqueEmailValidator : IUserValidator<Account>
{
    public async Task<IdentityResult> ValidateAsync(UserManager<Account> manager, Account user)
    {
        var email = await manager.GetEmailAsync(user);
        if (string.IsNullOrWhiteSpace(email))
        {
            return IdentityResult.Success;
        }

        var normalized = manager.NormalizeEmail(email);
        var taken = await manager.Users.AnyAsync(a => a.NormalizedEmail == normalized && a.Id != user.Id);
        return taken
            ? IdentityResult.Failed(new IdentityErrorDescriber().DuplicateEmail(email))
            : IdentityResult.Success;
    }
}
