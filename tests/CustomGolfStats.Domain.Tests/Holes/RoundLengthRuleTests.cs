using CustomGolfStats.Domain.Holes;

namespace CustomGolfStats.Domain.Tests.Holes;

public class RoundLengthRuleTests
{
    [Theory]
    [InlineData(9, RoundLength.Nine)]
    [InlineData(18, RoundLength.Eighteen)]
    public void Classify_exact_lengths(int holes, RoundLength expected) => Assert.Equal(expected, RoundLengthRule.Classify(holes));

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(12)] [InlineData(19)] [InlineData(27)]
    public void Classify_other_lengths_is_null(int holes) => Assert.Null(RoundLengthRule.Classify(holes));
}
