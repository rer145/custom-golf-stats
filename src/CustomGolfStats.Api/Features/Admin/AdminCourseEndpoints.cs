using System.Security.Claims;
using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Features.Courses;
using CustomGolfStats.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Features.Admin;

/// <summary>
/// <c>/admin/courses</c>: list by status, edit details, correct a tee version in place (even when rounds use it),
/// verify, and merge duplicates. Validation and version edits reuse <see cref="CourseRules"/> and
/// <see cref="TeeVersionEditor"/>.
/// </summary>
public static class AdminCourseEndpoints
{
    private const int MaxResults = 200;

    public static RouteGroupBuilder MapAdminCourseRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("/courses", ListAsync).WithName("AdminListCourses");
        group.MapPut("/courses/{id:guid}", UpdateDetailsAsync).WithName("AdminUpdateCourseDetails");
        group.MapPut("/courses/{id:guid}/tees/{teeId:guid}/versions/{versionId:guid}", CorrectVersionAsync)
            .WithName("AdminCorrectTeeVersion");
        group.MapPost("/courses/{id:guid}/verify", VerifyAsync).WithName("AdminVerifyCourse");
        group.MapPost("/courses/{id:guid}/merge", MergeAsync).WithName("AdminMergeCourse");
        return group;
    }

    /// <summary>
    /// <c>GET /admin/courses?status=&amp;query=</c>: courses (merged ones included, marked by <c>mergedIntoCourseId</c>)
    /// with their open flag count, optionally filtered by status (Imported, UserEdited, Verified; case-insensitive) and
    /// by name, club name or city containing <c>query</c>. Ordered by name; at most 200.
    /// </summary>
    internal static async Task<Results<Ok<List<AdminCourseSummaryDto>>, ValidationProblem>> ListAsync(
        string? status, string? query, AppDbContext db, CancellationToken ct)
    {
        var courses = db.Courses.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!AdminEnums.TryParse<CourseStatus>(status, out var parsed))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = [$"Status must be one of {AdminEnums.Names<CourseStatus>()}."],
                });
            }

            courses = courses.Where(c => c.Status == parsed);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{AdminQueries.EscapeLike(query.Trim())}%";
            courses = courses.Where(c =>
                EF.Functions.ILike(c.Name, pattern, @"\")
                || EF.Functions.ILike(c.ClubName!, pattern, @"\")
                || EF.Functions.ILike(c.City!, pattern, @"\"));
        }

        var list = await courses.OrderBy(c => c.Name).ThenBy(c => c.Id).Take(MaxResults)
            .Select(c => new AdminCourseSummaryDto(
                c.Id, c.Name, c.ClubName, c.City, c.Region, c.Country, c.Status, c.Source, c.LastVerifiedAt, c.MergedIntoCourseId,
                db.CourseFlags.Count(f => f.CourseId == c.Id && f.Status == FlagStatus.Open),
                c.UpdatedAt))
            .ToListAsync(ct);
        return TypedResults.Ok(list);
    }

    /// <summary>
    /// <c>PUT /admin/courses/{id}</c>: replaces the course details (same rules as <c>PUT /courses/{id}</c>). Applied
    /// directly, never as a flag; a Verified course stays Verified, any other becomes UserEdited.
    /// </summary>
    internal static async Task<Results<Ok<CourseDetailDto>, ValidationProblem, NotFound>> UpdateDetailsAsync(
        Guid id, CourseDetailsRequest request, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        CourseRules.ValidateDetails(request, errors);
        if (errors.Any)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        var course = await db.Courses.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (course is null)
        {
            return TypedResults.NotFound();
        }

        CourseRules.ApplyDetails(course, request);
        course.UpdatedAt = time.GetUtcNow();
        if (course.Status != CourseStatus.Verified)
        {
            course.Status = CourseStatus.UserEdited;
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok((await LoadDetailAsync(db, id, time, ct))!);
    }

    /// <summary>
    /// <c>PUT /admin/courses/{id}/tees/{teeId}/versions/{versionId}</c>: corrects a version in place, even when rounds
    /// use it, so the correction changes those rounds' stats. Body as <c>PUT /courses/.../versions/{versionId}</c>
    /// (null <c>effectiveFrom</c> keeps the date). Records the admin as the changer; the course status is unchanged
    /// when Verified, otherwise UserEdited.
    /// </summary>
    internal static async Task<Results<Ok<TeeVersionDto>, ValidationProblem, NotFound>> CorrectVersionAsync(
        Guid id, Guid teeId, Guid versionId, TeeVersionInput request, ClaimsPrincipal user, AppDbContext db, TeeVersionEditor editor,
        TimeProvider time, CancellationToken ct)
    {
        if (!await db.TeeVersions.AnyAsync(v => v.Id == versionId && v.TeeId == teeId && v.Tee.CourseId == id, ct))
        {
            return TypedResults.NotFound();
        }

        var result = await editor.ApplyAsync(versionId, request, allowWhenUsed: true, user.GetAccountId(), ct);
        switch (result.Outcome)
        {
            case TeeVersionEditOutcome.Applied:
                var siblings = await db.TeeVersions.AsNoTracking().Where(v => v.TeeId == teeId)
                    .Select(v => new { v.Id, v.EffectiveFrom }).ToListAsync(ct);
                var current = CourseRules.VersionInEffect(siblings.Select(v => (v.Id, v.EffectiveFrom)), AdminQueries.Today(time));
                return TypedResults.Ok(result.Version!.ToDto(current == versionId));
            case TeeVersionEditOutcome.Invalid:
                return TypedResults.ValidationProblem(result.Errors!);
            case TeeVersionEditOutcome.DuplicateEffectiveFrom:
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["effectiveFrom"] = [$"This tee already has a version effective from {request.EffectiveFrom:yyyy-MM-dd}."],
                });
            default:
                return TypedResults.NotFound();
        }
    }

    /// <summary><c>POST /admin/courses/{id}/verify</c>: sets Status Verified and LastVerifiedAt to now.</summary>
    internal static async Task<Results<Ok<CourseDetailDto>, NotFound>> VerifyAsync(
        Guid id, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var course = await db.Courses.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (course is null)
        {
            return TypedResults.NotFound();
        }

        var now = time.GetUtcNow();
        course.Status = CourseStatus.Verified;
        course.LastVerifiedAt = now;
        course.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok((await LoadDetailAsync(db, id, time, ct))!);
    }

    /// <summary>
    /// <c>POST /admin/courses/{id}/merge {"intoCourseId"}</c>: merges a duplicate course into another. See
    /// <see cref="CourseMerger"/> for the matching rules. 400 when the target is missing, the course itself or already
    /// merged; 404 when the course doesn't exist; 409 when it was already merged, or when some used tee versions have no
    /// match (the problem's <c>unmatched</c> lists them as <see cref="UnmatchedTeeVersionDto"/>) and nothing changes.
    /// </summary>
    internal static async Task<Results<Ok<CourseMergeResultDto>, ValidationProblem, NotFound, ProblemHttpResult>> MergeAsync(
        Guid id, MergeCourseRequest request, CourseMerger merger, CancellationToken ct)
    {
        if (request.IntoCourseId is not { } intoCourseId)
        {
            return IntoCourseIdProblem("intoCourseId is required.");
        }

        var result = await merger.MergeAsync(id, intoCourseId, ct);
        return result.Outcome switch
        {
            CourseMergeOutcome.Merged => TypedResults.Ok(result.Merged!),
            CourseMergeOutcome.NotFound => TypedResults.NotFound(),
            CourseMergeOutcome.TargetNotFound => IntoCourseIdProblem("The course to merge into doesn't exist."),
            CourseMergeOutcome.IntoItself => IntoCourseIdProblem("A course can't be merged into itself."),
            CourseMergeOutcome.TargetMerged => IntoCourseIdProblem(
                "The course to merge into has itself been merged; merge into the course it was merged into."),
            CourseMergeOutcome.AlreadyMerged => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Course already merged",
                detail: "This course has already been merged into another course."),
            _ => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Tee versions don't match",
                detail: "Rounds use tee versions that have no match (same tee name and gender, same effective date) in the "
                    + "course to merge into. Add matching tees or versions there, then merge again. Nothing was changed.",
                extensions: new Dictionary<string, object?> { ["unmatched"] = result.Unmatched }),
        };
    }

    private static ValidationProblem IntoCourseIdProblem(string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["intoCourseId"] = [message] });

    private static async Task<CourseDetailDto?> LoadDetailAsync(AppDbContext db, Guid id, TimeProvider time, CancellationToken ct)
    {
        var course = await db.Courses.AsNoTracking().AsSplitQuery()
            .Include(c => c.Tees).ThenInclude(t => t.Versions).ThenInclude(v => v.Holes)
            .SingleOrDefaultAsync(c => c.Id == id, ct);
        return course?.ToDetailDto(AdminQueries.Today(time));
    }
}
