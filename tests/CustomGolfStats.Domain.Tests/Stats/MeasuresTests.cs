using CustomGolfStats.Domain.Stats;

namespace CustomGolfStats.Domain.Tests.Stats;

public class MeasuresTests
{
    [Fact]
    public void Zero_denominator_is_null()
    {
        Assert.Null(new Ratio(0, 0).Value);
        Assert.Null(new Average(0, 0).Value);
    }

    [Fact]
    public void Ratio_value() => Assert.Equal(0.25, new Ratio(1, 4).Value);
}
