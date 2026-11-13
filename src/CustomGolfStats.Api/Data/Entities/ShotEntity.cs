using CustomGolfStats.Domain;

namespace CustomGolfStats.Api.Data.Entities;

/// <summary>
/// One stroke of a shot-by-shot hole (spec §6), table <c>shots</c>. Unique per (hole result, sequence).
/// Distances are yards, or feet when the lie is <see cref="Lie.Green"/>.
/// </summary>
public class ShotEntity
{
    public Guid Id { get; set; }

    public Guid RoundId { get; set; }

    public Round Round { get; set; } = null!;

    public Guid HoleResultId { get; set; }

    public HoleResult HoleResult { get; set; } = null!;

    public int Sequence { get; set; }

    public Lie StartLie { get; set; }

    public int StartDistance { get; set; }

    public Club Club { get; set; }

    public ShotType ShotType { get; set; }

    public Lie EndLie { get; set; }

    /// <summary>Null when holed.</summary>
    public int? EndDistance { get; set; }

    public Direction Direction { get; set; }

    public Shape? Shape { get; set; }

    public int PenaltyStrokes { get; set; }
}
