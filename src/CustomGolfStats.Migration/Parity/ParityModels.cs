namespace CustomGolfStats.Migration.Parity;

/// <summary>A documented phase 1 decision that makes a stat differ from golftrack on purpose.</summary>
public sealed record IntendedRule(string Name, string Reason);

/// <summary>The intended differences, from the v1 roadmap's phase 1 decisions and the spec's round length rule.</summary>
public static class IntendedRules
{
    public static readonly IntendedRule EaglesIncludeAlbatrosses = new(
        "Eagles include albatrosses",
        "Every hole two or more under par counts as an eagle; golftrack counted exactly two under.");

    public static readonly IntendedRule ParThreeTeeShotsIgnored = new(
        "Par 3 tee shots ignored",
        "Driving and GIR from fairway/left/right use par 4 and 5 holes only; golftrack also counted the flags on par 3s.");

    public static readonly IntendedRule BirdieConversionGirOnly = new(
        "Birdie conversion counts GIR birdies only",
        "Only birdies or better on GIR holes count; golftrack counted every birdie, so its ratio could exceed 100%.");

    public static readonly IntendedRule SandSavesGreensideOnly = new(
        "Sand saves count greenside sand only",
        "golftrack's single sand count was imported as greenside sand; fairway bunker shots are excluded.");

    public static readonly IntendedRule FourBucketHistogram = new(
        "18-hole histogram has 4 buckets",
        "Buckets are <80, 80-89, 90-99 and 100+; golftrack had <80, 80-89 and 90+.");

    public static readonly IntendedRule ExactRoundLength = new(
        "Only exact 9/18-hole rounds in 9/18 figures",
        "9 and 18-hole figures use the number of holes recorded; golftrack used the round's HolesPlayed field.");

    public static IReadOnlyList<IntendedRule> All { get; } =
    [
        EaglesIncludeAlbatrosses, ParThreeTeeShotsIgnored, BirdieConversionGirOnly, SandSavesGreensideOnly,
        FourBucketHistogram, ExactRoundLength,
    ];
}

public enum ValueKind { Count, Average, Percent, Date, ToPar }

public enum Verdict
{
    /// <summary>Equal (counts, dates, to-par exactly; averages and percentages within ±0.05 for float rounding).</summary>
    Match,

    /// <summary>Different, and golftrack's value is what golftrack's definition gives on the imported data.</summary>
    Intended,

    /// <summary>Different, and no rule explains it.</summary>
    Unexplained,

    /// <summary>Not compared: no counterpart, or the column or procedure is missing or failed.</summary>
    NotCompared,
}

/// <param name="Column">golftrack's result column (or a description when there is none).</param>
/// <param name="Golftrack">golftrack's value; percentages as 0-100, dates as day numbers.</param>
/// <param name="New">The new engine's value on the imported data, in the same units.</param>
/// <param name="Rule">The rule that explains an <see cref="Verdict.Intended"/> difference.</param>
public sealed record ParityRow(
    string Column, ValueKind Kind, double? Golftrack, double? New, Verdict Verdict, IntendedRule? Rule = null, string? Note = null);

public sealed record ParitySection(string Title, string Procedure, IReadOnlyList<ParityRow> Rows, string? Note = null);

/// <summary>Facts about one year that explain differences: golftrack's year procedures read every golfer's rounds, etc.</summary>
public sealed record ParityContext(
    int GolftrackRounds,
    int ImportedRounds,
    int OtherGolfersRounds,
    int InactiveDetailsInActiveRounds,
    int RoundsWhoseStoredScoreDiffers,
    int RoundsWhoseHolesPlayedDiffers,
    int RoundsSkippedByImport,
    int RoundsMovedToTheirHolesTee);

public sealed record ParityYear(int Year, ParityContext Context, IReadOnlyList<ParitySection> Sections);

public sealed record ParityResult(int GolferId, Guid AccountId, IReadOnlyList<ParityYear> Years);
