namespace CustomGolfStats.Api.Features.Me;

public sealed record ProfileResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Gender,
    string? GhinNumber,
    Guid? HomeCourseId,
    string Units);

/// <summary>
/// PUT is a full replace: any omitted field is reset to its default (empty name, Unspecified gender, no GHIN or home
/// course, Yards). Gender and units are the enum names (case-insensitive). Names max 100 characters, GHIN max 20.
/// </summary>
public sealed record UpdateProfileRequest(
    string? FirstName,
    string? LastName,
    string? Gender,
    string? GhinNumber,
    Guid? HomeCourseId,
    string? Units);

public sealed record HandicapEntryResponse(Guid Id, DateOnly EffectiveOn, decimal Index);

public sealed record CreateHandicapEntryRequest(DateOnly? EffectiveOn, decimal? Index);

public sealed record EntitlementFeatures(bool ShotByShotEntry);

/// <summary>What the signed-in golfer may do. <c>TrialShotRoundsRemaining</c> is null for Paid (unlimited).</summary>
public sealed record EntitlementsResponse(
    string Tier,
    string Role,
    EntitlementFeatures Features,
    int? TrialShotRoundsRemaining);
