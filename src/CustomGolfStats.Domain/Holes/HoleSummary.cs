namespace CustomGolfStats.Domain.Holes;

public sealed record HoleSummary(int HoleNumber, int EntrySequence, int Par, int Score, int Putts,
    bool FairwayHit, bool TeeShotLeft, bool TeeShotRight, bool TeeShotLong, bool TeeShotShort,
    bool Gir, int PenaltyStrokes, int GreensideSandShots, int FairwaySandShots)
{
    public int ToPar => Score - Par;
}
