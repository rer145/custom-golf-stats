namespace CustomGolfStats.Migration.Report;

// What the migration noticed but did not fix. The mapper produces these; the report lists them. Ids are golftrack ids.

public abstract record MigrationFinding;

/// <summary>
/// A tee rating or slope that was missing or out of range. The 18-hole rating becomes 0 and slope 113 (as for course
/// imports); a front or back nine value becomes null. <see cref="StoredValue"/> is null when nothing was stored.
/// </summary>
public sealed record TeeRatingDefaulted(
    int LegacyTeeId, string CourseName, string TeeName, string Field, string? OriginalValue, string? StoredValue)
    : MigrationFinding;

/// <summary>A tee that could not be imported (no holes, more than 18, or its course is missing).</summary>
public sealed record TeeSkipped(int LegacyTeeId, string CourseName, string TeeName, string Reason) : MigrationFinding;

/// <summary>
/// A tee with more than one hole row of the same number whose par, yardage or stroke index differ. The active row is
/// used (the lowest id among several active ones, or among all when none is active). Identical rows are not reported.
/// </summary>
public sealed record DuplicateTeeHoleNumber(int LegacyTeeId, int HoleNumber, int KeptLegacyHoleId, IReadOnlyList<int> LegacyHoleIds)
    : MigrationFinding;

/// <summary>
/// An imported tee hole whose number (1..18), par (3..6) or yardage (0..1000) is outside what the API accepts. Stored
/// as golftrack has it; an edit through the API would have to correct it.
/// </summary>
public sealed record HoleOutOfRange(
    int LegacyTeeId, string CourseName, string TeeName, int LegacyHoleId, int HoleNumber, string Field, int Value)
    : MigrationFinding;

/// <summary>A round that was not imported: its tee or one of its holes is missing, or it has no hole details.</summary>
public sealed record RoundSkipped(int LegacyRoundId, DateOnly PlayedOn, string Reason) : MigrationFinding;

/// <summary>A round type name (or id) that isn't Practice, Casual, Tournament or League; imported as Casual.</summary>
public sealed record UnknownRoundType(int LegacyRoundId, DateOnly PlayedOn, int RoundTypeId, string? Name) : MigrationFinding;

/// <summary>A round value that could not be stored (an unparseable duration, an out-of-range distance); stored as null.</summary>
public sealed record RoundValueDropped(int LegacyRoundId, DateOnly PlayedOn, string Field, string Value) : MigrationFinding;

/// <summary>
/// A round whose entry sequences (SequenceNumber, or the hole number when it is null) repeat; the holes were numbered
/// 1..n in golftrack detail order (RoundDetail.Id) instead.
/// </summary>
public sealed record EntrySequenceFallback(int LegacyRoundId, DateOnly PlayedOn, IReadOnlyList<int> RepeatedSequences)
    : MigrationFinding;

/// <summary>Rounds on the same date and tee with identical hole-by-hole scores. All of them are imported.</summary>
public sealed record DuplicateRounds(DateOnly PlayedOn, int LegacyTeeId, IReadOnlyList<int> LegacyRoundIds) : MigrationFinding;

/// <summary>
/// An imported hole with zero putts and a score above 1: a hole-out from off the green, or putts not entered.
/// <see cref="LooksLikeHoleOut"/> is true when golftrack's GIR flag agrees with holing out from off the green (GIR exactly
/// when the score is at most par - 2); false (for example GIR set on a par or worse) suggests the putts weren't entered.
/// </summary>
public sealed record ZeroPuttHole(
    int LegacyRoundId, int LegacyRoundDetailId, DateOnly PlayedOn, int HoleNumber, int Par, int Score, bool Gir, bool LooksLikeHoleOut)
    : MigrationFinding
{
    public int Year => PlayedOn.Year;
}
