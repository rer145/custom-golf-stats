namespace CustomGolfStats.Api.Data.Entities;

// API-only enums. Golf enums (Lie, Club, Role, Tier, EntryMode, ...) come from CustomGolfStats.Domain.
// All enums are stored as strings (see AppDbContext.ConfigureConventions).

public enum Gender { Male, Female, Unspecified }

public enum DistanceUnits { Yards, Meters }

public enum CourseStatus { Imported, UserEdited, Verified }

/// <summary>Where a course or tee version came from.</summary>
public enum CourseSource { GolfCourseApi, Manual, Golftrack }

public enum RoundType { Practice, Casual, Tournament, League }

public enum FlagStatus { Open, Resolved, Dismissed }
