using CustomGolfStats.Domain.Holes;

namespace CustomGolfStats.Domain.Tests.Holes;

public class HoleSummaryValidatorTests
{
    private static HoleSummaryError[] Errors(HoleSummary h) => HoleSummaryValidator.Validate(h).ToArray();

    [Fact]
    public void Hole_in_one_is_valid()
        => Assert.Empty(Errors(Builders.Hole(par: 3, score: 1, putts: 0, gir: true)));

    [Fact]
    public void Chip_in_is_valid()
        => Assert.Empty(Errors(Builders.Hole(par: 4, score: 3, putts: 0)));

    [Fact]
    public void Three_putt_is_valid()
        => Assert.Empty(Errors(Builders.Hole(par: 4, score: 6, putts: 3, fairway: true, gir: true)));

    [Fact]
    public void Score_below_one()
        => Assert.Contains(HoleSummaryError.ScoreBelowOne, Errors(Builders.Hole(par: 4, score: 0, putts: 0)));

    [Fact]
    public void Negative_putts()
        => Assert.Equal([HoleSummaryError.NegativePutts], Errors(Builders.Hole(par: 4, score: 4, putts: -1)));

    [Fact]
    public void Putts_exceed_score()
        => Assert.Contains(HoleSummaryError.PuttsExceedScore, Errors(Builders.Hole(par: 4, score: 2, putts: 3)));

    [Fact]
    public void Putts_equal_to_score_is_invalid()
        => Assert.Equal([HoleSummaryError.PuttsExceedScore], Errors(Builders.Hole(par: 3, score: 1, putts: 1)));

    [Fact]
    public void Putts_plus_penalty_leaving_no_non_putt_stroke_is_invalid()
        => Assert.Equal([HoleSummaryError.PenaltiesAndPuttsExceedScore],
            Errors(Builders.Hole(par: 4, score: 2, putts: 1, penalties: 1)));

    [Fact]
    public void Negative_penalty()
        => Assert.Equal([HoleSummaryError.NegativePenalty], Errors(Builders.Hole(par: 4, score: 4, penalties: -1)));

    [Fact]
    public void Negative_sand()
    {
        Assert.Equal([HoleSummaryError.NegativeSand], Errors(Builders.Hole(par: 4, score: 4, greensideSand: -1)));
        Assert.Equal([HoleSummaryError.NegativeSand], Errors(Builders.Hole(par: 4, score: 4, fairwaySand: -1)));
    }

    [Fact]
    public void Penalties_and_putts_exceed_score()
        => Assert.Equal([HoleSummaryError.PenaltiesAndPuttsExceedScore],
            Errors(Builders.Hole(par: 4, score: 4, putts: 2, penalties: 3)));

    [Fact]
    public void Tee_flags_on_par3()
    {
        Assert.Equal([HoleSummaryError.TeeFlagsOnPar3], Errors(Builders.Hole(par: 3, score: 3, fairway: true)));
        Assert.Equal([HoleSummaryError.TeeFlagsOnPar3], Errors(Builders.Hole(par: 3, score: 3, left: true)));
        Assert.Equal([HoleSummaryError.TeeFlagsOnPar3], Errors(Builders.Hole(par: 3, score: 3, longMiss: true)));
        Assert.Equal([HoleSummaryError.TeeFlagsOnPar3], Errors(Builders.Hole(par: 3, score: 3, right: true)));
        Assert.Equal([HoleSummaryError.TeeFlagsOnPar3], Errors(Builders.Hole(par: 3, score: 3, shortMiss: true)));
    }
}
