using System.Security.Claims;
using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Features.Admin;

/// <summary><c>/admin/flags</c>: the flag queue (golfers' reports and proposed edits to Verified courses).</summary>
public static class AdminFlagEndpoints
{
    private const int MaxResults = 200;

    public static RouteGroupBuilder MapAdminFlagRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("/flags", ListAsync).WithName("AdminListFlags");
        group.MapPost("/flags/{id:guid}/resolve", ResolveAsync).WithName("AdminResolveFlag");
        group.MapPost("/flags/{id:guid}/dismiss", DismissAsync).WithName("AdminDismissFlag");
        return group;
    }

    /// <summary>
    /// <c>GET /admin/flags?status=</c>: flags with the given status (Open when omitted; Open, Resolved or Dismissed,
    /// case-insensitive). Oldest first; at most 200.
    /// </summary>
    internal static async Task<Results<Ok<List<AdminFlagDto>>, ValidationProblem>> ListAsync(
        string? status, AppDbContext db, CancellationToken ct)
    {
        var wanted = FlagStatus.Open;
        if (!string.IsNullOrWhiteSpace(status) && !AdminEnums.TryParse(status, out wanted))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["status"] = [$"Status must be one of {AdminEnums.Names<FlagStatus>()}."],
            });
        }

        var flags = await Project(db.CourseFlags.AsNoTracking().Where(f => f.Status == wanted)
                .OrderBy(f => f.CreatedAt).ThenBy(f => f.Id).Take(MaxResults))
            .ToListAsync(ct);
        return TypedResults.Ok(flags);
    }

    /// <summary><c>POST /admin/flags/{id}/resolve</c>: marks an Open flag Resolved (409 when it isn't Open).</summary>
    internal static Task<Results<Ok<AdminFlagDto>, NotFound, ProblemHttpResult>> ResolveAsync(
        Guid id, ClaimsPrincipal user, AppDbContext db, TimeProvider time, CancellationToken ct) =>
        CloseAsync(id, FlagStatus.Resolved, user, db, time, ct);

    /// <summary><c>POST /admin/flags/{id}/dismiss</c>: marks an Open flag Dismissed (409 when it isn't Open).</summary>
    internal static Task<Results<Ok<AdminFlagDto>, NotFound, ProblemHttpResult>> DismissAsync(
        Guid id, ClaimsPrincipal user, AppDbContext db, TimeProvider time, CancellationToken ct) =>
        CloseAsync(id, FlagStatus.Dismissed, user, db, time, ct);

    private static async Task<Results<Ok<AdminFlagDto>, NotFound, ProblemHttpResult>> CloseAsync(
        Guid id, FlagStatus status, ClaimsPrincipal user, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var flag = await db.CourseFlags.SingleOrDefaultAsync(f => f.Id == id, ct);
        if (flag is null)
        {
            return TypedResults.NotFound();
        }

        if (flag.Status != FlagStatus.Open)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Flag is not open",
                detail: $"This flag is already {flag.Status}.");
        }

        flag.Status = status;
        flag.ResolvedByAccountId = user.GetAccountId();
        flag.ResolvedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await Project(db.CourseFlags.AsNoTracking().Where(f => f.Id == id)).SingleAsync(ct));
    }

    private static IQueryable<AdminFlagDto> Project(IQueryable<CourseFlag> flags) =>
        flags.Select(f => new AdminFlagDto(
            f.Id,
            f.CourseId,
            f.Course.Name,
            f.TeeId,
            f.Tee == null ? null : f.Tee.Name,
            f.HoleNumber,
            f.Note,
            f.Status,
            f.ReporterAccountId,
            f.Reporter.Email,
            f.CreatedAt,
            f.ResolvedByAccountId,
            f.ResolvedAt));
}
