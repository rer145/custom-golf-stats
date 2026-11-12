using System.Security.Claims;
using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Features.Me;

public static class MeFeature
{
    private const decimal MinIndex = -10.0m;
    private const decimal MaxIndex = 54.0m;

    public static IServiceCollection AddMeFeature(this IServiceCollection services)
    {
        services.AddScoped<EntitlementsReader>();
        return services;
    }

    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/me").WithTags("Me").RequireAuthorization();

        group.MapGet("/", GetProfileAsync).WithName("MeGetProfile");
        group.MapPut("/", UpdateProfileAsync).WithName("MeUpdateProfile");
        group.MapGet("/handicap", ListHandicapAsync).WithName("MeListHandicap");
        group.MapPost("/handicap", AddHandicapAsync).WithName("MeAddHandicap");
        group.MapDelete("/handicap/{id:guid}", DeleteHandicapAsync).WithName("MeDeleteHandicap");
        group.MapGet("/entitlements", GetEntitlementsAsync).WithName("MeGetEntitlements");
        return app;
    }

    private static async Task<Results<Ok<ProfileResponse>, NotFound>> GetProfileAsync(
        ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var accountId = user.GetAccountId();
        var account = await db.Users.AsNoTracking().SingleOrDefaultAsync(a => a.Id == accountId, ct);
        return account is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(account));
    }

    private static async Task<Results<Ok<ProfileResponse>, NotFound, ValidationProblem>> UpdateProfileAsync(
        UpdateProfileRequest request, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        Gender? gender = null;
        DistanceUnits? units = null;

        if (request.Gender is not null)
        {
            if (Enum.TryParse<Gender>(request.Gender, ignoreCase: true, out var g) && Enum.IsDefined(g))
            {
                gender = g;
            }
            else
            {
                errors["gender"] = [$"Must be one of: {string.Join(", ", Enum.GetNames<Gender>())}."];
            }
        }

        if (request.Units is not null)
        {
            if (Enum.TryParse<DistanceUnits>(request.Units, ignoreCase: true, out var u) && Enum.IsDefined(u))
            {
                units = u;
            }
            else
            {
                errors["units"] = [$"Must be one of: {string.Join(", ", Enum.GetNames<DistanceUnits>())}."];
            }
        }

        if (request.HomeCourseId is { } courseId && !await db.Courses.AnyAsync(c => c.Id == courseId, ct))
        {
            errors["homeCourseId"] = ["The course does not exist."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var accountId = user.GetAccountId();
        var account = await db.Users.SingleOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null)
        {
            return TypedResults.NotFound();
        }

        account.FirstName = request.FirstName?.Trim() ?? "";
        account.LastName = request.LastName?.Trim() ?? "";
        account.Gender = gender ?? Gender.Unspecified;
        account.GhinNumber = string.IsNullOrWhiteSpace(request.GhinNumber) ? null : request.GhinNumber.Trim();
        account.HomeCourseId = request.HomeCourseId;
        account.Units = units ?? DistanceUnits.Yards;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToResponse(account));
    }

    private static async Task<Ok<List<HandicapEntryResponse>>> ListHandicapAsync(
        ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var accountId = user.GetAccountId();
        var entries = await db.HandicapEntries.AsNoTracking()
            .Where(h => h.AccountId == accountId)
            .OrderByDescending(h => h.EffectiveOn).ThenByDescending(h => h.Id)
            .Select(h => new HandicapEntryResponse(h.Id, h.EffectiveOn, h.Index))
            .ToListAsync(ct);
        return TypedResults.Ok(entries);
    }

    private static async Task<Results<Created<HandicapEntryResponse>, ValidationProblem>> AddHandicapAsync(
        CreateHandicapEntryRequest request, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.EffectiveOn is null)
        {
            errors["effectiveOn"] = ["A date is required."];
        }

        if (request.Index is not { } index)
        {
            errors["index"] = ["A handicap index is required."];
        }
        else if (index < MinIndex || index > MaxIndex)
        {
            errors["index"] = [$"Must be between {MinIndex:0.0} and {MaxIndex:0.0}."];
        }
        else if (decimal.Round(index, 1) != index)
        {
            errors["index"] = ["Must have at most one decimal place."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var entry = new HandicapEntry
        {
            Id = Guid.CreateVersion7(),
            AccountId = user.GetAccountId(),
            EffectiveOn = request.EffectiveOn!.Value,
            Index = request.Index!.Value,
        };
        db.HandicapEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/me/handicap/{entry.Id}", new HandicapEntryResponse(entry.Id, entry.EffectiveOn, entry.Index));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteHandicapAsync(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var accountId = user.GetAccountId();
        var entry = await db.HandicapEntries.SingleOrDefaultAsync(h => h.Id == id && h.AccountId == accountId, ct);
        if (entry is null)
        {
            return TypedResults.NotFound();
        }

        db.HandicapEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<EntitlementsResponse>, NotFound>> GetEntitlementsAsync(
        ClaimsPrincipal user, EntitlementsReader reader, CancellationToken ct)
    {
        var response = await reader.GetAsync(user.GetAccountId(), ct);
        return response is null ? TypedResults.NotFound() : TypedResults.Ok(response);
    }

    private static ProfileResponse ToResponse(Account a) =>
        new(a.Id, a.Email ?? "", a.FirstName, a.LastName, a.Gender.ToString(), a.GhinNumber, a.HomeCourseId, a.Units.ToString());
}
