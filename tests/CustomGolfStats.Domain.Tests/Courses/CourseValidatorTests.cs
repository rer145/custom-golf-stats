using CustomGolfStats.Domain.Courses;
using CustomGolfStats.Domain.Holes;

namespace CustomGolfStats.Domain.Tests.Courses;

public class CourseValidatorTests
{
    private static IReadOnlyList<HoleInfo> Layout(int count, int par = 4, int yardage = 350) =>
        Enumerable.Range(1, count).Select(n => new HoleInfo(n, par, yardage, n)).ToList();

    private static IReadOnlyList<CourseError> Check(decimal? rating = 71.0m, int? slope = 125, IReadOnlyList<HoleInfo>? holes = null) =>
        CourseValidator.Validate(rating, slope, holes ?? Layout(18));

    [Fact]
    public void A_normal_layout_is_valid() => Assert.Empty(Check());

    [Fact]
    public void The_limits_match_what_the_API_has_always_accepted()
    {
        Assert.Equal((1, 18, 3, 6, 0, 1000, 55, 155), (CourseLimits.MinHoles, CourseLimits.MaxHoles, CourseLimits.MinPar, CourseLimits.MaxPar,
            CourseLimits.MinYardage, CourseLimits.MaxYardage, CourseLimits.MinSlope, CourseLimits.MaxSlope));
        Assert.Equal((1.0m, 99.9m, 113), (CourseLimits.MinRating, CourseLimits.MaxRating, CourseLimits.StandardSlope));
    }

    [Theory]
    [InlineData(1.0, true)]
    [InlineData(99.9, true)]
    [InlineData(0.9, false)]
    [InlineData(100.0, false)]
    public void Rating_boundaries(double rating, bool valid) =>
        Assert.Equal(valid, !Check(rating: (decimal)rating).Contains(CourseError.RatingOutOfRange));

    [Theory]
    [InlineData(55, true)]
    [InlineData(155, true)]
    [InlineData(54, false)]
    [InlineData(156, false)]
    public void Slope_boundaries(int slope, bool valid) =>
        Assert.Equal(valid, !Check(slope: slope).Contains(CourseError.SlopeOutOfRange));

    [Fact]
    public void Missing_rating_and_slope_are_reported()
    {
        var errors = Check(rating: null, slope: null);

        Assert.Contains(CourseError.RatingMissing, errors);
        Assert.Contains(CourseError.SlopeMissing, errors);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(9, true)]
    [InlineData(18, true)]
    [InlineData(0, false)]
    [InlineData(19, false)]
    public void Hole_count_boundaries(int count, bool valid) => Assert.Equal(valid, Check(holes: Layout(count)).Count == 0);

    [Theory]
    [InlineData(3, true)]
    [InlineData(6, true)]
    [InlineData(2, false)]
    [InlineData(7, false)]
    public void Par_boundaries(int par, bool valid) =>
        Assert.Equal(valid, !Check(holes: Layout(9, par: par)).Contains(CourseError.ParOutOfRange));

    [Theory]
    [InlineData(0, true)]
    [InlineData(1000, true)]
    [InlineData(-1, false)]
    [InlineData(1001, false)]
    public void Yardage_boundaries(int yardage, bool valid) =>
        Assert.Equal(valid, !Check(holes: Layout(9, yardage: yardage)).Contains(CourseError.YardageOutOfRange));

    [Fact]
    public void Stroke_indexes_must_use_1_to_n_once_each()
    {
        var repeated = Layout(9).Select(h => h with { StrokeIndex = 1 }).ToList();
        var tooHigh = Layout(9).Select(h => h with { StrokeIndex = h.Number + 1 }).ToList();

        Assert.Contains(CourseError.StrokeIndexRepeated, Check(holes: repeated));
        Assert.Contains(CourseError.StrokeIndexOutOfRange, Check(holes: tooHigh));
    }

    [Fact]
    public void Hole_numbers_must_be_1_to_18_and_unique()
    {
        var odd = new List<HoleInfo> { new(0, 4, 300, 1), new(19, 4, 300, 2), new(5, 4, 300, 3), new(5, 4, 300, 4) };

        var errors = Check(holes: odd);

        Assert.Contains(CourseError.HoleNumberOutOfRange, errors);
        Assert.Contains(CourseError.HoleNumberRepeated, errors);
    }
}
