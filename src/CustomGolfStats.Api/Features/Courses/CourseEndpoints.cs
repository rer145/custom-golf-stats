using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Infrastructure;
using CustomGolfStats.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Features.Courses;

/// <summary>
/// <c>/courses</c>: database search, provider search and import, read, manual create, detail and tee version edits,
/// and flags. Courses are shared by every golfer; non-admin edits to a Verified course become flags (202).
/// </summary>
public static class CourseEndpoints
{
    private const int MaxResults = 50;
    private const int MaxCandidates = 1000;
    private const double NearRadiusMiles = 50;
    private const double MilesPerDegreeLatitude = 69.0;

    private static readonly JsonSerializerOptions NoteJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static RouteGroupBuilder MapCourseRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("", SearchAsync).WithName("CoursesSearch");
        group.MapGet("/provider-search", ProviderSearchAsync).WithName("CoursesProviderSearch");
        group.MapPost("/import", ImportAsync).WithName("CoursesImport");
        group.MapPost("", CreateAsync).WithName("CoursesCreate");
        group.MapGet("/{id:guid}", GetAsync).WithName("CoursesGet");
        group.MapPut("/{id:guid}", UpdateDetailsAsync).WithName("CoursesUpdateDetails");
        group.MapPost("/{id:guid}/tees/{teeId:guid}/versions", CreateVersionAsync).WithName("CoursesCreateTeeVersion");
        group.MapPut("/{id:guid}/tees/{teeId:guid}/versions/{versionId:guid}", EditVersionAsync).WithName("CoursesEditTeeVersion");
        group.MapPost("/{id:guid}/flags", CreateFlagAsync).WithName("CoursesCreateFlag");
        return group;
    }

    /// <summary>
    /// <c>GET /courses?query=&amp;near=lat,lon</c>: name, club name or city contains <c>query</c> (case-insensitive);
    /// merged courses are hidden. With <c>near</c> results are ordered by distance (unknown locations last); with
    /// <c>near</c> alone, only courses within 50 miles are returned. At most 50 results.
    /// </summary>
    internal static async Task<Results<Ok<List<CourseSummaryDto>>, ValidationProblem>> SearchAsync(
        string? query, string? near, AppDbContext db, CancellationToken ct)
    {
        var text = query?.Trim();
        var errors = new ValidationErrors();
        (double Lat, double Lon)? point = null;
        if (!string.IsNullOrWhiteSpace(near))
        {
            if (TryParseNear(near, out var parsed))
            {
                point = parsed;
            }
            else
            {
                errors.Add("near", "near must be \"latitude,longitude\" with latitude -90..90 and longitude -180..180.");
            }
        }
        else if (string.IsNullOrEmpty(text))
        {
            errors.Add("query", "Give a query, near=latitude,longitude, or both.");
        }

        if (errors.Any)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        var courses = db.Courses.AsNoTracking().Where(c => c.MergedIntoCourseId == null);
        if (!string.IsNullOrEmpty(text))
        {
            var pattern = $"%{EscapeLike(text)}%";
            courses = courses.Where(c =>
                EF.Functions.ILike(c.Name, pattern, @"\")
                || EF.Functions.ILike(c.ClubName!, pattern, @"\")
                || EF.Functions.ILike(c.City!, pattern, @"\"));
        }

        if (point is not { } p)
        {
            var byName = await courses.OrderBy(c => c.Name).ThenBy(c => c.Id).Take(MaxResults)
                .Select(c => new CourseSummaryDto(c.Id, c.Name, c.ClubName, c.City, c.Region, c.Country, c.Latitude, c.Longitude, c.Status, null))
                .ToListAsync(ct);
            return TypedResults.Ok(byName);
        }

        var nearOnly = string.IsNullOrEmpty(text);
        if (nearOnly)
        {
            var latDelta = NearRadiusMiles / MilesPerDegreeLatitude;
            var cosLat = Math.Cos(p.Lat * Math.PI / 180);
            var lonDelta = cosLat < 0.01 ? 360 : NearRadiusMiles / (MilesPerDegreeLatitude * cosLat);
            courses = courses.Where(c => c.Latitude != null && c.Longitude != null
                && c.Latitude >= p.Lat - latDelta && c.Latitude <= p.Lat + latDelta
                && c.Longitude >= p.Lon - lonDelta && c.Longitude <= p.Lon + lonDelta);
        }

        var candidates = await courses.OrderBy(c => c.Name).Take(MaxCandidates)
            .Select(c => new CourseSummaryDto(c.Id, c.Name, c.ClubName, c.City, c.Region, c.Country, c.Latitude, c.Longitude, c.Status, null))
            .ToListAsync(ct);
        var results = candidates
            .Select(c => c with
            {
                DistanceMiles = c is { Latitude: { } lat, Longitude: { } lon }
                    ? Math.Round(CourseRules.DistanceMiles(p.Lat, p.Lon, lat, lon), 2)
                    : null,
            })
            .Where(c => !nearOnly || c.DistanceMiles <= NearRadiusMiles)
            .OrderBy(c => c.DistanceMiles is null)
            .ThenBy(c => c.DistanceMiles)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxResults)
            .ToList();
        return TypedResults.Ok(results);
    }

    /// <summary><c>GET /courses/provider-search?query=</c>: GolfCourseAPI results, each marked when already imported.</summary>
    internal static async Task<Results<Ok<List<ProviderSearchResultDto>>, ValidationProblem, ProblemHttpResult>> ProviderSearchAsync(
        string? query, ICourseProvider provider, AppDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["A query is required."] });
        }

        IReadOnlyList<ProviderCourseSummary> found;
        try
        {
            found = await provider.SearchAsync(query.Trim(), ct);
        }
        catch (CourseProviderException)
        {
            return ProviderUnavailable();
        }

        var ids = found.Select(f => f.ProviderCourseId).Distinct().ToList();
        var imported = await db.Courses.AsNoTracking()
            .Where(c => c.Source == CourseSource.GolfCourseApi && c.ProviderCourseId != null && ids.Contains(c.ProviderCourseId))
            .Select(c => new { ProviderCourseId = c.ProviderCourseId!, CourseId = c.MergedIntoCourseId ?? c.Id })
            .ToDictionaryAsync(c => c.ProviderCourseId, c => c.CourseId, ct);

        return TypedResults.Ok(found
            .Select(f => new ProviderSearchResultDto(
                f.ProviderCourseId, f.ClubName, f.CourseName, f.City, f.Region, f.Country,
                imported.ContainsKey(f.ProviderCourseId),
                imported.TryGetValue(f.ProviderCourseId, out var courseId) ? courseId : null))
            .ToList());
    }

    /// <summary>
    /// <c>POST /courses/import</c>: imports one GolfCourseAPI course (201), or returns the course already imported
    /// under that id (200) without calling the provider.
    /// </summary>
    internal static async Task<Results<Created<CourseDetailDto>, Ok<CourseDetailDto>, ValidationProblem, NotFound, ProblemHttpResult>> ImportAsync(
        ImportCourseRequest request, ClaimsPrincipal user, ICourseProvider provider, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var providerCourseId = request.ProviderCourseId?.Trim();
        if (string.IsNullOrEmpty(providerCourseId) || providerCourseId.Length > 100)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["providerCourseId"] = ["A provider course id of at most 100 characters is required."],
            });
        }

        if (await FindImportedAsync(db, providerCourseId, time, ct) is { } existing)
        {
            return TypedResults.Ok(existing);
        }

        ProviderCourse? source;
        try
        {
            source = await provider.GetAsync(providerCourseId, ct);
        }
        catch (CourseProviderException)
        {
            return ProviderUnavailable();
        }

        if (source is null)
        {
            return TypedResults.NotFound();
        }

        var course = CourseImport.Build(source, providerCourseId, user.GetAccountId(), time.GetUtcNow());
        db.Courses.Add(course);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (TeeVersionEditor.IsUniqueViolation(ex))
        {
            // A concurrent import of the same course won.
            db.ChangeTracker.Clear();
            if (await FindImportedAsync(db, providerCourseId, time, ct) is { } winner)
            {
                return TypedResults.Ok(winner);
            }

            throw;
        }

        return TypedResults.Created($"/courses/{course.Id}", course.ToDetailDto(Today(time)));
    }

    /// <summary><c>GET /courses/{id}</c>: the course with every tee and version (the one in effect today flagged).</summary>
    internal static async Task<Results<Ok<CourseDetailDto>, NotFound>> GetAsync(Guid id, AppDbContext db, TimeProvider time, CancellationToken ct) =>
        await LoadDetailAsync(db, id, time, ct) is { } course ? TypedResults.Ok(course) : TypedResults.NotFound();

    /// <summary><c>POST /courses</c>: a manual course (Source Manual, Status UserEdited) with its tees and holes.</summary>
    internal static async Task<Results<Created<CourseDetailDto>, ValidationProblem>> CreateAsync(
        CreateCourseRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        CourseRules.ValidateDetails(request.Details, errors);
        if (request.Tees is null || request.Tees.Count == 0)
        {
            errors.Add("tees", "At least one tee is required.");
        }
        else
        {
            for (var i = 0; i < request.Tees.Count; i++)
            {
                var tee = request.Tees[i];
                if (tee is null)
                {
                    errors.Add($"tees[{i}]", "Tee is required.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(tee.Name) || tee.Name.Trim().Length > 100)
                {
                    errors.Add($"tees[{i}].name", "A tee name of at most 100 characters is required.");
                }

                CourseRules.ValidateVersion(tee.Version, errors, requireEffectiveFrom: true, $"tees[{i}].");
            }
        }

        if (errors.Any)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        var now = time.GetUtcNow();
        var accountId = user.GetAccountId();
        var course = new Course { Name = "", Status = CourseStatus.UserEdited, Source = CourseSource.Manual, CreatedAt = now, UpdatedAt = now };
        CourseRules.ApplyDetails(course, request.Details);
        foreach (var teeRequest in request.Tees!)
        {
            var tee = new Tee { Name = teeRequest.Name!.Trim(), Gender = teeRequest.Gender ?? Gender.Unspecified };
            var version = new TeeVersion { Source = CourseSource.Manual, ChangedByAccountId = accountId, ChangedAt = now };
            CourseRules.ApplyVersion(version, teeRequest.Version);
            tee.Versions.Add(version);
            course.Tees.Add(tee);
        }

        db.Courses.Add(course);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/courses/{course.Id}", course.ToDetailDto(Today(time)));
    }

    /// <summary>
    /// <c>PUT /courses/{id}</c>: replaces the course details. Sets Status UserEdited (a Verified course stays Verified
    /// when an admin edits it; a non-admin's edit to it becomes a flag, 202).
    /// </summary>
    internal static async Task<Results<Ok<CourseDetailDto>, Accepted<CourseFlagDto>, ValidationProblem, NotFound>> UpdateDetailsAsync(
        Guid id, CourseDetailsRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var course = await db.Courses.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (course is null)
        {
            return TypedResults.NotFound();
        }

        var errors = new ValidationErrors();
        CourseRules.ValidateDetails(request, errors);
        if (errors.Any)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        if (course.Status == CourseStatus.Verified && !IsAdmin(user))
        {
            return await ProposeAsync(db, time, user, course.Id, teeId: null, "course details", request, ct);
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
    /// <c>POST /courses/{id}/tees/{teeId}/versions</c>: a new version from <c>effectiveFrom</c> on. Rounds on older
    /// versions keep them.
    /// </summary>
    internal static async Task<Results<Created<TeeVersionDto>, Accepted<CourseFlagDto>, ValidationProblem, NotFound>> CreateVersionAsync(
        Guid id, Guid teeId, TeeVersionInput request, ClaimsPrincipal user, AppDbContext db, TeeVersionEditor editor, TimeProvider time,
        CancellationToken ct)
    {
        var tee = await db.Tees.Include(t => t.Course).SingleOrDefaultAsync(t => t.Id == teeId && t.CourseId == id, ct);
        if (tee is null)
        {
            return TypedResults.NotFound();
        }

        var errors = new ValidationErrors();
        CourseRules.ValidateVersion(request, errors, requireEffectiveFrom: true);
        if (errors.Any)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        if (tee.Course.Status == CourseStatus.Verified && !IsAdmin(user))
        {
            return await ProposeAsync(db, time, user, id, teeId, $"new version of tee \"{tee.Name}\" ({tee.Gender})", request, ct);
        }

        var result = await editor.CreateAsync(tee, request, user.GetAccountId(), ct);
        return result.Outcome switch
        {
            TeeVersionEditOutcome.Applied => TypedResults.Created(
                $"/courses/{id}", result.Version!.ToDto(await IsCurrentAsync(db, result.Version!, time, ct))),
            TeeVersionEditOutcome.DuplicateEffectiveFrom => DuplicateEffectiveFrom(request.EffectiveFrom),
            TeeVersionEditOutcome.Invalid => TypedResults.ValidationProblem(result.Errors!),
            _ => TypedResults.NotFound(),
        };
    }

    /// <summary>
    /// <c>PUT /courses/{id}/tees/{teeId}/versions/{versionId}</c>: in-place edit, only while no round uses the version
    /// (otherwise 409: create a new version, or ask an admin to correct it).
    /// </summary>
    internal static async Task<Results<Ok<TeeVersionDto>, Accepted<CourseFlagDto>, ValidationProblem, NotFound, ProblemHttpResult>> EditVersionAsync(
        Guid id, Guid teeId, Guid versionId, TeeVersionInput request, ClaimsPrincipal user, AppDbContext db, TeeVersionEditor editor,
        TimeProvider time, CancellationToken ct)
    {
        var target = await db.TeeVersions.AsNoTracking()
            .Where(v => v.Id == versionId && v.TeeId == teeId && v.Tee.CourseId == id)
            .Select(v => new { v.Tee.Course.Status, TeeName = v.Tee.Name, v.Tee.Gender, v.EffectiveFrom })
            .SingleOrDefaultAsync(ct);
        if (target is null)
        {
            return TypedResults.NotFound();
        }

        var errors = new ValidationErrors();
        CourseRules.ValidateVersion(request, errors, requireEffectiveFrom: false);
        if (errors.Any)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        if (target.Status == CourseStatus.Verified && !IsAdmin(user))
        {
            var what = $"tee \"{target.TeeName}\" ({target.Gender}) version {versionId} effective {target.EffectiveFrom:yyyy-MM-dd}";
            return await ProposeAsync(db, time, user, id, teeId, what, request, ct);
        }

        var result = await editor.ApplyAsync(versionId, request, allowWhenUsed: false, user.GetAccountId(), ct);
        return result.Outcome switch
        {
            TeeVersionEditOutcome.Applied => TypedResults.Ok(result.Version!.ToDto(await IsCurrentAsync(db, result.Version!, time, ct))),
            TeeVersionEditOutcome.InUse => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Tee version is in use",
                detail: "Rounds have been played on this tee version, so it can't be edited in place. "
                    + "Create a new version with the date the change took effect, or ask an admin to correct this version."),
            TeeVersionEditOutcome.DuplicateEffectiveFrom => DuplicateEffectiveFrom(request.EffectiveFrom),
            TeeVersionEditOutcome.Invalid => TypedResults.ValidationProblem(result.Errors!),
            _ => TypedResults.NotFound(),
        };
    }

    /// <summary><c>POST /courses/{id}/flags</c>: reports that a course, tee or hole is wrong.</summary>
    internal static async Task<Results<Created<CourseFlagDto>, ValidationProblem, NotFound>> CreateFlagAsync(
        Guid id, CreateFlagRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (!await db.Courses.AnyAsync(c => c.Id == id, ct))
        {
            return TypedResults.NotFound();
        }

        var errors = new ValidationErrors();
        var note = request.Note?.Trim();
        if (string.IsNullOrEmpty(note))
        {
            errors.Add("note", "A note is required.");
        }
        else if (note.Length > CourseRules.MaxFlagNote)
        {
            errors.Add("note", $"At most {CourseRules.MaxFlagNote} characters.");
        }

        if (request.HoleNumber is < 1 or > CourseRules.MaxHoles)
        {
            errors.Add("holeNumber", $"Hole number must be between 1 and {CourseRules.MaxHoles}.");
        }

        if (request.TeeId is { } teeId && !await db.Tees.AnyAsync(t => t.Id == teeId && t.CourseId == id, ct))
        {
            errors.Add("teeId", "The tee doesn't belong to this course.");
        }

        if (errors.Any)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        var flag = new CourseFlag
        {
            CourseId = id,
            TeeId = request.TeeId,
            HoleNumber = request.HoleNumber,
            ReporterAccountId = user.GetAccountId(),
            Note = note!,
            CreatedAt = time.GetUtcNow(),
        };
        db.CourseFlags.Add(flag);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created((string?)null, flag.ToDto());
    }

    private static async Task<Accepted<CourseFlagDto>> ProposeAsync(
        AppDbContext db, TimeProvider time, ClaimsPrincipal user, Guid courseId, Guid? teeId, string what, object proposal, CancellationToken ct)
    {
        var note = $"Proposed change to {what}: {JsonSerializer.Serialize(proposal, proposal.GetType(), NoteJson)}";
        if (note.Length > CourseRules.MaxFlagNote)
        {
            note = note[..(CourseRules.MaxFlagNote - 1)] + "…";
        }

        var flag = new CourseFlag
        {
            CourseId = courseId,
            TeeId = teeId,
            ReporterAccountId = user.GetAccountId(),
            Note = note,
            CreatedAt = time.GetUtcNow(),
        };
        db.CourseFlags.Add(flag);
        await db.SaveChangesAsync(ct);
        return TypedResults.Accepted((string?)null, flag.ToDto());
    }

    private static async Task<CourseDetailDto?> FindImportedAsync(AppDbContext db, string providerCourseId, TimeProvider time, CancellationToken ct)
    {
        var id = await db.Courses
            .Where(c => c.Source == CourseSource.GolfCourseApi && c.ProviderCourseId == providerCourseId)
            .Select(c => (Guid?)(c.MergedIntoCourseId ?? c.Id))
            .SingleOrDefaultAsync(ct);
        return id is { } courseId ? await LoadDetailAsync(db, courseId, time, ct) : null;
    }

    private static async Task<CourseDetailDto?> LoadDetailAsync(AppDbContext db, Guid id, TimeProvider time, CancellationToken ct)
    {
        var course = await db.Courses.AsNoTracking().AsSplitQuery()
            .Include(c => c.Tees).ThenInclude(t => t.Versions).ThenInclude(v => v.Holes)
            .SingleOrDefaultAsync(c => c.Id == id, ct);
        return course?.ToDetailDto(Today(time));
    }

    private static async Task<bool> IsCurrentAsync(AppDbContext db, TeeVersion version, TimeProvider time, CancellationToken ct)
    {
        var versions = await db.TeeVersions.Where(v => v.TeeId == version.TeeId)
            .Select(v => new { v.Id, v.EffectiveFrom }).ToListAsync(ct);
        return CourseRules.VersionInEffect(versions.Select(v => (v.Id, v.EffectiveFrom)), Today(time)) == version.Id;
    }

    private static ValidationProblem DuplicateEffectiveFrom(DateOnly? effectiveFrom) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            ["effectiveFrom"] = [$"This tee already has a version effective from {effectiveFrom:yyyy-MM-dd}."],
        });

    private static ProblemHttpResult ProviderUnavailable() => TypedResults.Problem(
        statusCode: StatusCodes.Status502BadGateway,
        title: "Course provider unavailable",
        detail: "The course provider could not be reached. Try again later.");

    private static bool IsAdmin(ClaimsPrincipal user) => user.IsInRole(nameof(Role.Admin));

    private static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

    private static bool TryParseNear(string near, out (double Lat, double Lon) point)
    {
        point = default;
        var parts = near.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
            || lat is < -90 or > 90 || lon is < -180 or > 180 || double.IsNaN(lat) || double.IsNaN(lon))
        {
            return false;
        }

        point = (lat, lon);
        return true;
    }
}
