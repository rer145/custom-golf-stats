namespace CustomGolfStats.Domain.Holes;

public static class GirRule
{
    /// <summary>Suggests a green in regulation: on the green with at least two strokes to spare for par.</summary>
    public static bool Suggest(int par, int score, int putts) => score - putts <= par - 2;
}
