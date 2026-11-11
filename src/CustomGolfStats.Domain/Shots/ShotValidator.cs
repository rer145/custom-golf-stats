namespace CustomGolfStats.Domain.Shots;

public enum ShotError
{
    Empty, SequenceNotContiguous, InvalidStartLie, HoledBeforeLastShot,
    NotHoled, EndDistanceMismatch, NegativeDistance, NegativePenalty
}

public static class ShotValidator
{
    /// <summary>Errors for a hole's shots, each reported at most once. Empty list means valid and complete.</summary>
    public static IReadOnlyList<ShotError> Validate(IReadOnlyList<Shot> shots)
    {
        if (shots.Count == 0)
            return [ShotError.Empty];

        var errors = new List<ShotError>();
        var ordered = shots.OrderBy(s => s.Sequence).ToList();

        if (!ordered.Select(s => s.Sequence).SequenceEqual(Enumerable.Range(1, ordered.Count)))
            errors.Add(ShotError.SequenceNotContiguous);

        if (ordered.Any(s => s.StartLie is Lie.Holed or Lie.PenaltyArea or Lie.OutOfBounds))
            errors.Add(ShotError.InvalidStartLie);

        if (ordered.Take(ordered.Count - 1).Any(s => s.EndLie == Lie.Holed))
            errors.Add(ShotError.HoledBeforeLastShot);

        if (ordered[^1].EndLie != Lie.Holed)
            errors.Add(ShotError.NotHoled);

        if (ordered.Any(s => (s.EndLie == Lie.Holed) != (s.EndDistance is null)))
            errors.Add(ShotError.EndDistanceMismatch);

        if (ordered.Any(s => s.StartDistance < 0 || s.EndDistance < 0))
            errors.Add(ShotError.NegativeDistance);

        if (ordered.Any(s => s.PenaltyStrokes < 0))
            errors.Add(ShotError.NegativePenalty);

        return errors;
    }
}
