using CustomGolfStats.Domain.Stats;

namespace CustomGolfStats.Domain.Tests.Stats;

public class SeasonStatsTests
{
    private static readonly DateOnly D = new(2026, 5, 1);

    private static RoundData R18(int score, DateOnly? on = null)
        => Builders.Round(on ?? D, Builders.Holes(18, 4, 4).Select((h, i) => h with { Score = i == 0 ? 4 + score - 72 : 4 }).ToArray());

    private static RoundData R9(int score)
        => Builders.Round(D, Builders.Holes(9, 4, 4).Select((h, i) => h with { Score = i == 0 ? 4 + score - 36 : 4 }).ToArray());

    [Fact]
    public void ForYear_filters_by_played_on_year()
    {
        var a = R18(80, new DateOnly(2025, 12, 31));
        var b = R18(80, new DateOnly(2026, 1, 1));
        var result = SeasonStats.ForYear([a, b], 2026);
        Assert.Equal([b], result);
    }

    [Fact]
    public void Overall_holes_and_to_par_include_odd_length_rounds()
    {
        var o = SeasonStats.Overall([R18(80), Builders.Round(D, Builders.Holes(12, 4, 5))]);
        Assert.Equal(30, o.HolesPlayed);
        Assert.Equal(8 + 12, o.ToPar);
        Assert.Equal(80, o.ScoringAverage18.Value);
    }

    [Fact]
    public void Overall_empty_has_null_average()
    {
        var o = SeasonStats.Overall([]);
        Assert.Null(o.ScoringAverage18.Value);
        Assert.Equal(0, o.HolesPlayed);
    }

    [Fact]
    public void Histogram_bucket_edges()
    {
        var s = SeasonStats.Season([R18(79), R18(80), R18(89), R18(90), R18(99), R18(100),
            R9(39), R9(40), R9(49), R9(50)]);
        Assert.Equal(6, s.Rounds18);
        Assert.Equal(4, s.Rounds9);
        Assert.Equal([1, 2, 2, 1], s.Histogram18.Select(b => b.Count).ToArray());
        Assert.Equal([(null, 79), (80, 89), (90, 99), (100, null)], s.Histogram18.Select(b => (b.Min, b.Max)).ToArray());
        Assert.Equal([1, 2, 1], s.Histogram9.Select(b => b.Count).ToArray());
        Assert.Equal([(null, 39), (40, 49), (50, null)], s.Histogram9.Select(b => (b.Min, b.Max)).ToArray());
    }

    [Fact]
    public void Odd_length_round_counted_in_RoundsOtherLength_only()
    {
        var s = SeasonStats.Season([Builders.Round(D, Builders.Holes(12, 4, 4))]);
        Assert.Equal(1, s.RoundsOtherLength);
        Assert.Equal(0, s.Rounds9);
        Assert.Equal(0, s.Rounds18);
        Assert.All(s.Histogram9.Concat(s.Histogram18), b => Assert.Equal(0, b.Count));
    }

    [Fact]
    public void Results_newest_first_with_per_par_to_par()
    {
        var older = Builders.Round(new DateOnly(2026, 4, 1),
            Builders.Hole(3, 4, putts: 2, gir: false, number: 1),
            Builders.Hole(4, 4, putts: 1, fairway: true, gir: true, number: 2),
            Builders.Hole(5, 7, putts: 3, fairway: false, number: 3));
        var newer = Builders.Round(new DateOnly(2026, 6, 1), Builders.Holes(9, 4, 5));
        var rows = SeasonStats.Results([older, newer]);
        Assert.Equal([newer.Id, older.Id], rows.Select(r => r.RoundId).ToArray());
        var row = rows[1];
        Assert.Equal(3, row.HoleCount);
        Assert.Equal(15, row.Score);
        Assert.Equal(3, row.ToPar);
        Assert.Equal(6, row.Putts);
        Assert.Equal(1, row.Par3ToPar);
        Assert.Equal(0, row.Par4ToPar);
        Assert.Equal(2, row.Par5ToPar);
        Assert.Equal(new Ratio(1, 3), row.Gir);
        Assert.Equal(new Ratio(1, 2), row.DrivingAccuracy);
        Assert.Equal("Course", row.CourseName);
        Assert.Equal("Tee", row.TeeName);
    }

    [Fact]
    public void Results_same_date_ties_break_by_id_descending()
    {
        var a = Builders.Round(D, Builders.Holes(9, 4, 4));
        var b = Builders.Round(D, Builders.Holes(9, 4, 4));
        var expected = new[] { a.Id, b.Id }.OrderByDescending(x => x).ToArray();
        Assert.Equal(expected, SeasonStats.Results([a, b]).Select(r => r.RoundId).ToArray());
        Assert.Equal(expected, SeasonStats.Results([b, a]).Select(r => r.RoundId).ToArray());
    }

    [Fact]
    public void Results_par5_null_when_round_has_no_par5()
    {
        var row = SeasonStats.Results([Builders.Round(D, Builders.Holes(9, 4, 4))])[0];
        Assert.Null(row.Par3ToPar);
        Assert.Null(row.Par5ToPar);
        Assert.Equal(0, row.Par4ToPar);
    }

    [Fact]
    public void Results_all_par3_round_has_null_driving_accuracy_value()
    {
        var row = SeasonStats.Results([Builders.Round(D, Builders.Holes(3, 3, 3))])[0];
        Assert.Null(row.DrivingAccuracy.Value);
    }
}
