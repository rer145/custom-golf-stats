using CustomGolfStats.Domain;
using CustomGolfStats.Domain.Holes;
using CustomGolfStats.Domain.Shots;
using SQLite;
using RoundType = CustomGolfStats.Api.Client.RoundType;

namespace CustomGolfStats.App.Core.Storage.Rows;

// Enums are stored as integers (sqlite-net's text storage needs an attribute on the enum type itself, and the
// Domain enums are not ours to annotate). The persisted values are the Domain enums (Lie, Club, ShotType, Direction, Shape,
// EntryMode, Role, Tier) and the generated Api.Client enums (RoundType, Gender, CourseStatus, CourseSource). Never reorder or
// renumber any of them (or regenerate the client with a different order) without a migration; EnumPinningTests pins the values.

/// <summary>A round header. <see cref="Dirty"/> means a local change is waiting to be pushed; a row with
/// <see cref="DeletedAt"/> set is a tombstone (kept so a pending DELETE survives restarts and a pull cannot resurrect it).</summary>
[Table("RoundRow")]
public sealed class RoundRow
{
    [PrimaryKey] public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public string CourseName { get; set; } = "";
    public Guid TeeId { get; set; }
    public string TeeName { get; set; } = "";
    public Guid TeeVersionId { get; set; }
    [Indexed] public DateTime PlayedOn { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public EntryMode EntryMode { get; set; }
    public RoundType RoundType { get; set; }
    public string? Notes { get; set; }
    public string? WeatherConditions { get; set; }
    public double? TemperatureF { get; set; }
    public double? WindSpeedMph { get; set; }
    public int? WindDirectionDegrees { get; set; }
    public string? LegacyWeather { get; set; }
    public int? DurationMinutes { get; set; }
    public double? DistanceMiles { get; set; }
    public int? StepCount { get; set; }

    /// <summary>When the golfer last changed this round on this device (sent to the server as the conflict clock).</summary>
    public DateTimeOffset ClientUpdatedAt { get; set; }
    /// <summary>The server's modified time from the last server copy stored. Null for a round never synced.</summary>
    public DateTimeOffset? ModifiedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    /// <summary>The server's version from the last server copy stored (0 until synced).</summary>
    public int Version { get; set; }
    public bool TrialCounted { get; set; }
    [Indexed] public bool Dirty { get; set; }

    public RoundRow Clone() => (RoundRow)MemberwiseClone();
}

/// <summary>One hole's result. For shot-entered holes <see cref="IsDerived"/> is true and the numbers equal the domain derivation.</summary>
[Table("HoleRow")]
public sealed class HoleRow
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed("IX_HoleRow_Round_Number", 0, Unique = true)] public Guid RoundId { get; set; }
    [Indexed("IX_HoleRow_Round_Number", 1, Unique = true)] public int HoleNumber { get; set; }
    public int EntrySequence { get; set; }
    public int Par { get; set; }
    public int Score { get; set; }
    public int Putts { get; set; }
    public bool FairwayHit { get; set; }
    public bool TeeShotLeft { get; set; }
    public bool TeeShotRight { get; set; }
    public bool TeeShotLong { get; set; }
    public bool TeeShotShort { get; set; }
    public bool Gir { get; set; }
    public int PenaltyStrokes { get; set; }
    public int GreensideSandShots { get; set; }
    public int FairwaySandShots { get; set; }
    public bool IsDerived { get; set; }

    public HoleRow Clone() => (HoleRow)MemberwiseClone();

    public HoleSummary ToSummary() => new(HoleNumber, EntrySequence, Par, Score, Putts, FairwayHit, TeeShotLeft, TeeShotRight,
        TeeShotLong, TeeShotShort, Gir, PenaltyStrokes, GreensideSandShots, FairwaySandShots);

    public static HoleRow FromSummary(Guid roundId, HoleSummary s, bool isDerived) => new()
    {
        RoundId = roundId, HoleNumber = s.HoleNumber, EntrySequence = s.EntrySequence, Par = s.Par, Score = s.Score, Putts = s.Putts,
        FairwayHit = s.FairwayHit, TeeShotLeft = s.TeeShotLeft, TeeShotRight = s.TeeShotRight, TeeShotLong = s.TeeShotLong,
        TeeShotShort = s.TeeShotShort, Gir = s.Gir, PenaltyStrokes = s.PenaltyStrokes, GreensideSandShots = s.GreensideSandShots,
        FairwaySandShots = s.FairwaySandShots, IsDerived = isDerived,
    };
}

/// <summary>One shot. Distances are yards, or feet when <see cref="StartLie"/> / <see cref="EndLie"/> is the green. Null end distance means holed.</summary>
[Table("ShotRow")]
public sealed class ShotRow
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed("IX_ShotRow_Round_Hole", 0)] public Guid RoundId { get; set; }
    [Indexed("IX_ShotRow_Round_Hole", 1)] public int HoleNumber { get; set; }
    public int Sequence { get; set; }
    public Lie StartLie { get; set; }
    public int StartDistance { get; set; }
    public Club Club { get; set; }
    public ShotType ShotType { get; set; }
    public Lie EndLie { get; set; }
    public int? EndDistance { get; set; }
    public Direction Direction { get; set; }
    public Shape? Shape { get; set; }
    public int PenaltyStrokes { get; set; }

    public ShotRow Clone() => (ShotRow)MemberwiseClone();

    public Shot ToShot() => new(Sequence, StartLie, StartDistance, Club, ShotType, EndLie, EndDistance, Direction, Shape, PenaltyStrokes);

    public static ShotRow FromShot(Guid roundId, int holeNumber, Shot s) => new()
    {
        RoundId = roundId, HoleNumber = holeNumber, Sequence = s.Sequence, StartLie = s.StartLie, StartDistance = s.StartDistance,
        Club = s.Club, ShotType = s.Type, EndLie = s.EndLie, EndDistance = s.EndDistance, Direction = s.Direction, Shape = s.Shape,
        PenaltyStrokes = s.PenaltyStrokes,
    };
}
