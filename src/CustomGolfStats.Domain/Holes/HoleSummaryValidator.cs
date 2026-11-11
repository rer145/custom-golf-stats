namespace CustomGolfStats.Domain.Holes;

public enum HoleSummaryError
{
    ScoreBelowOne, NegativePutts, PuttsExceedScore, NegativePenalty, NegativeSand,
    PenaltiesAndPuttsExceedScore, TeeFlagsOnPar3
}

public static class HoleSummaryValidator
{
    /// <summary>Errors for a quick-entry hole, each reported at most once. Empty list means valid.</summary>
    public static IReadOnlyList<HoleSummaryError> Validate(HoleSummary hole)
    {
        var errors = new List<HoleSummaryError>();

        if (hole.Score < 1)
            errors.Add(HoleSummaryError.ScoreBelowOne);
        if (hole.Putts < 0)
            errors.Add(HoleSummaryError.NegativePutts);
        // Every hole needs at least one non-putt stroke, so putts must be strictly below the score.
        var puttsTooMany = hole.Putts >= hole.Score;
        if (puttsTooMany)
            errors.Add(HoleSummaryError.PuttsExceedScore);
        if (hole.PenaltyStrokes < 0)
            errors.Add(HoleSummaryError.NegativePenalty);
        if (hole.GreensideSandShots < 0 || hole.FairwaySandShots < 0)
            errors.Add(HoleSummaryError.NegativeSand);
        // Putts plus penalty strokes must leave room for at least one non-putt shot (ace: Score 1, Putts 0).
        if (!puttsTooMany && hole.Putts >= 0 && hole.PenaltyStrokes >= 0 && hole.Putts + hole.PenaltyStrokes > hole.Score - 1)
            errors.Add(HoleSummaryError.PenaltiesAndPuttsExceedScore);
        if (!DrivingHole.IsDrivingHole(hole.Par) && (hole.FairwayHit || hole.TeeShotLeft || hole.TeeShotRight || hole.TeeShotLong || hole.TeeShotShort))
            errors.Add(HoleSummaryError.TeeFlagsOnPar3);

        return errors;
    }
}
