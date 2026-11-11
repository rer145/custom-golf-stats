using CustomGolfStats.Domain.Holes;

namespace CustomGolfStats.Domain.Tests.Holes;

public class GirRuleTests
{
    [Theory]
    [InlineData(4, 4, 2, true)]   // reached in 2
    [InlineData(4, 5, 2, false)]  // reached in 3
    [InlineData(3, 1, 0, true)]   // ace
    [InlineData(5, 4, 1, true)]   // reached in 3 on a par 5
    [InlineData(4, 4, 0, false)]  // holed out from 4th shot, never on green in regulation
    public void Suggest_uses_score_minus_putts(int par, int score, int putts, bool expected)
        => Assert.Equal(expected, GirRule.Suggest(par, score, putts));
}
