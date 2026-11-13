namespace CustomGolfStats.Api.Data.Entities;

/// <summary>One hole played in a round. Unique per (round, entry sequence).</summary>
public class HoleResult
{
    public Guid Id { get; set; }

    public Guid RoundId { get; set; }

    public Round Round { get; set; } = null!;

    public int HoleNumber { get; set; }

    /// <summary>Order the hole was played in the round (differs from the hole number on a back-nine start).</summary>
    public int EntrySequence { get; set; }

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

    /// <summary>True when the summary was derived from <see cref="Shots"/>.</summary>
    public bool IsDerived { get; set; }

    public List<ShotEntity> Shots { get; set; } = [];
}
