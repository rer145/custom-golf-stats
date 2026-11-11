using CustomGolfStats.Domain.Holes;

namespace CustomGolfStats.Domain.Tests;

public static class Builders
{
    public static HoleSummary Hole(int par, int score, int putts = 2, bool fairway = false, bool gir = false,
        bool left = false, bool right = false, bool shortMiss = false, bool longMiss = false,
        int penalties = 0, int greensideSand = 0, int fairwaySand = 0, int number = 1)
        => new(number, number, par, score, putts, fairway, left, right, longMiss, shortMiss, gir,
            penalties, greensideSand, fairwaySand);
}
