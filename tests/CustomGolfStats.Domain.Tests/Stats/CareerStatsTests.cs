using CustomGolfStats.Domain.Stats;
using static CustomGolfStats.Domain.Tests.Builders;

namespace CustomGolfStats.Domain.Tests.Stats;

public class CareerStatsTests
{
    [Fact]
    public void One_row_per_year_newest_first()
    {
        var rounds = new[]
        {
            Round(new DateOnly(2023, 5, 1), Builders.Holes(18, 4, 4)),
            Round(new DateOnly(2025, 5, 1), Builders.Holes(18, 4, 5)),
            Round(new DateOnly(2025, 6, 1), Builders.Holes(9, 4, 5)),
        };
        var rows = CareerStats.ByYear(rounds);
        Assert.Equal(new[] { 2025, 2023 }, rows.Select(r => r.Year));
        var y = rows[0];
        Assert.Equal(1, y.Nine.Rounds);
        Assert.Equal(1, y.Eighteen.Rounds);
        Assert.Equal(27, y.Holes);
        Assert.Equal(27, y.ToPar);
        Assert.Equal(2.0, y.PuttsPerHole.Value);
    }

    [Fact]
    public void Length_summary_low_high_only_exact_lengths()
    {
        var rounds = new[]
        {
            Round(new DateOnly(2025, 1, 1), Builders.Holes(18, 4, 4)),  // 72
            Round(new DateOnly(2025, 1, 2), Builders.Holes(18, 4, 5)),  // 90
            Round(new DateOnly(2025, 1, 3), Builders.Holes(12, 4, 4)),  // 48 ignored
            Round(new DateOnly(2025, 1, 4), Builders.Holes(9, 4, 4)),   // 36
        };
        var y = CareerStats.ByYear(rounds).Single();
        Assert.Equal(72, y.Eighteen.Low);
        Assert.Equal(90, y.Eighteen.High);
        Assert.Equal(81.0, y.Eighteen.Average.Value);
        Assert.Equal(36, y.Nine.Low);
        Assert.Equal(36, y.Nine.High);
        Assert.Equal(1, y.Nine.Rounds);
        Assert.Equal(57, y.Holes);
    }

    [Fact]
    public void Length_summary_with_no_rounds_is_null()
    {
        var y = CareerStats.ByYear(new[] { Round(new DateOnly(2025, 1, 1), Builders.Holes(18, 4, 4)) }).Single();
        Assert.Equal(0, y.Nine.Rounds);
        Assert.Null(y.Nine.Low);
        Assert.Null(y.Nine.High);
        Assert.Null(y.Nine.Average.Value);
    }

    [Fact]
    public void Earliest_and_latest_dates()
    {
        var rounds = new[]
        {
            Round(new DateOnly(2025, 6, 1), Builders.Holes(9, 4, 4)),
            Round(new DateOnly(2025, 3, 2), Builders.Holes(9, 4, 4)),
            Round(new DateOnly(2025, 9, 9), Builders.Holes(9, 4, 4)),
        };
        var y = CareerStats.ByYear(rounds).Single();
        Assert.Equal(new DateOnly(2025, 3, 2), y.Earliest);
        Assert.Equal(new DateOnly(2025, 9, 9), y.Latest);
    }

    [Fact]
    public void Gir_and_driving_accuracy_exclude_par_3_from_driving()
    {
        var holes = new[]
        {
            Hole(3, 3, gir: true, number: 1),
            Hole(4, 4, fairway: true, gir: true, number: 2),
            Hole(5, 5, number: 3),
            Hole(4, 4, fairway: true, number: 4),
        };
        var y = CareerStats.ByYear(new[] { Round(new DateOnly(2025, 1, 1), holes) }).Single();
        Assert.Equal(new Ratio(2, 4), y.Gir);
        Assert.Equal(new Ratio(2, 3), y.DrivingAccuracy);
    }

    [Fact]
    public void Course_rows_grouped_by_course_and_tee()
    {
        var c = Guid.NewGuid();
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        var rounds = new[]
        {
            Round(new DateOnly(2025, 1, 1), c, t1, Builders.Holes(18, 4, 4)),
            Round(new DateOnly(2025, 1, 2), c, t1, Builders.Holes(18, 4, 5)),
            Round(new DateOnly(2025, 1, 3), c, t2, Builders.Holes(9, 4, 4)),
        };
        var rows = CareerStats.ByCourseAndTee(rounds);
        Assert.Equal(2, rows.Count);
        var r1 = rows.Single(r => r.TeeId == t1);
        Assert.Equal(2, r1.Eighteen.Rounds);
        Assert.Equal(36, r1.Holes);
        Assert.Equal(c, r1.CourseId);
        Assert.Equal(new DateOnly(2025, 1, 2), r1.Latest);
        Assert.Equal(1, rows.Single(r => r.TeeId == t2).Nine.Rounds);
    }

    [Fact]
    public void Course_rows_ordered_by_course_name_then_tee_name()
    {
        RoundData R(string course, string tee) =>
            new(Guid.NewGuid(), new DateOnly(2025, 1, 1), Guid.NewGuid(), course, Guid.NewGuid(), tee, Builders.Holes(9, 4, 4));
        var rows = CareerStats.ByCourseAndTee(new[] { R("B", "White"), R("A", "Red"), R("B", "Blue"), R("A", "Black") });
        Assert.Equal(new[] { "A/Black", "A/Red", "B/Blue", "B/White" }, rows.Select(r => $"{r.CourseName}/{r.TeeName}"));
    }

    [Fact]
    public void Course_par_averages_to_par()
    {
        var holes = new[]
        {
            Hole(3, 4, number: 1), Hole(3, 3, number: 2),
            Hole(4, 5, number: 3),
            Hole(5, 4, number: 4),
        };
        var row = CareerStats.ByCourseAndTee(new[] { Round(new DateOnly(2025, 1, 1), holes) }).Single();
        Assert.Equal(0.5, row.Par3ToPar.Value);
        Assert.Equal(1.0, row.Par4ToPar.Value);
        Assert.Equal(-1.0, row.Par5ToPar.Value);
    }

    [Fact]
    public void Course_par_average_with_no_such_holes_is_null()
    {
        var row = CareerStats.ByCourseAndTee(new[] { Round(new DateOnly(2025, 1, 1), Builders.Holes(9, 4, 4)) }).Single();
        Assert.Null(row.Par3ToPar.Value);
        Assert.Null(row.Par5ToPar.Value);
    }

    [Fact]
    public void Empty_input_returns_empty_lists()
    {
        Assert.Empty(CareerStats.ByYear(Array.Empty<RoundData>()));
        Assert.Empty(CareerStats.ByCourseAndTee(Array.Empty<RoundData>()));
    }
}
