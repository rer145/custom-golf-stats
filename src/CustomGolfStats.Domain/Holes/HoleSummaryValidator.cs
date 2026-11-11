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
        if (hole.Putts > hole.Score)
            errors.Add(HoleSummaryError.PuttsExceedScore);
        if (hole.PenaltyStrokes < 0)
            errors.Add(HoleSummaryError.NegativePenalty);
        if (hole.GreensideSandShots < 0 || hole.FairwaySandShots < 0)
            errors.Add(HoleSummaryError.NegativeSand);
        // Penalty strokes are never putts or shots played, so putts plus penalties cannot exceed the score
        // (an ace has Score 1, Putts 0; a penalty on the tee shot followed by a holed putt has Score 3, Putts 1, Penalty 1).
        if (hole.Putts >= 0 && hole.PenaltyStrokes >= 0 && hole.Putts + hole.PenaltyStrokes > hole.Score)
            errors.Add(HoleSummaryError.PenaltiesAndPuttsExceedScore);
        if (hole.Par == 3 && (hole.FairwayHit || hole.TeeShotLeft || hole.TeeShotRight || hole.TeeShotLong || hole.TeeShotShort))
            errors.Add(HoleSummaryError.TeeFlagsOnPar3);

        return errors;
    }
}
