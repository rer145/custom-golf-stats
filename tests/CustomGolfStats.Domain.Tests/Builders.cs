using CustomGolfStats.Domain.Holes;
using CustomGolfStats.Domain.Stats;

namespace CustomGolfStats.Domain.Tests;

public static class Builders
{
    public static HoleSummary Hole(int par, int score, int putts = 2, bool fairway = false, bool gir = false,
        bool left = false, bool right = false, bool shortMiss = false, bool longMiss = false,
        int penalties = 0, int greensideSand = 0, int fairwaySand = 0, int number = 1)
        => new(number, number, par, score, putts, fairway, left, right, longMiss, shortMiss, gir,
            penalties, greensideSand, fairwaySand);

    public static readonly Guid DefaultCourseId = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    public static readonly Guid DefaultTeeId = Guid.Parse("00000000-0000-0000-0000-0000000000e1");

    public static RoundData Round(DateOnly playedOn, params HoleSummary[] holes)
        => Round(playedOn, DefaultCourseId, DefaultTeeId, holes);

    public static RoundData Round(DateOnly playedOn, Guid courseId, Guid teeId, params HoleSummary[] holes)
        => new(Guid.NewGuid(), playedOn, courseId, "Course", teeId, "Tee", holes);

    public static HoleSummary[] Holes(int count, int par, int score)
        => Enumerable.Range(1, count).Select(n => Hole(par, score, number: n)).ToArray();
}
