using CustomGolfStats.Domain.Holes;

namespace CustomGolfStats.Domain.Stats;

internal static class DrivingAccuracy
{
    /// <summary>Fairways hit / holes with a tee shot at the fairway (par 4 and 5; par 3s excluded).</summary>
    internal static Ratio Of(IEnumerable<HoleSummary> holes)
    {
        var driven = holes.Where(h => DrivingHole.IsDrivingHole(h.Par)).ToList();
        return new Ratio(driven.Count(h => h.FairwayHit), driven.Count);
    }
}
