namespace CustomGolfStats.Domain.Holes;

public static class RoundLengthRule
{
    public static RoundLength? Classify(int holeCount) => holeCount switch
    {
        9 => RoundLength.Nine,
        18 => RoundLength.Eighteen,
        _ => null,
    };
}
