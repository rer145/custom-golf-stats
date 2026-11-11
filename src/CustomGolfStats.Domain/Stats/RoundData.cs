using CustomGolfStats.Domain.Holes;

namespace CustomGolfStats.Domain.Stats;

public sealed record RoundData(Guid Id, DateOnly PlayedOn, Guid CourseId, string CourseName,
    Guid TeeId, string TeeName, IReadOnlyList<HoleSummary> Holes)
{
    public int Score => Holes.Sum(h => h.Score);
    public int Par => Holes.Sum(h => h.Par);
    public int ToPar => Score - Par;
    public int Putts => Holes.Sum(h => h.Putts);
    public RoundLength? Length => RoundLengthRule.Classify(Holes.Count);
}
