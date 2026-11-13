using CustomGolfStats.Domain;
using CustomGolfStats.Domain.Shots;
using SQLite;

namespace CustomGolfStats.App.Core.Storage.Rows;

/// <summary>An unsaved hole in progress: the shots entered so far for one (round, entry sequence), kept so a crash or a closed app does
/// not lose them. Sync ignores it. Cleared when the hole is saved or deleted.</summary>
[Table("ShotDraftRow")]
public sealed class ShotDraftRow
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed("IX_ShotDraftRow_Round_Sequence", 0)] public Guid RoundId { get; set; }
    [Indexed("IX_ShotDraftRow_Round_Sequence", 1)] public int EntrySequence { get; set; }
    public int HoleNumber { get; set; }
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

    public Shot ToShot() => new(Sequence, StartLie, StartDistance, Club, ShotType, EndLie, EndDistance, Direction, Shape, PenaltyStrokes);

    public static ShotDraftRow From(Guid roundId, int entrySequence, int holeNumber, Shot s) => new()
    {
        RoundId = roundId, EntrySequence = entrySequence, HoleNumber = holeNumber, Sequence = s.Sequence, StartLie = s.StartLie,
        StartDistance = s.StartDistance, Club = s.Club, ShotType = s.Type, EndLie = s.EndLie, EndDistance = s.EndDistance,
        Direction = s.Direction, Shape = s.Shape, PenaltyStrokes = s.PenaltyStrokes,
    };
}
