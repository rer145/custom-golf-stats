namespace CustomGolfStats.Domain.Shots;

/// <summary>One recorded shot. Distances are yards, except feet when the lie is <see cref="Lie.Green"/>.</summary>
public sealed record Shot(int Sequence, Lie StartLie, int StartDistance, Club Club, ShotType Type,
    Lie EndLie, int? EndDistance, Direction Direction, Shape? Shape, int PenaltyStrokes);
