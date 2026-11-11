using CustomGolfStats.Domain.Shots;

namespace CustomGolfStats.Domain.Tests;

public static class ShotBuilders
{
    public static Shot Shot(int seq, Lie start, int startDist, Lie end, int? endDist, Club club = Club.Iron7,
        ShotType type = ShotType.Approach, Direction dir = Direction.OnTarget, int penalties = 0)
        => new(seq, start, startDist, club, type, end, endDist, dir, null, penalties);
}
