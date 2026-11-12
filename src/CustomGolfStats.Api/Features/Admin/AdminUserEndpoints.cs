using System.Security.Claims;
using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Infrastructure;
using CustomGolfStats.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Features.Admin;

/// <summary><c>/admin/users</c>: find golfers and set their tier, role and disabled flag.</summary>
public static class AdminUserEndpoints
{
    private const int MaxResults = 100;

    public static RouteGroupBuilder MapAdminUserRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("/users", ListAsync).WithName("AdminListUsers");
        group.MapPut("/users/{id:guid}", UpdateAsync).WithName("AdminUpdateUser");
        return group;
    }

    /// <summary>
    /// <c>GET /admin/users?query=</c>: accounts whose email, first or last name contains <c>query</c>
    /// (case-insensitive), ordered by email. At most 100.
    /// </summary>
    internal static async Task<Ok<List<AdminUserDto>>> ListAsync(string? query, AppDbContext db, CancellationToken ct)
    {
        var users = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{AdminQueries.EscapeLike(query.Trim())}%";
            users = users.Where(a =>
                EF.Functions.ILike(a.Email!, pattern, @"\")
                || EF.Functions.ILike(a.FirstName, pattern, @"\")
                || EF.Functions.ILike(a.LastName, pattern, @"\"));
        }

        var list = await users.OrderBy(a => a.Email).ThenBy(a => a.Id).Take(MaxResults)
            .Select(a => new AdminUserDto(
                a.Id, a.Email ?? "", a.FirstName, a.LastName, a.Role, a.Tier, a.TrialShotRoundsUsed, a.DisabledAt, a.CreatedAt))
            .ToListAsync(ct);
        return TypedResults.Ok(list);
    }

    /// <summary>
    /// <c>PUT /admin/users/{id}</c>: sets tier, role and/or disabled (null fields are left alone). An admin can't remove
    /// their own Admin role or disable themselves.
    /// <para>
    /// A tier change shows at once in <c>/me/entitlements</c> (read from the database). A role change reaches the
    /// golfer's token at their next login or refresh. Disabling blocks login, refresh and external sign-in at once,
    /// but access tokens already issued stay valid until they expire (up to 1 hour).
    /// </para>
    /// </summary>
    internal static async Task<Results<Ok<AdminUserDto>, ValidationProblem, NotFound>> UpdateAsync(
        Guid id, UpdateUserRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        Tier? tier = null;
        Role? role = null;
        if (request.Tier is not null)
        {
            if (AdminEnums.TryParse<Tier>(request.Tier, out var parsed))
            {
                tier = parsed;
            }
            else
            {
                errors.Add("tier", $"Tier must be one of {AdminEnums.Names<Tier>()}.");
            }
        }

        if (request.Role is not null)
        {
            if (AdminEnums.TryParse<Role>(request.Role, out var parsed))
            {
                role = parsed;
            }
            else
            {
                errors.Add("role", $"Role must be one of {AdminEnums.Names<Role>()}.");
            }
        }

        var isSelf = id == user.GetAccountId();
        if (isSelf && role is { } newRole && newRole != Role.Admin)
        {
            errors.Add("role", "You can't remove your own Admin role.");
        }

        if (isSelf && request.Disabled == true)
        {
            errors.Add("disabled", "You can't disable your own account.");
        }

        if (errors.Any)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        var account = await db.Users.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (account is null)
        {
            return TypedResults.NotFound();
        }

        account.Tier = tier ?? account.Tier;
        account.Role = role ?? account.Role;
        if (request.Disabled is { } disabled)
        {
            // Disabling an already disabled account keeps the original time.
            account.DisabledAt = disabled ? account.DisabledAt ?? time.GetUtcNow() : null;
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new AdminUserDto(
            account.Id, account.Email ?? "", account.FirstName, account.LastName, account.Role, account.Tier,
            account.TrialShotRoundsUsed, account.DisabledAt, account.CreatedAt));
    }
}
